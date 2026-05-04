package cli

import (
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/adapters/project"
)

const (
	skillsCommandName   = "skills"
	managedSkillsDir    = "unity-cli-loop"
	skillFileName       = "SKILL.md"
	uloopSettingsDir    = ".uloop"
	toolSettingsFile    = "settings.tools.json"
	manifestFileName    = "manifest.json"
	packageName         = "io.github.hatayama.uloopmcp"
	packageNameAlias    = "io.github.hatayama.uLoopMCP"
	skillSearchMaxDepth = 3

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
	"cursor":      {id: "cursor", displayName: "Cursor", projectDir: ".cursor"},
	"gemini":      {id: "gemini", displayName: "Gemini CLI", projectDir: ".gemini"},
	"agents":      {id: "agents", displayName: "Other (.agents)", projectDir: ".agents"},
	"windsurf":    {id: "windsurf", displayName: "Windsurf", projectDir: ".agents"},
	"antigravity": {id: "antigravity", displayName: "Antigravity", projectDir: ".agent"},
}

var defaultSkillTargetIDs = []string{"claude", "codex", "cursor", "gemini", "agents", "antigravity"}

var deprecatedSkillNames = []string{
	"uloop-capture-window",
	"uloop-get-provider-details",
	"uloop-unity-search",
	"uloop-get-menu-items",
	"uloop-get-unity-search-providers",
	"uloop-execute-menu-item",
}

var excludedSkillSearchDirs = map[string]bool{
	"node_modules": true,
	".git":         true,
	"Temp":         true,
	"obj":          true,
	"Build":        true,
	"Builds":       true,
	"Logs":         true,
	"Skill":        true,
}

type skillTarget struct {
	id          string
	displayName string
	projectDir  string
}

type skillCommandOptions struct {
	global  bool
	flat    bool
	targets []skillTarget
}

type skillDefinition struct {
	name            string
	toolName        string
	content         []byte
	sourceDirectory string
}

type skillSourceRoot struct {
	path    string
	cliOnly bool
}

type manifestData struct {
	Dependencies map[string]string `json:"dependencies"`
}

type toolSettingsData struct {
	DisabledTools []string `json:"disabledTools"`
}

func tryHandleSkillsRequest(args []string, startPath string, globalProjectPath string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 || args[0] != skillsCommandName {
		return false, 0
	}
	if len(args) == 1 || isHelpRequest(args[1:]) {
		printSkillsHelp(stdout)
		return true, 0
	}

	subcommand := args[1]
	if !isKnownSkillsSubcommand(subcommand) {
		writeErrorEnvelope(stderr, unknownSkillsSubcommandError(subcommand, errorContext{command: skillsCommandName}))
		return true, 1
	}
	options, err := parseSkillsOptions(args[2:])
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: skillsCommandName})
		return true, 1
	}

	projectRoot, err := resolveSkillsProjectRoot(startPath, globalProjectPath, options.global)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: skillsCommandName})
		return true, 1
	}
	skills, err := collectSkillDefinitions(projectRoot)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: skillsCommandName})
		return true, 1
	}

	switch subcommand {
	case "list":
		return true, runSkillsList(projectRoot, skills, options, stdout, stderr)
	case "install":
		if len(options.targets) == 0 {
			printSkillsTargetGuidance("install", stdout)
			return true, 0
		}
		return true, runSkillsInstall(projectRoot, skills, options, stdout, stderr)
	case "uninstall":
		if len(options.targets) == 0 {
			printSkillsTargetGuidance("uninstall", stdout)
			return true, 0
		}
		return true, runSkillsUninstall(projectRoot, skills, options, stdout, stderr)
	}
	return true, 1
}

func parseSkillsOptions(args []string) (skillCommandOptions, error) {
	options := skillCommandOptions{}
	seenTargets := map[string]bool{}
	for _, arg := range args {
		switch arg {
		case "-g", "--global":
			options.global = true
		case "--flat":
			options.flat = true
		case "--claude", "--codex", "--cursor", "--gemini", "--agents", "--windsurf", "--antigravity":
			targetID := strings.TrimPrefix(arg, "--")
			if seenTargets[targetID] {
				continue
			}
			options.targets = append(options.targets, targetConfigs[targetID])
			seenTargets[targetID] = true
		default:
			return skillCommandOptions{}, &argumentError{
				message:     "Unknown skills option: " + arg,
				option:      arg,
				command:     skillsCommandName,
				nextActions: []string{"Run `uloop skills --help` to inspect supported skills options."},
			}
		}
	}
	return options, nil
}

func isKnownSkillsSubcommand(subcommand string) bool {
	switch subcommand {
	case "list", "install", "uninstall":
		return true
	default:
		return false
	}
}

func unknownSkillsSubcommandError(subcommand string, context errorContext) cliError {
	return (&argumentError{
		message:     "Unknown skills command: " + subcommand,
		received:    subcommand,
		command:     skillsCommandName,
		nextActions: []string{"Use `uloop skills list`, `uloop skills install`, or `uloop skills uninstall`."},
	}).toCLIError(context)
}

