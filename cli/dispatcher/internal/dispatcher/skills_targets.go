package dispatcher

import (
	"os"
	"path/filepath"
	"strings"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

var userHomeDir = os.UserHomeDir

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
	_, err := removeSkillFromAllLayoutsIfExists(baseDir, skillName)
	return err
}

func removeSkillFromAllLayoutsIfExists(baseDir string, skillName string) (bool, error) {
	removed := false
	for _, grouped := range []bool{true, false} {
		exists, err := removeDirIfExists(getPreferredSkillDir(baseDir, skillName, grouped))
		if err != nil {
			return removed, err
		}
		if exists {
			removed = true
		}
	}
	return removed, nil
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
	return clicore.IsToolDisabledByToolSettings(toolName, disabledTools)
}
