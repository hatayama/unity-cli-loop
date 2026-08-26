package dispatcher

import (
	"io"
	"os"
	"path/filepath"
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/project"
	"github.com/hatayama/unity-cli-loop/common/skillscan"
)

const (
	managedSkillsDir     = "unity-cli-loop"
	v3MigrationSkillName = "v3-cli-invocation-migration"
	groupSkillsByDefault = false

	utf16LittleEndianBOMFirstByte  = 0xff
	utf16LittleEndianBOMSecondByte = 0xfe
	utf16BigEndianBOMFirstByte     = 0xfe
	utf16BigEndianBOMSecondByte    = 0xff
	utf16CodeUnitByteCount         = 2
	carriageReturnCodeUnit         = 0x000d
	lineFeedCodeUnit               = 0x000a
)

var targetConfigs = map[string]skillTarget{
	"claude":      {id: "claude", displayName: "Claude Code", projectDir: ".claude"},
	"codex":       {id: "codex", displayName: "Codex CLI", projectDir: ".codex"},
	"agents":      {id: "agents", displayName: "Common", projectDir: ".agents"},
	"windsurf":    {id: "windsurf", displayName: "Windsurf", projectDir: ".agents"},
	"antigravity": {id: "antigravity", displayName: "Antigravity", projectDir: ".agent"},
}

// allSkillTargetIDs enumerates every target id in the display order used by
// help output and flag parsing. targetConfigs is an unordered map, so this
// slice is the single source of truth for iteration order and for the set of
// accepted --<id> flags; help lines and parseSkillsOptions must both derive
// from it to avoid drift.
var allSkillTargetIDs = []string{"claude", "codex", "agents", "windsurf", "antigravity"}

// nonDefaultSkillTargetIDs lists targets that are excluded from the default
// target set and are only installed when explicitly requested via their
// --<id> flag.
var nonDefaultSkillTargetIDs = map[string]bool{"windsurf": true}

// defaultSkillTargetIDs is derived from allSkillTargetIDs so its order matches
// help output and so any new target added to allSkillTargetIDs is included by
// default unless it is explicitly listed in nonDefaultSkillTargetIDs.
var defaultSkillTargetIDs = buildDefaultSkillTargetIDs()

func buildDefaultSkillTargetIDs() []string {
	ids := make([]string, 0, len(allSkillTargetIDs))
	for _, id := range allSkillTargetIDs {
		if nonDefaultSkillTargetIDs[id] {
			continue
		}
		ids = append(ids, id)
	}
	return ids
}

// deprecatedSkillNames lists previously installed skill directory names that
// skills install/uninstall runs clean up. Keep cleanup names here and in the
// Unity-side SkillTargetInstaller; agent-facing V2-to-V3 CLI migration
// guidance lives in Packages/src/TemporarySkills~/v3-cli-invocation-migration.
var deprecatedSkillNames = []string{
	"uloop-wait-for-pause-point",
	"uloop-capture-window",
	"uloop-get-provider-details",
	"uloop-unity-search",
	"uloop-get-menu-items",
	"uloop-get-unity-search-providers",
	"uloop-execute-menu-item",
	"uloop-raycast",
	"uloop-record-input",
}

type skillTarget struct {
	id          string
	displayName string
	projectDir  string
}

type skillCommandOptions struct {
	global    bool
	flat      bool
	outputDir string
	targets   []skillTarget
}

type skillDefinition struct {
	name            string
	toolName        string
	content         []byte
	sourceDirectory string
}

