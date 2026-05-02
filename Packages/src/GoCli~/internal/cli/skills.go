package cli

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"sort"
	"strings"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/project"
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
		return true, runSkillsList(projectRoot, skills, options, stdout)
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
	default:
		writeErrorEnvelope(stderr, (&argumentError{
			message:     "Unknown skills command: " + subcommand,
			received:    subcommand,
			command:     skillsCommandName,
			nextActions: []string{"Use `uloop skills list`, `uloop skills install`, or `uloop skills uninstall`."},
		}).toCLIError(errorContext{projectRoot: projectRoot, command: skillsCommandName}))
		return true, 1
	}
}

func parseSkillsOptions(args []string) (skillCommandOptions, error) {
	options := skillCommandOptions{}
	for _, arg := range args {
		switch arg {
		case "-g", "--global":
			options.global = true
		case "--flat":
			options.flat = true
		case "--claude", "--codex", "--cursor", "--gemini", "--agents", "--windsurf", "--antigravity":
			targetID := strings.TrimPrefix(arg, "--")
			options.targets = append(options.targets, targetConfigs[targetID])
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

func runSkillsList(projectRoot string, skills []skillDefinition, options skillCommandOptions, stdout io.Writer) int {
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
		baseDir := getSkillsBaseDir(projectRoot, target, options.global)
		writeFormat(stdout, "%s (%s):\n", target.displayName, location)
		writeFormat(stdout, "Location: %s\n", baseDir)
		writeLine(stdout, strings.Repeat("=", 50))
		for _, skill := range skills {
			status := getSkillStatus(baseDir, skill, !options.flat)
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
		writeFormat(stdout, "  Location: %s\n\n", getSkillsBaseDir(projectRoot, target, options.global))
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
		writeFormat(stdout, "  Location: %s\n\n", getSkillsBaseDir(projectRoot, target, options.global))
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
	baseDir := getSkillsBaseDir(projectRoot, target, global)
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

		status := getSkillStatus(baseDir, skill, grouped)
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
	baseDir := getSkillsBaseDir(projectRoot, target, global)
	deprecatedRemoved, err := removeDeprecatedSkillDirs(baseDir)
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

func collectSkillDefinitions(projectRoot string) ([]skillDefinition, error) {
	skills := []skillDefinition{}
	seen := map[string]bool{}
	for _, sourceRoot := range enumerateSkillSourceRoots(projectRoot) {
		discovered, err := scanSkillSourceRoot(sourceRoot)
		if err != nil {
			return nil, err
		}
		for _, skill := range discovered {
			if seen[skill.name] {
				continue
			}
			seen[skill.name] = true
			skills = append(skills, skill)
		}
	}
	sort.Slice(skills, func(left int, right int) bool {
		return skills[left].name < skills[right].name
	})
	return skills, nil
}

func collectInternalSkillToolNames(projectRoot string) map[string]bool {
	toolNames := map[string]bool{}
	for _, sourceRoot := range enumerateSkillSourceRoots(projectRoot) {
		for _, toolName := range scanInternalSkillToolNames(sourceRoot) {
			toolNames[toolName] = true
		}
	}
	return toolNames
}

func enumerateSkillSourceRoots(projectRoot string) []skillSourceRoot {
	sourceRoots := []skillSourceRoot{}
	seen := map[string]bool{}
	addSourceRoot := func(path string, cliOnly bool) {
		if path == "" {
			return
		}
		absolutePath, err := filepath.Abs(path)
		if err != nil || seen[absolutePath] {
			return
		}
		seen[absolutePath] = true
		sourceRoots = append(sourceRoots, skillSourceRoot{path: absolutePath, cliOnly: cliOnly})
	}

	packageRoot := resolvePackageRoot(projectRoot)
	addSourceRoot(packageRoot, false)
	addSourceRoot(filepath.Join(projectRoot, "Packages/src/GoCli~/internal/cli/skill-definitions/cli-only"), true)
	addSourceRoot(filepath.Join(projectRoot, "Assets"), false)
	for _, packageRoot := range enumerateDirectProjectPackageRoots(projectRoot) {
		addSourceRoot(packageRoot, false)
	}
	for _, packageRoot := range resolveManifestLocalPackageRoots(projectRoot) {
		addSourceRoot(packageRoot, false)
	}
	for _, packageRoot := range resolveDependencyPackageCacheRoots(projectRoot) {
		addSourceRoot(packageRoot, false)
	}
	return sourceRoots
}

func scanSkillSourceRoot(sourceRoot skillSourceRoot) ([]skillDefinition, error) {
	if _, err := os.Stat(sourceRoot.path); err != nil {
		return []skillDefinition{}, nil
	}

	scanRoots := []string{sourceRoot.path}
	if !sourceRoot.cliOnly {
		scanRoots = findEditorFolders(sourceRoot.path, skillSearchMaxDepth)
	}

	skills := []skillDefinition{}
	for _, scanRoot := range scanRoots {
		discovered, err := scanSkillDirectories(scanRoot)
		if err != nil {
			return nil, err
		}
		skills = append(skills, discovered...)
	}
	return skills, nil
}

func scanInternalSkillToolNames(sourceRoot skillSourceRoot) []string {
	if _, err := os.Stat(sourceRoot.path); err != nil {
		return []string{}
	}

	scanRoots := []string{sourceRoot.path}
	if !sourceRoot.cliOnly {
		scanRoots = findEditorFolders(sourceRoot.path, skillSearchMaxDepth)
	}

	toolNames := []string{}
	for _, scanRoot := range scanRoots {
		toolNames = append(toolNames, scanInternalSkillDirectories(scanRoot)...)
	}
	return toolNames
}

func scanSkillDirectories(searchRoot string) ([]skillDefinition, error) {
	skills := []skillDefinition{}
	err := filepath.WalkDir(searchRoot, func(path string, entry os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if !entry.IsDir() {
			if entry.Name() != skillFileName {
				return nil
			}
			skill, ok, err := readSkillDefinition(filepath.Dir(path))
			if err != nil {
				return err
			}
			if ok {
				skills = append(skills, skill)
			}
			return nil
		}
		if excludedSkillSearchDirs[entry.Name()] && entry.Name() != "Skill" {
			return filepath.SkipDir
		}
		if entry.Name() != "Skill" {
			return nil
		}

		skill, ok, err := readSkillDefinition(path)
		if err != nil {
			return err
		}
		if !ok {
			return filepath.SkipDir
		}
		skills = append(skills, skill)
		return filepath.SkipDir
	})
	if err != nil {
		return nil, err
	}
	return skills, nil
}

func scanInternalSkillDirectories(searchRoot string) []string {
	toolNames := []string{}
	_ = filepath.WalkDir(searchRoot, func(path string, entry os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return nil
		}
		if !entry.IsDir() {
			if entry.Name() != skillFileName {
				return nil
			}
			toolName, ok := readInternalSkillToolName(filepath.Dir(path))
			if ok {
				toolNames = append(toolNames, toolName)
			}
			return nil
		}
		if excludedSkillSearchDirs[entry.Name()] && entry.Name() != "Skill" {
			return filepath.SkipDir
		}
		if entry.Name() != "Skill" {
			return nil
		}

		toolName, ok := readInternalSkillToolName(path)
		if ok {
			toolNames = append(toolNames, toolName)
		}
		return filepath.SkipDir
	})
	return toolNames
}

func readSkillDefinition(skillDirectory string) (skillDefinition, bool, error) {
	skillPath := filepath.Join(skillDirectory, skillFileName)
	content, err := os.ReadFile(skillPath)
	if err != nil {
		if os.IsNotExist(err) {
			return skillDefinition{}, false, nil
		}
		return skillDefinition{}, false, err
	}
	frontmatter := parseSkillFrontmatter(string(content))
	if strings.EqualFold(frontmatter["internal"], "true") {
		return skillDefinition{}, false, nil
	}
	name := frontmatter["name"]
	if name == "" {
		name = fallbackSkillName(skillDirectory)
	}
	if !isSafeSkillName(name) {
		return skillDefinition{}, false, nil
	}
	return skillDefinition{
		name:            name,
		toolName:        frontmatter["toolName"],
		content:         content,
		sourceDirectory: skillDirectory,
	}, true, nil
}

func readInternalSkillToolName(skillDirectory string) (string, bool) {
	skillPath := filepath.Join(skillDirectory, skillFileName)
	content, err := os.ReadFile(skillPath)
	if err != nil {
		return "", false
	}
	frontmatter := parseSkillFrontmatter(string(content))
	if !strings.EqualFold(frontmatter["internal"], "true") {
		return "", false
	}
	if frontmatter["toolName"] != "" {
		return frontmatter["toolName"], true
	}
	name := frontmatter["name"]
	if strings.HasPrefix(name, "uloop-") {
		return strings.TrimPrefix(name, "uloop-"), true
	}
	return "", false
}

func fallbackSkillName(skillDirectory string) string {
	if filepath.Base(skillDirectory) == "Skill" {
		return filepath.Base(filepath.Dir(skillDirectory))
	}
	return filepath.Base(skillDirectory)
}

func parseSkillFrontmatter(content string) map[string]string {
	result := map[string]string{}
	if !strings.HasPrefix(content, "---") {
		return result
	}
	parts := strings.SplitN(content, "---", 3)
	if len(parts) < 3 {
		return result
	}
	for _, line := range strings.Split(parts[1], "\n") {
		key, value, ok := strings.Cut(line, ":")
		if !ok {
			continue
		}
		result[strings.TrimSpace(key)] = strings.Trim(strings.TrimSpace(value), `"`)
	}
	return result
}

func syncSkillDirectory(sourceDir string, destinationDir string) error {
	parentDir := filepath.Dir(destinationDir)
	if err := os.MkdirAll(parentDir, 0o755); err != nil {
		return err
	}
	tempDir, err := os.MkdirTemp(parentDir, filepath.Base(destinationDir)+".tmp-")
	if err != nil {
		return err
	}

	replaced := false
	defer func() {
		if !replaced {
			_ = os.RemoveAll(tempDir)
		}
	}()
	if err := copySkillDirectory(sourceDir, tempDir); err != nil {
		return err
	}
	if err := os.RemoveAll(destinationDir); err != nil {
		return err
	}
	if err := os.Rename(tempDir, destinationDir); err != nil {
		return err
	}
	replaced = true
	return nil
}

func copySkillDirectory(sourceDir string, destinationDir string) error {
	return filepath.WalkDir(sourceDir, func(path string, entry os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		relativePath, err := filepath.Rel(sourceDir, path)
		if err != nil {
			return err
		}
		if relativePath == "." {
			return os.MkdirAll(destinationDir, 0o755)
		}
		if shouldSkipSkillFile(entry.Name()) {
			if entry.IsDir() {
				return filepath.SkipDir
			}
			return nil
		}

		destinationPath := filepath.Join(destinationDir, relativePath)
		if entry.IsDir() {
			return os.MkdirAll(destinationPath, 0o755)
		}

		content, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		return os.WriteFile(destinationPath, content, 0o644)
	})
}

func getSkillStatus(baseDir string, skill skillDefinition, grouped bool) string {
	skillDir := getPreferredSkillDir(baseDir, skill.name, grouped)
	if _, err := os.Stat(filepath.Join(skillDir, skillFileName)); err != nil {
		return "not_installed"
	}
	if isInstalledSkillOutdated(skillDir, skill) {
		return "outdated"
	}
	return "installed"
}

func isInstalledSkillOutdated(installedDir string, skill skillDefinition) bool {
	installedContent, err := os.ReadFile(filepath.Join(installedDir, skillFileName))
	if err != nil {
		return true
	}
	if !bytes.Equal(installedContent, skill.content) {
		return true
	}

	expectedFiles := collectComparableSkillFiles(skill.sourceDirectory)
	installedFiles := collectComparableSkillFiles(installedDir)
	if len(expectedFiles) != len(installedFiles) {
		return true
	}
	for relativePath, expectedContent := range expectedFiles {
		installedContent, ok := installedFiles[relativePath]
		if !ok || !bytes.Equal(expectedContent, installedContent) {
			return true
		}
	}
	return false
}

func collectComparableSkillFiles(root string) map[string][]byte {
	files := map[string][]byte{}
	_ = filepath.WalkDir(root, func(path string, entry os.DirEntry, walkErr error) error {
		if walkErr != nil || entry.IsDir() || shouldSkipSkillFile(entry.Name()) {
			return nil
		}
		relativePath, err := filepath.Rel(root, path)
		if err != nil || relativePath == skillFileName {
			return nil
		}
		content, err := os.ReadFile(path)
		if err != nil {
			return nil
		}
		files[relativePath] = content
		return nil
	})
	return files
}

func getSkillsBaseDir(projectRoot string, target skillTarget, global bool) string {
	if global {
		homeDir, err := os.UserHomeDir()
		if err == nil {
			return filepath.Join(homeDir, target.projectDir, "skills")
		}
		return filepath.Join(target.projectDir, "skills")
	}
	return filepath.Join(projectRoot, target.projectDir, "skills")
}

func getPreferredSkillDir(baseDir string, skillName string, grouped bool) string {
	if grouped {
		return filepath.Join(baseDir, managedSkillsDir, skillName)
	}
	return filepath.Join(baseDir, skillName)
}

func migrateLegacyManagedSkills(baseDir string, skills []skillDefinition) error {
	for _, skill := range skills {
		legacyDir := getPreferredSkillDir(baseDir, skill.name, false)
		managedDir := getPreferredSkillDir(baseDir, skill.name, true)
		if _, err := os.Stat(legacyDir); err != nil {
			if os.IsNotExist(err) {
				continue
			}
			return err
		}
		if _, err := os.Stat(managedDir); err == nil {
			continue
		} else if !os.IsNotExist(err) {
			return err
		}
		if err := os.MkdirAll(filepath.Dir(managedDir), 0o755); err != nil {
			return err
		}
		if err := os.Rename(legacyDir, managedDir); err != nil {
			return err
		}
	}
	return nil
}

func removeDeprecatedSkillDirs(baseDir string) (int, error) {
	removed := 0
	for _, skillName := range deprecatedSkillNames {
		for _, grouped := range []bool{true, false} {
			skillDir := getPreferredSkillDir(baseDir, skillName, grouped)
			exists, err := removeDirIfExists(skillDir)
			if err != nil {
				return removed, err
			}
			if exists {
				removed++
			}
		}
	}
	return removed, nil
}

func removeSkillFromAllLayouts(baseDir string, skillName string) error {
	for _, grouped := range []bool{true, false} {
		if _, err := removeDirIfExists(getPreferredSkillDir(baseDir, skillName, grouped)); err != nil {
			return err
		}
	}
	return nil
}

func removeDirIfExists(path string) (bool, error) {
	if _, err := os.Stat(path); err != nil {
		if os.IsNotExist(err) {
			return false, nil
		}
		return false, err
	}
	if err := os.RemoveAll(path); err != nil {
		return false, err
	}
	return true, nil
}

func removeEmptyDir(path string) error {
	entries, err := os.ReadDir(path)
	if err != nil {
		if os.IsNotExist(err) {
			return nil
		}
		return err
	}
	if len(entries) > 0 {
		return nil
	}
	return os.Remove(path)
}

func loadDisabledTools(projectRoot string) []string {
	settingsPath := filepath.Join(projectRoot, uloopSettingsDir, toolSettingsFile)
	content, err := os.ReadFile(settingsPath)
	if err != nil || len(strings.TrimSpace(string(content))) == 0 {
		return []string{}
	}

	settings := toolSettingsData{}
	if err := json.Unmarshal(content, &settings); err != nil {
		return []string{}
	}
	if settings.DisabledTools == nil {
		return []string{}
	}
	return settings.DisabledTools
}

func isSkillDisabledByToolSettings(skill skillDefinition, disabledTools []string) bool {
	if len(disabledTools) == 0 {
		return false
	}
	toolName := skill.toolName
	if toolName == "" && strings.HasPrefix(skill.name, "uloop-") {
		toolName = strings.TrimPrefix(skill.name, "uloop-")
	}
	if toolName == "" {
		return false
	}
	for _, disabledTool := range disabledTools {
		if disabledTool == toolName {
			return true
		}
	}
	return false
}

func findEditorFolders(basePath string, maxDepth int) []string {
	editorFolders := []string{}
	var scan func(string, int)
	scan = func(currentPath string, depth int) {
		if depth > maxDepth {
			return
		}
		entries, err := os.ReadDir(currentPath)
		if err != nil {
			return
		}
		for _, entry := range entries {
			if !entry.IsDir() || excludedSkillSearchDirs[entry.Name()] {
				continue
			}
			fullPath := filepath.Join(currentPath, entry.Name())
			if entry.Name() == "Editor" {
				editorFolders = append(editorFolders, fullPath)
				continue
			}
			scan(fullPath, depth+1)
		}
	}
	scan(basePath, 0)
	sort.Strings(editorFolders)
	return editorFolders
}

func enumerateDirectProjectPackageRoots(projectRoot string) []string {
	packagesRoot := filepath.Join(projectRoot, "Packages")
	entries, err := os.ReadDir(packagesRoot)
	if err != nil {
		return []string{}
	}
	packageRoots := []string{}
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		packageRoots = append(packageRoots, resolveSkillSearchRootCandidate(filepath.Join(packagesRoot, entry.Name())))
	}
	sort.Strings(packageRoots)
	return packageRoots
}

func resolveManifestLocalPackageRoots(projectRoot string) []string {
	dependencies := readManifestDependencies(projectRoot)
	if len(dependencies) == 0 {
		return []string{}
	}
	packageRoots := []string{}
	for _, dependencyValue := range dependencies {
		localPath := resolveLocalDependencyPath(dependencyValue, projectRoot)
		if localPath == "" {
			continue
		}
		packageRoots = append(packageRoots, resolveSkillSearchRootCandidate(localPath))
	}
	sort.Strings(packageRoots)
	return packageRoots
}

func resolveDependencyPackageCacheRoots(projectRoot string) []string {
	dependencies := readManifestDependencies(projectRoot)
	if len(dependencies) == 0 {
		return []string{}
	}
	dependencyNames := map[string]bool{}
	for dependencyName := range dependencies {
		dependencyNames[strings.ToLower(dependencyName)] = true
	}
	packageCacheDir := filepath.Join(projectRoot, "Library", "PackageCache")
	entries, err := os.ReadDir(packageCacheDir)
	if err != nil {
		return []string{}
	}
	packageRoots := []string{}
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		dependencyName := entry.Name()
		if separatorIndex := strings.Index(dependencyName, "@"); separatorIndex >= 0 {
			dependencyName = dependencyName[:separatorIndex]
		}
		if !dependencyNames[strings.ToLower(dependencyName)] {
			continue
		}
		packageRoots = append(packageRoots, resolveSkillSearchRootCandidate(filepath.Join(packageCacheDir, entry.Name())))
	}
	sort.Strings(packageRoots)
	return packageRoots
}

func resolvePackageRoot(projectRoot string) string {
	candidates := []string{
		filepath.Join(projectRoot, "Packages", "src"),
		filepath.Join(projectRoot, "Packages", packageName),
		filepath.Join(projectRoot, "Packages", packageNameAlias),
	}
	for _, candidate := range candidates {
		if resolvedRoot := resolvePackageRootCandidate(candidate); resolvedRoot != "" {
			return resolvedRoot
		}
	}

	return resolvePackageCacheRoot(projectRoot)
}

func resolvePackageCacheRoot(projectRoot string) string {
	packageCacheDir := filepath.Join(projectRoot, "Library", "PackageCache")
	entries, err := os.ReadDir(packageCacheDir)
	if err != nil {
		return ""
	}
	for _, entry := range entries {
		if !entry.IsDir() || !isTargetPackageCacheDir(entry.Name()) {
			continue
		}
		if resolvedRoot := resolvePackageRootCandidate(filepath.Join(packageCacheDir, entry.Name())); resolvedRoot != "" {
			return resolvedRoot
		}
	}
	return ""
}

func resolvePackageRootCandidate(candidate string) string {
	if _, err := os.Stat(candidate); err != nil {
		return ""
	}
	directToolsPath := filepath.Join(candidate, "Editor", "Api", "McpTools")
	if _, err := os.Stat(directToolsPath); err == nil {
		return candidate
	}
	nestedRoot := filepath.Join(candidate, "Packages", "src")
	nestedToolsPath := filepath.Join(nestedRoot, "Editor", "Api", "McpTools")
	if _, err := os.Stat(nestedToolsPath); err == nil {
		return nestedRoot
	}
	return ""
}

func resolveSkillSearchRootCandidate(candidate string) string {
	nestedRoot := filepath.Join(candidate, "Packages", "src")
	if _, err := os.Stat(nestedRoot); err == nil {
		return nestedRoot
	}
	return candidate
}

func readManifestDependencies(projectRoot string) map[string]string {
	manifestPath := filepath.Join(projectRoot, "Packages", manifestFileName)
	content, err := os.ReadFile(manifestPath)
	if err != nil {
		return map[string]string{}
	}
	manifest := manifestData{}
	if err := json.Unmarshal(content, &manifest); err != nil {
		return map[string]string{}
	}
	if manifest.Dependencies == nil {
		return map[string]string{}
	}
	return manifest.Dependencies
}

func resolveLocalDependencyPath(dependencyValue string, projectRoot string) string {
	rawPath := ""
	switch {
	case strings.HasPrefix(dependencyValue, "file:"):
		rawPath = strings.TrimPrefix(dependencyValue, "file:")
	case strings.HasPrefix(dependencyValue, "path:"):
		rawPath = strings.TrimPrefix(dependencyValue, "path:")
	default:
		return ""
	}
	rawPath = strings.TrimSpace(rawPath)
	if rawPath == "" {
		return ""
	}
	rawPath = strings.TrimPrefix(rawPath, "//")
	if filepath.IsAbs(rawPath) {
		return rawPath
	}
	return filepath.Join(projectRoot, rawPath)
}

func isTargetPackageCacheDir(dirName string) bool {
	normalizedName := strings.ToLower(dirName)
	return strings.HasPrefix(normalizedName, strings.ToLower(packageName)+"@") ||
		strings.HasPrefix(normalizedName, strings.ToLower(packageNameAlias)+"@")
}

func defaultSkillTargets() []skillTarget {
	targets := make([]skillTarget, 0, len(defaultSkillTargetIDs))
	for _, targetID := range defaultSkillTargetIDs {
		targets = append(targets, targetConfigs[targetID])
	}
	return targets
}

func shouldSkipSkillFile(name string) bool {
	return name == ".DS_Store" || strings.HasSuffix(name, ".meta")
}

func isSafeSkillName(name string) bool {
	return name != "" && name != "." && name != ".." &&
		!strings.Contains(name, "/") && !strings.Contains(name, `\`)
}

func skillLocationName(global bool) string {
	if global {
		return "global"
	}
	return "project"
}

func statusIcon(status string) string {
	switch status {
	case "installed":
		return "+"
	case "outdated":
		return "^"
	default:
		return "-"
	}
}

func statusText(status string) string {
	switch status {
	case "installed":
		return "installed"
	case "outdated":
		return "outdated"
	default:
		return "not installed"
	}
}

func printSkillsHelp(stdout io.Writer) {
	writeLine(stdout, "Usage:")
	writeLine(stdout, "  uloop skills list [options]")
	writeLine(stdout, "  uloop skills install [options]")
	writeLine(stdout, "  uloop skills uninstall [options]")
}

func printSkillsTargetGuidance(command string, stdout io.Writer) {
	writeFormat(stdout, "\nPlease specify at least one target for '%s':\n\n", command)
	writeLine(stdout, "Available targets:")
	writeLine(stdout, "  --claude")
	writeLine(stdout, "  --codex")
	writeLine(stdout, "  --cursor")
	writeLine(stdout, "  --gemini")
	writeLine(stdout, "  --agents")
	writeLine(stdout, "  --windsurf")
	writeLine(stdout, "  --antigravity")
}
