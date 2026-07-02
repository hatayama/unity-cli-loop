package cli

import (
	"os"
	"path/filepath"
	"strings"

	"github.com/hatayama/unity-cli-loop/cli/internal/skills"
)

const (
	skillFileName       = "SKILL.md"
	skillSearchMaxDepth = 3
)

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

// skillSourceRoot is a directory tree that may contain skill definitions, plus whether
// it should be scanned directly (CLI-only sources) or only under its Editor folders.
type skillSourceRoot struct {
	path    string
	cliOnly bool
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

	addSourceRoot(skills.CliOnlySourceRoot(projectRoot), true)
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
	addSourceRoot(resolvePackageRoot(projectRoot), false)
	return sourceRoots
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
	if name == "" {
		name = fallbackSkillName(skillDirectory)
	}
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
		result[strings.TrimSpace(key)] = strings.Trim(strings.TrimSpace(value), `"'`)
	}
	return result
}