func tryHandleSkillsRequest(args []string, startPath string, globalProjectPath string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 || args[0] != clicore.SkillsCommandName {
		return false, 0
	}
	if len(args) == 1 || clicore.IsHelpRequest(args[1:]) {
		printSkillsHelp(stdout)
		return true, 0
	}

	subcommand := args[1]
	if !isKnownSkillsSubcommand(subcommand) {
		clierrors.WriteErrorEnvelope(stderr, unknownSkillsSubcommandError(subcommand, clierrors.ErrorContext{Command: clicore.SkillsCommandName}))
		return true, 1
	}
	if clicore.ContainsHelpRequest(args[2:]) {
		printSkillsSubcommandHelp(subcommand, stdout)
		return true, 0
	}
	options, err := parseSkillsOptions(args[2:])
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{Command: clicore.SkillsCommandName})
		return true, 1
	}
	if !skillsSubcommandSupportsOutputDir(subcommand) && options.outputDir != "" {
		clierrors.WriteClassifiedError(stderr, &clierrors.ArgumentError{
			Message:     "The " + skillsOutputDirFlagName + " option is not supported for " + subcommand + ".",
			Option:      skillsOutputDirFlagName,
			Command:     clicore.SkillsCommandName,
			NextActions: []string{"Run the subcommand with target flags such as --claude instead."},
		}, clierrors.ErrorContext{Command: clicore.SkillsCommandName})
		return true, 1
	}

	projectRoot, err := resolveSkillsProjectRoot(startPath, globalProjectPath, options.global)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{Command: clicore.SkillsCommandName})
		return true, 1
	}

	if isV3MigrationSkillSubcommand(subcommand) {
		return true, runV3MigrationSkillsSubcommand(subcommand, projectRoot, options, stdout, stderr)
	}

	skills, err := collectSkillDefinitions(projectRoot)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: projectRoot, Command: clicore.SkillsCommandName})
		return true, 1
	}

	return true, runSkillsSubcommand(subcommand, projectRoot, skills, options, stdout, stderr)
}

func isKnownSkillsSubcommand(subcommand string) bool {
	switch subcommand {
	case "list", "install", "uninstall", "install-v3-migration", "uninstall-v3-migration":
		return true
	default:
		return false
	}
}

// skillsSubcommandSupportsOutputDir reports whether a skills subcommand runs
// in --output-dir mode. Help, guidance, and the routing rejection all consult
// this one predicate; it is a positive list matching the dir-mode dispatch, so
// a future subcommand is not advertised as supporting the flag until dir mode
// is actually wired for it.
func skillsSubcommandSupportsOutputDir(subcommand string) bool {
	switch subcommand {
	case "list", "install", "uninstall":
		return true
	default:
		return false
	}
}

func isV3MigrationSkillSubcommand(subcommand string) bool {
	switch subcommand {
	case "install-v3-migration", "uninstall-v3-migration":
		return true
	default:
		return false
	}
}

func groupManagedSkillsForOptions(options skillCommandOptions) bool {
	if options.flat {
		return false
	}
	return groupSkillsByDefault
}

func unknownSkillsSubcommandError(subcommand string, context clierrors.ErrorContext) clierrors.CLIError {
	return (&clierrors.ArgumentError{
		Message:     "Unknown skills command: " + subcommand,
		Received:    subcommand,
		Command:     clicore.SkillsCommandName,
		NextActions: []string{"Use `uloop skills list`, `uloop skills install`, or `uloop skills uninstall`."},
	}).ToCLIError(context)
}

func resolveSkillsProjectRoot(startPath string, explicitProjectPath string, global bool) (string, error) {
	if explicitProjectPath != "" {
		return project.ResolveExplicitProjectRoot(explicitProjectPath)
	}
	if global {
		projectRoot, err := project.FindUnityProjectRoot(startPath)
		if err == nil {
			return projectRoot, nil
		}
		return "", nil
	}
	return project.FindUnityProjectRoot(startPath)
}

func runSkillsList(projectRoot string, skills []skillDefinition, options skillCommandOptions, stdout io.Writer, stderr io.Writer) int {
	targets := options.targets
	if len(targets) == 0 {
		targets = defaultSkillTargets()
	}

	location := "Project"
	if options.global {
		location = "Global"
	}

	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "uloop Skills Status:")
	clicore.WriteLine(stdout, "")
	for _, target := range targets {
		baseDir, err := getSkillsBaseDir(projectRoot, target, options.global)
		if err != nil {
			clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: projectRoot, Command: clicore.SkillsCommandName})
			return 1
		}
		clicore.WriteFormat(stdout, "%s (%s):\n", target.displayName, location)
		clicore.WriteFormat(stdout, "Location: %s\n", baseDir)
		clicore.WriteLine(stdout, strings.Repeat("=", 50))
		for _, skill := range skills {
			status, err := getSkillStatus(baseDir, skill, groupManagedSkillsForOptions(options))
			if err != nil {
				clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: projectRoot, Command: clicore.SkillsCommandName})
				return 1
			}
			clicore.WriteFormat(stdout, "  %s %s (%s)\n", statusIcon(status), skill.name, statusText(status))
		}
		clicore.WriteLine(stdout, "")
	}
	clicore.WriteFormat(stdout, "Total: %d skills\n", len(skills))
	return 0
}

