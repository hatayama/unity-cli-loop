package cli

import (
	"bytes"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"sort"
	"strings"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/project"
)

const (
	skillsCommandName = "skills"
	managedSkillsDir  = "unity-cli-loop"
	skillFileName     = "SKILL.md"
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
	content         []byte
	sourceDirectory string
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
		fmt.Fprintln(stderr, err.Error())
		return true, 1
	}

	projectRoot, err := resolveSkillsProjectRoot(startPath, globalProjectPath, options.global)
	if err != nil {
		fmt.Fprintln(stderr, err.Error())
		return true, 1
	}
	skills, err := collectSkillDefinitions(projectRoot)
	if err != nil {
		fmt.Fprintln(stderr, err.Error())
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
		fmt.Fprintf(stderr, "unknown skills command: %s\n", subcommand)
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
			return skillCommandOptions{}, fmt.Errorf("unknown skills option: %s", arg)
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

	fmt.Fprintln(stdout, "")
	fmt.Fprintln(stdout, "uloop Skills Status:")
	fmt.Fprintln(stdout, "")
	for _, target := range targets {
		baseDir := getSkillsBaseDir(projectRoot, target, options.global)
		fmt.Fprintf(stdout, "%s (%s):\n", target.displayName, location)
		fmt.Fprintf(stdout, "Location: %s\n", baseDir)
		fmt.Fprintln(stdout, strings.Repeat("=", 50))
		for _, skill := range skills {
			status := getSkillStatus(baseDir, skill, !options.flat)
			fmt.Fprintf(stdout, "  %s %s (%s)\n", statusIcon(status), skill.name, statusText(status))
		}
		fmt.Fprintln(stdout, "")
	}
	fmt.Fprintf(stdout, "Total: %d skills\n", len(skills))
	return 0
}

func runSkillsInstall(projectRoot string, skills []skillDefinition, options skillCommandOptions, stdout io.Writer, stderr io.Writer) int {
	fmt.Fprintln(stdout, "")
	fmt.Fprintf(stdout, "Installing uloop skills (%s)...\n", skillLocationName(options.global))
	fmt.Fprintln(stdout, "")
	for _, target := range options.targets {
		result, err := installSkillsForTarget(projectRoot, target, skills, options.global, !options.flat)
		if err != nil {
			fmt.Fprintln(stderr, err.Error())
			return 1
		}
		fmt.Fprintf(stdout, "%s:\n", target.displayName)
		fmt.Fprintf(stdout, "  Installed: %d\n", result.installed)
		fmt.Fprintf(stdout, "  Updated: %d\n", result.updated)
		fmt.Fprintf(stdout, "  Skipped: %d\n", result.skipped)
		fmt.Fprintf(stdout, "  Location: %s\n\n", getSkillsBaseDir(projectRoot, target, options.global))
	}
	return 0
}

func runSkillsUninstall(projectRoot string, skills []skillDefinition, options skillCommandOptions, stdout io.Writer, stderr io.Writer) int {
	fmt.Fprintln(stdout, "")
	fmt.Fprintf(stdout, "Uninstalling uloop skills (%s)...\n", skillLocationName(options.global))
	fmt.Fprintln(stdout, "")
	for _, target := range options.targets {
		removed, notFound, err := uninstallSkillsForTarget(projectRoot, target, skills, options.global, !options.flat)
		if err != nil {
			fmt.Fprintln(stderr, err.Error())
			return 1
		}
		fmt.Fprintf(stdout, "%s:\n", target.displayName)
		fmt.Fprintf(stdout, "  Removed: %d\n", removed)
		fmt.Fprintf(stdout, "  Not found: %d\n", notFound)
		fmt.Fprintf(stdout, "  Location: %s\n\n", getSkillsBaseDir(projectRoot, target, options.global))
	}
	return 0
}

type skillInstallResult struct {
	installed int
	updated   int
	skipped   int
}

