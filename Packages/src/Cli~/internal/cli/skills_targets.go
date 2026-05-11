package cli

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
)

var userHomeDir = os.UserHomeDir

func getSkillsBaseDir(projectRoot string, target skillTarget, global bool) (string, error) {
	if global {
		homeDir, err := userHomeDir()
		if err != nil {
			return "", err
		}
		return filepath.Join(homeDir, target.projectDir, "skills"), nil
	}
	return filepath.Join(projectRoot, target.projectDir, "skills"), nil
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
			exists, err := removeDeprecatedSkillDir(baseDir, skillName, grouped)
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

func removeDeprecatedSkillDirsForLayout(baseDir string, grouped bool) (int, error) {
	removed := 0
	for _, skillName := range deprecatedSkillNames {
		exists, err := removeDeprecatedSkillDir(baseDir, skillName, grouped)
		if err != nil {
			return removed, err
		}
		if exists {
			removed++
		}
	}
	return removed, nil
}

func removeDeprecatedSkillDir(baseDir string, skillName string, grouped bool) (bool, error) {
	return removeDirIfExists(getPreferredSkillDir(baseDir, skillName, grouped))
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