func runSkillsInstall(projectRoot string, skills []skillDefinition, options skillCommandOptions, stdout io.Writer, stderr io.Writer) int {
	clicore.WriteLine(stdout, "")
	clicore.WriteFormat(stdout, "Installing uloop skills (%s)...\n", skillLocationName(options.global))
	clicore.WriteLine(stdout, "")
	// Targets installed earlier but omitted from this invocation would keep stale
	// skill copies that contradict the CLI, so refresh every detected install.
	autoRefreshTargets, err := detectInstalledSkillTargets(projectRoot, skills, options)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: projectRoot, Command: clicore.SkillsCommandName})
		return 1
	}
	for _, autoTarget := range autoRefreshTargets {
		clicore.WriteFormat(stdout, "Auto-refreshing %s: an existing uloop skill install was detected there.\n\n", autoTarget.displayName)
	}
	for _, target := range append(append([]skillTarget{}, options.targets...), autoRefreshTargets...) {
		result, err := installSkillsForTarget(projectRoot, target, skills, options.global, groupManagedSkillsForOptions(options))
		if err != nil {
			clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: projectRoot, Command: clicore.SkillsCommandName})
			return 1
		}
		clicore.WriteFormat(stdout, "%s:\n", target.displayName)
		clicore.WriteFormat(stdout, "  Installed: %d\n", result.installed)
		clicore.WriteFormat(stdout, "  Updated: %d\n", result.updated)
		clicore.WriteFormat(stdout, "  Skipped: %d\n", result.skipped)
		if result.deprecatedRemoved > 0 {
			clicore.WriteFormat(stdout, "  Deprecated removed: %d\n", result.deprecatedRemoved)
		}
		baseDir, err := getSkillsBaseDir(projectRoot, target, options.global)
		if err != nil {
			clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: projectRoot, Command: clicore.SkillsCommandName})
			return 1
		}
		clicore.WriteFormat(stdout, "  Location: %s\n\n", baseDir)
	}
	return 0
}

func runSkillsUninstall(projectRoot string, skills []skillDefinition, options skillCommandOptions, stdout io.Writer, stderr io.Writer) int {
	clicore.WriteLine(stdout, "")
	clicore.WriteFormat(stdout, "Uninstalling uloop skills (%s)...\n", skillLocationName(options.global))
	clicore.WriteLine(stdout, "")
	for _, target := range options.targets {
		grouped := groupManagedSkillsForOptions(options)
		removed, notFound, err := uninstallSkillsForTarget(projectRoot, target, skills, options.global, grouped)
		if err != nil {
			clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: projectRoot, Command: clicore.SkillsCommandName})
			return 1
		}
		clicore.WriteFormat(stdout, "%s:\n", target.displayName)
		clicore.WriteFormat(stdout, "  Removed: %d\n", removed)
		clicore.WriteFormat(stdout, "  Not found: %d\n", notFound)
		baseDir, err := getSkillsBaseDir(projectRoot, target, options.global)
		if err != nil {
			clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: projectRoot, Command: clicore.SkillsCommandName})
			return 1
		}
		clicore.WriteFormat(stdout, "  Location: %s\n\n", baseDir)
	}
	return 0
}

// detectInstalledSkillTargets returns known targets that already contain at least one
// of the current uloop skills but were not requested in this invocation.
func detectInstalledSkillTargets(projectRoot string, skills []skillDefinition, options skillCommandOptions) ([]skillTarget, error) {
	requestedDirs := map[string]bool{}
	for _, target := range options.targets {
		requestedDirs[target.projectDir] = true
	}

	detected := []skillTarget{}
	for _, targetID := range defaultSkillTargetIDs {
		target := targetConfigs[targetID]
		if requestedDirs[target.projectDir] {
			continue
		}
		baseDir, err := getSkillsBaseDir(projectRoot, target, options.global)
		if err != nil {
			return nil, err
		}
		installed, err := hasAnyInstalledSkill(baseDir, skills)
		if err != nil {
			return nil, err
		}
		if installed {
			detected = append(detected, target)
			requestedDirs[target.projectDir] = true
		}
	}
	return detected, nil
}