func installSkillsForTarget(projectRoot string, target skillTarget, skills []skillDefinition, global bool, grouped bool) (skillInstallResult, error) {
	result := skillInstallResult{}
	baseDir := getSkillsBaseDir(projectRoot, target, global)
	for _, skill := range skills {
		status := getSkillStatus(baseDir, skill, grouped)
		destinationDir := getPreferredSkillDir(baseDir, skill.name, grouped)
		if status == "installed" {
			result.skipped++
			continue
		}
		if err := os.RemoveAll(destinationDir); err != nil {
			return skillInstallResult{}, err
		}
		if err := copySkillDirectory(skill.sourceDirectory, destinationDir); err != nil {
			return skillInstallResult{}, err
		}
		if status == "outdated" {
			result.updated++
			continue
		}
		result.installed++
	}
	return result, nil
}

func uninstallSkillsForTarget(projectRoot string, target skillTarget, skills []skillDefinition, global bool, grouped bool) (int, int, error) {
	removed := 0
	notFound := 0
	baseDir := getSkillsBaseDir(projectRoot, target, global)
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
	sourceRoots := []string{
		filepath.Join(projectRoot, "Packages/src/Editor/Api/McpTools"),
		filepath.Join(projectRoot, "Packages/src/GoCli~/internal/cli/skill-definitions/cli-only"),
	}

	skills := []skillDefinition{}
	seen := map[string]bool{}
	for _, sourceRoot := range sourceRoots {
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

func scanSkillSourceRoot(sourceRoot string) ([]skillDefinition, error) {
	if _, err := os.Stat(sourceRoot); err != nil {
		return []skillDefinition{}, nil
	}

	skills := []skillDefinition{}
	err := filepath.WalkDir(sourceRoot, func(path string, entry os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if !entry.IsDir() || entry.Name() != "Skill" {
			return nil
		}

		skillPath := filepath.Join(path, skillFileName)
		content, err := os.ReadFile(skillPath)
		if err != nil {
			return nil
		}
		frontmatter := parseSkillFrontmatter(string(content))
		if frontmatter["internal"] == "true" {
			return filepath.SkipDir
		}
		name := frontmatter["name"]
		if name == "" {
			name = filepath.Base(filepath.Dir(path))
		}
		if !isSafeSkillName(name) {
			return filepath.SkipDir
		}
		skills = append(skills, skillDefinition{
			name:            name,
			content:         content,
			sourceDirectory: path,
		})
		return filepath.SkipDir
	})
	if err != nil {
		return nil, err
	}
	return skills, nil
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
	fallbackDir := getPreferredSkillDir(baseDir, skill.name, !grouped)
	for _, candidate := range []string{skillDir, fallbackDir} {
		if _, err := os.Stat(filepath.Join(candidate, skillFileName)); err != nil {
			continue
		}
		if isInstalledSkillOutdated(candidate, skill) {
			return "outdated"
		}
		return "installed"
	}
	return "not_installed"
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
	fmt.Fprintln(stdout, "Usage:")
	fmt.Fprintln(stdout, "  uloop skills list [options]")
	fmt.Fprintln(stdout, "  uloop skills install [options]")
	fmt.Fprintln(stdout, "  uloop skills uninstall [options]")
}

func printSkillsTargetGuidance(command string, stdout io.Writer) {
	fmt.Fprintf(stdout, "\nPlease specify at least one target for '%s':\n\n", command)
	fmt.Fprintln(stdout, "Available targets:")
	fmt.Fprintln(stdout, "  --claude")
	fmt.Fprintln(stdout, "  --codex")
	fmt.Fprintln(stdout, "  --cursor")
	fmt.Fprintln(stdout, "  --gemini")
	fmt.Fprintln(stdout, "  --agents")
	fmt.Fprintln(stdout, "  --windsurf")
	fmt.Fprintln(stdout, "  --antigravity")
}
