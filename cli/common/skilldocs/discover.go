package skilldocs

import (
	"os"
	"path/filepath"
	"sort"

	"github.com/hatayama/unity-cli-loop/common/skillscan"
	"github.com/hatayama/unity-cli-loop/common/vibelog"
)

const (
	editorDirectoryName    = "Editor"
	firstPartyToolsDirName = "FirstPartyTools"
	cliOnlyToolsDirName    = "CliOnlyTools~"
	skillDirectoryName     = "Skill"

	skillDocsLogOperation = "skill_docs_render"
)

// Load reads every skill in the installed uloop package and returns what each one documents, keyed
// by tool name. A project without the package, an unreadable file, or a skill that documents nothing
// yields no entry for the affected tools; callers then fall back to the descriptions they already
// had. Help that prints stale text is a nuisance, help that fails to print is a broken CLI.
func Load(projectRoot string) map[string]ToolDocs {
	if projectRoot == "" {
		return nil
	}

	packageRoot := skillscan.ResolvePackageRoot(projectRoot)
	if packageRoot == "" {
		logSkillDocsFallback(projectRoot, "uloop package root not found; keeping embedded descriptions", nil)
		return nil
	}

	result := map[string]ToolDocs{}
	for _, skillPath := range skillFilePaths(packageRoot) {
		content, err := os.ReadFile(skillPath)
		if err != nil {
			logSkillDocsFallback(projectRoot, "skill file could not be read", map[string]any{
				"skill_path": skillPath,
				"error":      err.Error(),
			})
			continue
		}

		parsed := ParseSkill(string(content))
		if len(parsed) == 0 {
			logSkillDocsFallback(projectRoot, "skill file documented no tool", map[string]any{
				"skill_path": skillPath,
			})
			continue
		}
		for toolName, docs := range parsed {
			result[toolName] = docs
		}
	}
	return result
}

// skillFilePaths lists the skill sources shipped inside the package. Both containers are read
// wholesale rather than by a hard-coded tool list, so a new tool's skill is picked up by adding the
// folder alone. CliOnlyTools~ holds the skills for commands with no Unity tool class; the ones that
// name no live tool simply never match a catalog entry.
func skillFilePaths(packageRoot string) []string {
	paths := []string{}
	for _, containerName := range []string{firstPartyToolsDirName, cliOnlyToolsDirName} {
		containerPath := filepath.Join(packageRoot, editorDirectoryName, containerName)
		entries, err := os.ReadDir(containerPath)
		if err != nil {
			continue
		}
		for _, entry := range entries {
			if !entry.IsDir() {
				continue
			}
			skillPath := filepath.Join(containerPath, entry.Name(), skillDirectoryName, skillscan.SkillFileName)
			if _, err := os.Stat(skillPath); err != nil {
				continue
			}
			paths = append(paths, skillPath)
		}
	}
	sort.Strings(paths)
	return paths
}

// logSkillDocsFallback records why a layer was skipped. The fallback is deliberately silent on
// stdout - a diagnostic line would corrupt `uloop list` output - so this log is the only trace.
func logSkillDocsFallback(projectRoot string, message string, context map[string]any) {
	if !vibelog.IsCLIVibeLogEnabled() {
		return
	}
	_ = vibelog.WriteCLIVibeLog(projectRoot, vibelog.CLIVibeLogEntry{
		Level:     "WARNING",
		Operation: skillDocsLogOperation,
		Message:   message,
		Context:   context,
		HumanNote: "Help and list fell back to the descriptions compiled into this binary.",
	})
}