func hasAnyInstalledSkill(baseDir string, skills []skillDefinition) (bool, error) {
	for _, skill := range skills {
		for _, grouped := range []bool{false, true} {
			skillFile := filepath.Join(getPreferredSkillDir(baseDir, skill.name, grouped), skillscan.SkillFileName)
			if _, err := os.Stat(skillFile); err == nil {
				return true, nil
			} else if !os.IsNotExist(err) {
				return false, err
			}
		}
	}
	return false, nil
}

type skillInstallResult struct {
	installed         int
	updated           int
	skipped           int
	deprecatedRemoved int
}

func installSkillsForTarget(projectRoot string, target skillTarget, skills []skillDefinition, global bool, grouped bool) (skillInstallResult, error) {
	result := skillInstallResult{}
	baseDir, err := getSkillsBaseDir(projectRoot, target, global)
	if err != nil {
		return skillInstallResult{}, err
	}
	deprecatedRemoved, err := removeDeprecatedSkillDirs(baseDir)
	if err != nil {
		return skillInstallResult{}, err
	}
	result.deprecatedRemoved = deprecatedRemoved
	if grouped {
		if err := migrateLegacyManagedSkills(baseDir, skills); err != nil {
			return skillInstallResult{}, err
		}
	}

	disabledTools := loadDisabledToolsForSkillInstall(projectRoot, global)
	for _, skill := range skills {
		if err := installSkillForTarget(baseDir, skill, disabledTools, grouped, &result); err != nil {
			return skillInstallResult{}, err
		}
	}
	if !grouped {
		if err := removeEmptyDir(getPreferredSkillDir(baseDir, managedSkillsDir, false)); err != nil {
			return skillInstallResult{}, err
		}
	}
	return result, nil
}

func loadDisabledToolsForSkillInstall(projectRoot string, global bool) []string {
	if global {
		return []string{}
	}
	return clicore.LoadDisabledTools(projectRoot)
}

func installSkillForTarget(
	baseDir string,
	skill skillDefinition,
	disabledTools []string,
	grouped bool,
	result *skillInstallResult,
) error {
	if isSkillDisabledByToolSettings(skill, disabledTools) {
		return removeSkillFromAllLayouts(baseDir, skill.name)
	}

	status, err := getSkillStatus(baseDir, skill, grouped)
	if err != nil {
		return err
	}
	if status == "installed" {
		result.skipped++
		return nil
	}

	destinationDir := getPreferredSkillDir(baseDir, skill.name, grouped)
	if err := syncSkillDirectory(skill.sourceDirectory, destinationDir); err != nil {
		return err
	}
	alternateDir := getPreferredSkillDir(baseDir, skill.name, !grouped)
	if err := os.RemoveAll(alternateDir); err != nil {
		return err
	}
	if status == "outdated" {
		result.updated++
		return nil
	}
	result.installed++
	return nil
}

func uninstallSkillsForTarget(projectRoot string, target skillTarget, skills []skillDefinition, global bool, grouped bool) (int, int, error) {
	removed := 0
	notFound := 0
	baseDir, err := getSkillsBaseDir(projectRoot, target, global)
	if err != nil {
		return removed, notFound, err
	}
	deprecatedRemoved, err := removeDeprecatedSkillDirsForLayout(baseDir, grouped)
	if err != nil {
		return removed, notFound, err
	}
	removed += deprecatedRemoved
	for _, skill := range skills {
		destinationDir := getPreferredSkillDir(baseDir, skill.name, grouped)
		if _, err := os.Stat(destinationDir); err != nil {
			if !os.IsNotExist(err) {
				return removed, notFound, err
			}
			notFound++
			continue
		}
		if err := os.RemoveAll(destinationDir); err != nil {
			return removed, notFound, err
		}
		removed++
	}
	return removed, notFound, nil
}
