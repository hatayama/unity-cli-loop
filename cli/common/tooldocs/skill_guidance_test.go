package tooldocs

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// Verifies a command with a matching skill gets an instruction to load it, and that the instruction
// names the skill exactly.
func TestSkillGuidanceLineNamesTheMatchingSkill(t *testing.T) {
	line, ok := SkillGuidanceLine("simulate-keyboard")
	if !ok {
		t.Fatalf("simulate-keyboard has no skill guidance line")
	}
	if !strings.Contains(line, "uloop-simulate-keyboard") {
		t.Errorf("guidance line = %q, want it to name uloop-simulate-keyboard", line)
	}
}

// Verifies all four pause-point commands point at the single uloop-pause-point skill rather than a
// per-command skill name derived from the command, which would not exist.
func TestSkillGuidanceLineMapsEveryPausePointCommandToOneSkill(t *testing.T) {
	commands := []string{
		"enable-pause-point",
		"clear-pause-point",
		"await-pause-point",
		"pause-point-status",
	}

	for _, command := range commands {
		line, ok := SkillGuidanceLine(command)
		if !ok {
			t.Errorf("%s has no skill guidance line", command)
			continue
		}
		if !strings.Contains(line, "uloop-pause-point") {
			t.Errorf("%s guidance line = %q, want it to name uloop-pause-point", command, line)
		}
	}
}

// Verifies a command with no skill gets no guidance line, so custom commands are not pointed at a
// skill that does not exist.
func TestSkillGuidanceLineOmittedForUnmappedCommands(t *testing.T) {
	if line, ok := SkillGuidanceLine("my-custom-command"); ok {
		t.Errorf("unmapped command produced guidance line %q", line)
	}
}

// Verifies every skill named in the guidance map exists as a SKILL.md frontmatter name, so the
// instruction can never tell an agent to load a skill that was renamed or removed.
func TestSkillGuidanceMapNamesExistingSkills(t *testing.T) {
	existing := installedSkillNames(t)

	for command, skillName := range commandSkillNames {
		if !existing[skillName] {
			t.Errorf("command %s points at skill %q, which no SKILL.md declares", command, skillName)
		}
	}
}

func installedSkillNames(t *testing.T) map[string]bool {
	t.Helper()

	names := map[string]bool{}
	for _, pattern := range []string{
		filepath.Join("Packages", "src", "Editor", "FirstPartyTools", "*", "Skill", "SKILL.md"),
		filepath.Join("Packages", "src", "Editor", "CliOnlyTools~", "*", "Skill", "SKILL.md"),
	} {
		matches, err := filepath.Glob(filepath.Join(repositoryRoot(t), pattern))
		if err != nil {
			t.Fatalf("failed to glob %s: %v", pattern, err)
		}
		for _, match := range matches {
			names[skillFrontmatterName(t, match)] = true
		}
	}

	if len(names) == 0 {
		t.Fatalf("found no SKILL.md files to validate against")
	}
	return names
}

func skillFrontmatterName(t *testing.T, path string) string {
	t.Helper()

	content, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read %s: %v", path, err)
	}

	for _, line := range strings.Split(string(content), "\n") {
		if strings.HasPrefix(line, "name:") {
			return strings.TrimSpace(strings.TrimPrefix(line, "name:"))
		}
	}

	t.Fatalf("%s has no frontmatter name", path)
	return ""
}

// repositoryRoot walks up from the test's working directory until the repository layout is visible,
// because this module's tests run from their own package directory.
func repositoryRoot(t *testing.T) string {
	t.Helper()

	directory, err := os.Getwd()
	if err != nil {
		t.Fatalf("failed to resolve current directory: %v", err)
	}

	for {
		if _, err := os.Stat(filepath.Join(directory, "Packages", "src", "Editor")); err == nil {
			return directory
		}

		parent := filepath.Dir(directory)
		if parent == directory {
			t.Fatalf("failed to find the repository root from %s", directory)
		}
		directory = parent
	}
}