func resolveSkillsProjectRoot(startPath string, explicitProjectPath string, global bool) (string, error) {
	if explicitProjectPath != "" {
		projectRoot, err := filepath.Abs(explicitProjectPath)
		if err != nil {
			return "", err
		}
		if !project.IsUnityProject(projectRoot) {
			return "", fmt.Errorf("not a Unity project: %s", projectRoot)
		}
		return projectRoot, nil
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

	writeLine(stdout, "")
	writeLine(stdout, "uloop Skills Status:")
	writeLine(stdout, "")
	for _, target := range targets {
		baseDir, err := getSkillsBaseDir(projectRoot, target, options.global)
		if err != nil {
			writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: skillsCommandName})
			return 1
		}
		writeFormat(stdout, "%s (%s):\n", target.displayName, location)
		writeFormat(stdout, "Location: %s\n", baseDir)
		writeLine(stdout, strings.Repeat("=", 50))
		for _, skill := range skills {
			status, err := getSkillStatus(baseDir, skill, !options.flat)
			if err != nil {
				writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: skillsCommandName})
				return 1
			}
			writeFormat(stdout, "  %s %s (%s)\n", statusIcon(status), skill.name, statusText(status))
		}
		writeLine(stdout, "")
	}
	writeFormat(stdout, "Total: %d skills\n", len(skills))
	return 0
}

func runSkillsInstall(projectRoot string, skills []skillDefinition, options skillCommandOptions, stdout io.Writer, stderr io.Writer) int {
	writeLine(stdout, "")
	writeFormat(stdout, "Installing uloop skills (%s)...\n", skillLocationName(options.global))
	writeLine(stdout, "")
	for _, target := range options.targets {
		result, err := installSkillsForTarget(projectRoot, target, skills, options.global, !options.flat)
		if err != nil {
			writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: skillsCommandName})
			return 1
		}
		writeFormat(stdout, "%s:\n", target.displayName)
		writeFormat(stdout, "  Installed: %d\n", result.installed)
		writeFormat(stdout, "  Updated: %d\n", result.updated)
		writeFormat(stdout, "  Skipped: %d\n", result.skipped)
		if result.deprecatedRemoved > 0 {
			writeFormat(stdout, "  Deprecated removed: %d\n", result.deprecatedRemoved)
		}
		baseDir, err := getSkillsBaseDir(projectRoot, target, options.global)
		if err != nil {
			writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: skillsCommandName})
			return 1
		}
		writeFormat(stdout, "  Location: %s\n\n", baseDir)
	}
	return 0
}

func runSkillsUninstall(projectRoot string, skills []skillDefinition, options skillCommandOptions, stdout io.Writer, stderr io.Writer) int {
	writeLine(stdout, "")
	writeFormat(stdout, "Uninstalling uloop skills (%s)...\n", skillLocationName(options.global))
	writeLine(stdout, "")
	for _, target := range options.targets {
		removed, notFound, err := uninstallSkillsForTarget(projectRoot, target, skills, options.global, !options.flat)
		if err != nil {
			writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: skillsCommandName})
			return 1
		}
		writeFormat(stdout, "%s:\n", target.displayName)
		writeFormat(stdout, "  Removed: %d\n", removed)
		writeFormat(stdout, "  Not found: %d\n", notFound)
		baseDir, err := getSkillsBaseDir(projectRoot, target, options.global)
		if err != nil {
			writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: skillsCommandName})
			return 1
		}
		writeFormat(stdout, "  Location: %s\n\n", baseDir)
	}
	return 0
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

	disabledTools := []string{}
	if !global {
		disabledTools = loadDisabledTools(projectRoot)
	}
	for _, skill := range skills {
		if isSkillDisabledByToolSettings(skill, disabledTools) {
			if err := removeSkillFromAllLayouts(baseDir, skill.name); err != nil {
				return skillInstallResult{}, err
			}
			continue
		}

		status, err := getSkillStatus(baseDir, skill, grouped)
		if err != nil {
			return skillInstallResult{}, err
		}
		destinationDir := getPreferredSkillDir(baseDir, skill.name, grouped)
		if status == "installed" {
			result.skipped++
			continue
		}
		if err := syncSkillDirectory(skill.sourceDirectory, destinationDir); err != nil {
			return skillInstallResult{}, err
		}
		alternateDir := getPreferredSkillDir(baseDir, skill.name, !grouped)
		if err := os.RemoveAll(alternateDir); err != nil {
			return skillInstallResult{}, err
		}
		if status == "outdated" {
			result.updated++
			continue
		}
		result.installed++
	}
	if !grouped {
		if err := removeEmptyDir(getPreferredSkillDir(baseDir, managedSkillsDir, false)); err != nil {
			return skillInstallResult{}, err
		}
	}
	return result, nil
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
