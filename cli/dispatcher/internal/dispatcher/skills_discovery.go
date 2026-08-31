package dispatcher

import (
	"errors"
	"os"
	"path/filepath"
	"sort"
	"strings"

	"github.com/hatayama/unity-cli-loop/common/skillscan"
)

func collectSkillDefinitions(projectRoot string) ([]skillDefinition, error) {
	skills := []skillDefinition{}
	seen := map[string]bool{}
	for _, sourceRoot := range skillscan.EnumerateSourceRoots(projectRoot) {
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

func collectV3MigrationSkillDefinition(projectRoot string) ([]skillDefinition, error) {
	packageRoot := skillscan.ResolvePackageRoot(projectRoot)
	if packageRoot == "" {
		return nil, errors.New("the Unity CLI Loop package root was not found")
	}

	skillDirectory := filepath.Join(
		packageRoot,
		"TemporarySkills~",
		v3MigrationSkillName,
		"Skill")
	skill, ok, err := readSkillDefinition(skillDirectory)
	if err != nil {
		return nil, err
	}
	if !ok {
		return nil, errors.New("v3 CLI invocation migration skill source was not found")
	}
	if skill.name != v3MigrationSkillName {
		return nil, errors.New("v3 CLI invocation migration skill source has an unexpected name")
	}
	return []skillDefinition{skill}, nil
}

func scanSkillSourceRoot(sourceRoot skillscan.SkillSourceRoot) ([]skillDefinition, error) {
	if _, err := os.Stat(sourceRoot.Path); err != nil {
		return []skillDefinition{}, nil
	}

	scanRoots := []string{sourceRoot.Path}
	if !sourceRoot.CLIOnly {
		scanRoots = skillscan.FindEditorFolders(sourceRoot.Path, skillscan.SkillSearchMaxDepth)
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

func scanSkillDirectories(searchRoot string) ([]skillDefinition, error) {
	skills := []skillDefinition{}
	err := filepath.WalkDir(searchRoot, func(path string, entry os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if !entry.IsDir() {
			if entry.Name() != skillscan.SkillFileName {
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
		if skillscan.ExcludedSkillSearchDirs[entry.Name()] && entry.Name() != "Skill" {
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

func readSkillDefinition(skillDirectory string) (skillDefinition, bool, error) {
	skillPath := filepath.Join(skillDirectory, skillscan.SkillFileName)
	content, err := os.ReadFile(skillPath)
	if err != nil {
		if os.IsNotExist(err) {
			return skillDefinition{}, false, nil
		}
		return skillDefinition{}, false, err
	}
	content = normalizeSkillFileContent(skillscan.SkillFileName, content)
	frontmatter := skillscan.ParseSkillFrontmatter(string(content))
	if strings.EqualFold(frontmatter["internal"], "true") {
		return skillDefinition{}, false, nil
	}
	name := frontmatter["name"]
	if name == "" {
		name = skillscan.FallbackSkillName(skillDirectory)
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
