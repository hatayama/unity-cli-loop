package cli

import (
	"os"
	"path/filepath"
	"sort"
	"strings"
)

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

	addSourceRoot(filepath.Join(projectRoot, "Packages/src/GoCli~/internal/presentation/cli/skill-definitions/cli-only"), true)
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
	content = normalizeSkillFileContent(skillFileName, content)
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
