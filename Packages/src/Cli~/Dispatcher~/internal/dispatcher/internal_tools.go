package dispatcher

import (
	"os"
	"path/filepath"
	"strings"
)

const (
	skillFileName       = "SKILL.md"
	skillDirectoryName  = "Skill"
	uloopSkillPrefix    = "uloop-"
	internalSkillMarker = "internal"
	skillNameMarker     = "name"
	skillToolNameMarker = "toolName"
)

var excludedSkillSearchDirs = map[string]bool{
	".git":         true,
	"Build":        true,
	"Builds":       true,
	"dist":         true,
	"Library":      true,
	"Logs":         true,
	"node_modules": true,
	"obj":          true,
	"Temp":         true,
}

func filterInternalSkillTools(projectRoot string, cache cachedTools) cachedTools {
	internalToolNames := collectInternalSkillToolNames(projectRoot)
	if len(internalToolNames) == 0 {
		return cache
	}

	filteredTools := make([]cachedTool, 0, len(cache.Tools))
	for _, tool := range cache.Tools {
		if internalToolNames[tool.Name] {
			continue
		}
		filteredTools = append(filteredTools, tool)
	}
	cache.Tools = filteredTools
	return cache
}

func collectInternalSkillToolNames(projectRoot string) map[string]bool {
	toolNames := map[string]bool{}
	for _, sourceRoot := range internalSkillSourceRoots(projectRoot) {
		for _, toolName := range scanInternalSkillToolNames(sourceRoot) {
			toolNames[toolName] = true
		}
	}
	return toolNames
}

func internalSkillSourceRoots(projectRoot string) []string {
	sourceRoots := []string{
		filepath.Join(projectRoot, "Packages/src/Cli~/Core~/internal/presentation/cli/skill-definitions/cli-only"),
		filepath.Join(projectRoot, "Assets"),
	}
	sourceRoots = append(sourceRoots, childDirectories(filepath.Join(projectRoot, "Packages"))...)
	sourceRoots = append(sourceRoots, resolveManifestLocalPackageRoots(projectRoot)...)
	sourceRoots = append(sourceRoots, resolveDependencyPackageCacheRoots(projectRoot)...)
	sourceRoots = append(sourceRoots, childDirectories(filepath.Join(projectRoot, "Library", "PackageCache"))...)
	return sourceRoots
}

func childDirectories(root string) []string {
	entries, err := os.ReadDir(root)
	if err != nil {
		return []string{}
	}

	directories := []string{}
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		directories = append(directories, filepath.Join(root, entry.Name()))
	}
	return directories
}

func scanInternalSkillToolNames(searchRoot string) []string {
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
		if excludedSkillSearchDirs[entry.Name()] {
			return filepath.SkipDir
		}
		if entry.Name() != skillDirectoryName {
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
	content, err := os.ReadFile(filepath.Join(skillDirectory, skillFileName))
	if err != nil {
		return "", false
	}
	frontmatter := parseSkillFrontmatter(string(content))
	if !strings.EqualFold(frontmatter[internalSkillMarker], "true") {
		return "", false
	}
	if frontmatter[skillToolNameMarker] != "" {
		return frontmatter[skillToolNameMarker], true
	}
	name := frontmatter[skillNameMarker]
	if name == "" {
		name = deriveSkillNameFromDirectory(skillDirectory)
	}
	return toolNameFromSkillName(name)
}

func deriveSkillNameFromDirectory(skillDirectory string) string {
	if filepath.Base(skillDirectory) == skillDirectoryName {
		return filepath.Base(filepath.Dir(skillDirectory))
	}
	return filepath.Base(skillDirectory)
}

func toolNameFromSkillName(name string) (string, bool) {
	if strings.HasPrefix(name, uloopSkillPrefix) {
		return strings.TrimPrefix(name, uloopSkillPrefix), true
	}
	return "", false
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
