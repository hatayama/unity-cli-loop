package skillscan

import (
	"os"
	"path/filepath"
	"reflect"
	"testing"
)

func writeSkillFile(t *testing.T, skillDirectory string, content string) {
	t.Helper()
	if err := os.MkdirAll(skillDirectory, 0o755); err != nil {
		t.Fatalf("failed to create skill directory: %v", err)
	}
	if err := os.WriteFile(filepath.Join(skillDirectory, SkillFileName), []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write skill file: %v", err)
	}
}

func internalSkill(fields string) string {
	return "---\ninternal: true\n" + fields + "---\n\n# Body\n"
}

// Verifies that internal tool names are collected from the CLI-only root and from Editor folders,
// using toolName, a uloop- prefixed name, or the folder name, and that public skills are ignored.
func TestCollectInternalSkillToolNamesReadsInternalSkillsFromEverySource(t *testing.T) {
	projectRoot := t.TempDir()
	cliOnlyRoot := CliOnlySourceRoot(projectRoot)
	writeSkillFile(t, filepath.Join(cliOnlyRoot, "Explicit", "Skill"), internalSkill("toolName: explicit-tool\n"))
	writeSkillFile(t, filepath.Join(cliOnlyRoot, "Prefixed", "Skill"), internalSkill("name: uloop-prefixed-tool\n"))
	writeSkillFile(t, filepath.Join(cliOnlyRoot, "uloop-folder-tool", "Skill"), internalSkill(""))
	writeSkillFile(t, filepath.Join(cliOnlyRoot, "Unprefixed", "Skill"), internalSkill("name: other-tool\n"))
	writeSkillFile(t, filepath.Join(cliOnlyRoot, "Public", "Skill"), "---\nname: uloop-public-tool\n---\n")
	writeSkillFile(t, filepath.Join(cliOnlyRoot, "uloop-bare-tool"), internalSkill(""))

	assetsRoot := filepath.Join(projectRoot, "Assets")
	writeSkillFile(t, filepath.Join(assetsRoot, "Feature", "Editor", "Tool", "Skill"), internalSkill("toolName: assets-editor-tool\n"))
	writeSkillFile(t, filepath.Join(assetsRoot, "Feature", "Runtime", "Tool", "Skill"), internalSkill("toolName: assets-runtime-tool\n"))
	writeSkillFile(t, filepath.Join(assetsRoot, "Feature", "Editor", "node_modules", "Skill"), internalSkill("toolName: excluded-tool\n"))
	writeSkillFile(t, filepath.Join(assetsRoot, "Feature", "Editor", "Tool", "Skill", "Nested", "Skill"), internalSkill("toolName: nested-tool\n"))

	toolNames := CollectInternalSkillToolNames(projectRoot)

	expected := map[string]bool{
		"explicit-tool":      true,
		"prefixed-tool":      true,
		"folder-tool":        true,
		"bare-tool":          true,
		"assets-editor-tool": true,
	}
	if !reflect.DeepEqual(toolNames, expected) {
		t.Fatalf("internal tool names mismatch:\nactual:   %#v\nexpected: %#v", toolNames, expected)
	}
}

// Verifies that a project with no skill sources yields an empty, non-nil tool name set.
func TestCollectInternalSkillToolNamesReturnsEmptySetForEmptyProject(t *testing.T) {
	toolNames := CollectInternalSkillToolNames(t.TempDir())

	if toolNames == nil || len(toolNames) != 0 {
		t.Fatalf("expected an empty tool name set, got %#v", toolNames)
	}
}

// Verifies that a skill directory whose SKILL.md cannot be read is skipped instead of reported.
func TestReadInternalSkillToolNameRejectsMissingSkillFile(t *testing.T) {
	toolName, ok := readInternalSkillToolName(filepath.Join(t.TempDir(), "Missing", "Skill"))

	if ok || toolName != "" {
		t.Fatalf("expected no tool name, got %q (ok=%v)", toolName, ok)
	}
}

// Verifies that a manifest dependency pointing at the Assets folder does not list Assets twice.
func TestEnumerateSourceRootsDeduplicatesRootsReachedTwice(t *testing.T) {
	projectRoot := t.TempDir()
	if err := os.MkdirAll(filepath.Join(projectRoot, "Assets"), 0o755); err != nil {
		t.Fatalf("failed to create Assets: %v", err)
	}
	writeManifest(t, projectRoot, `{"dependencies":{"com.example.assets":"file:../Assets"}}`)

	actualPaths := sourceRootPaths(EnumerateSourceRoots(projectRoot))

	expectedPaths := cleanPaths([]string{
		CliOnlySourceRoot(projectRoot),
		filepath.Join(projectRoot, "Assets"),
	})
	if !reflect.DeepEqual(actualPaths, expectedPaths) {
		t.Fatalf("source roots mismatch:\nactual:   %#v\nexpected: %#v", actualPaths, expectedPaths)
	}
}

// Verifies that the fallback skill name skips a trailing Skill folder and otherwise uses the folder itself.
func TestFallbackSkillName(t *testing.T) {
	cases := map[string]string{
		filepath.Join("Tools", "uloop-sample", "Skill"): "uloop-sample",
		filepath.Join("Tools", "uloop-direct"):          "uloop-direct",
	}
	for skillDirectory, expected := range cases {
		if actual := FallbackSkillName(skillDirectory); actual != expected {
			t.Errorf("FallbackSkillName(%q) = %q, want %q", skillDirectory, actual, expected)
		}
	}
}

// Verifies that frontmatter parsing requires both delimiters, trims quotes, and ignores lines without a colon.
func TestParseSkillFrontmatter(t *testing.T) {
	cases := []struct {
		name     string
		content  string
		expected map[string]string
	}{
		{name: "no leading delimiter", content: "name: uloop-x\n", expected: map[string]string{}},
		{name: "unterminated", content: "---\nname: uloop-x\n", expected: map[string]string{}},
		{
			name:    "quoted values and plain lines",
			content: "---\nname: \"uloop-x\"\ntoolName: 'x-tool'\nnot a pair\ndescription: a: b\n---\nbody: ignored\n",
			expected: map[string]string{
				"name":        "uloop-x",
				"toolName":    "x-tool",
				"description": "a: b",
			},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			actual := ParseSkillFrontmatter(testCase.content)
			if !reflect.DeepEqual(actual, testCase.expected) {
				t.Fatalf("frontmatter mismatch:\nactual:   %#v\nexpected: %#v", actual, testCase.expected)
			}
		})
	}
}
