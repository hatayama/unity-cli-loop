package automation

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// fixtureCatalogContent is a miniature default-tools.json with the traits the real file has that a
// struct round trip would destroy: a zero-value default, a hidden property, an enum, and properties
// in declaration rather than alphabetical order.
const fixtureCatalogContent = `{
  "tools": [
    {
      "name": "simulate-keyboard",
      "description": "Stale tool description",
      "inputSchema": {
        "type": "object",
        "properties": {
          "Key": {
            "type": "string",
            "description": "Stale key description"
          },
          "Action": {
            "type": "string",
            "description": "Stale action description",
            "enum": [
              "Press",
              "ReleaseAll"
            ],
            "default": "Press"
          },
          "Duration": {
            "type": "number",
            "description": "Stale duration description",
            "default": 0
          },
          "InternalOnly": {
            "type": "boolean",
            "description": "Not documented anywhere",
            "hidden": true
          }
        }
      }
    },
    {
      "name": "focus-window",
      "description": "Stale focus description",
      "inputSchema": {
        "type": "object",
        "properties": {}
      }
    }
  ]
}
`

const fixtureKeyboardSkill = `---
name: uloop-simulate-keyboard
toolName: simulate-keyboard
description: "Simulate keyboard input in PlayMode."
---

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| ` + "`--action`" + ` | enum | ` + "`Press`" + ` | Press \| ReleaseAll |
| ` + "`--key`" + ` | string | - | Key name matching the Input System Key enum |
| ` + "`--duration`" + ` | number | ` + "`0`" + ` | Hold duration in seconds |
`

const fixtureFocusWindowSkill = `---
name: uloop-focus-window
description: "Bring the Unity Editor window to front."
---

# uloop focus-window
`

// writeGeneratorFixture builds a repository holding the uloop package and the catalog file, and
// returns its root.
func writeGeneratorFixture(t *testing.T, skills map[string]string, catalogContent string) string {
	t.Helper()

	repositoryRoot := t.TempDir()
	packageRoot := filepath.Join(repositoryRoot, "Packages", "src")
	if err := os.MkdirAll(filepath.Join(packageRoot, "Editor", "FirstPartyTools"), 0o755); err != nil {
		t.Fatalf("failed to create the package root: %v", err)
	}
	writeFixtureFile(t, filepath.Join(packageRoot, "package.json"), `{"name":"io.github.hatayama.uloopmcp"}`)
	writeFixtureFile(t, filepath.Join(repositoryRoot, filepath.FromSlash(CatalogRelativePath)), catalogContent)

	for relativeDirectory, content := range skills {
		skillPath := filepath.Join(packageRoot, "Editor", relativeDirectory, "Skill", "SKILL.md")
		writeFixtureFile(t, skillPath, content)
	}
	return repositoryRoot
}

func writeFixtureFile(t *testing.T, path string, content string) {
	t.Helper()

	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatalf("failed to create %s: %v", filepath.Dir(path), err)
	}
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write %s: %v", path, err)
	}
}

func defaultGeneratorSkills() map[string]string {
	return map[string]string{
		"FirstPartyTools/SimulateKeyboard": fixtureKeyboardSkill,
		"CliOnlyTools~/FocusWindow":        fixtureFocusWindowSkill,
	}
}

// Verifies generation replaces every description with its skill text and changes nothing else: a
// zero-value default, the property order, the enum and the hidden property all survive byte for byte,
// which a decode-edit-encode round trip would not manage.
func TestGenerateCatalogReplacesOnlyDescriptions(t *testing.T) {
	repositoryRoot := writeGeneratorFixture(t, defaultGeneratorSkills(), fixtureCatalogContent)

	generated, err := GenerateCatalogWithSkillDescriptions([]byte(fixtureCatalogContent), repositoryRoot)
	if err != nil {
		t.Fatalf("generation failed: %v", err)
	}

	expected := strings.NewReplacer(
		`"Stale tool description"`, `"Simulate keyboard input in PlayMode."`,
		`"Stale key description"`, `"Key name matching the Input System Key enum"`,
		`"Stale action description"`, `"Press | ReleaseAll"`,
		`"Stale duration description"`, `"Hold duration in seconds"`,
		`"Stale focus description"`, `"Bring the Unity Editor window to front."`,
	).Replace(fixtureCatalogContent)
	if string(generated) != expected {
		t.Errorf("generated content differs from the expected byte-for-byte result:\n%s", string(generated))
	}
}

// Verifies a second run over its own output changes nothing, so the committed file is a fixed point
// and CI's --check cannot fail on a freshly generated catalog.
func TestGenerateCatalogIsIdempotent(t *testing.T) {
	repositoryRoot := writeGeneratorFixture(t, defaultGeneratorSkills(), fixtureCatalogContent)

	first, err := GenerateCatalogWithSkillDescriptions([]byte(fixtureCatalogContent), repositoryRoot)
	if err != nil {
		t.Fatalf("first generation failed: %v", err)
	}
	second, err := GenerateCatalogWithSkillDescriptions(first, repositoryRoot)
	if err != nil {
		t.Fatalf("second generation failed: %v", err)
	}

	if string(first) != string(second) {
		t.Errorf("generation is not idempotent:\n%s", string(second))
	}
}

// Verifies a property the tool accepts but the skill table omits stops generation, since one of the
// two is stale and writing the catalog anyway would hide which.
func TestGenerateCatalogFailsOnAnUndocumentedProperty(t *testing.T) {
	skills := defaultGeneratorSkills()
	skills["FirstPartyTools/SimulateKeyboard"] = strings.ReplaceAll(
		fixtureKeyboardSkill, "| `--duration` | number | `0` | Hold duration in seconds |\n", "")
	repositoryRoot := writeGeneratorFixture(t, skills, fixtureCatalogContent)

	_, err := GenerateCatalogWithSkillDescriptions([]byte(fixtureCatalogContent), repositoryRoot)

	if err == nil {
		t.Fatal("an undocumented property must stop generation")
	}
	if !strings.Contains(err.Error(), "simulate-keyboard --duration") {
		t.Errorf("the error must name the undocumented option: %v", err)
	}
}

// Verifies a table row matching no accepted option stops generation, which is the drift left behind
// when an option is renamed or removed.
func TestGenerateCatalogFailsOnAnUnknownTableRow(t *testing.T) {
	skills := defaultGeneratorSkills()
	skills["FirstPartyTools/SimulateKeyboard"] = fixtureKeyboardSkill +
		"| `--removed-flag` | flag | - | No longer accepted |\n"
	repositoryRoot := writeGeneratorFixture(t, skills, fixtureCatalogContent)

	_, err := GenerateCatalogWithSkillDescriptions([]byte(fixtureCatalogContent), repositoryRoot)

	if err == nil {
		t.Fatal("a table row for an option the tool does not accept must stop generation")
	}
	if !strings.Contains(err.Error(), "--removed-flag") {
		t.Errorf("the error must name the unknown row: %v", err)
	}
}

// Verifies a hidden property needs no table row and keeps its description, because it never reaches
// help and documenting it would describe something no caller can pass.
func TestGenerateCatalogIgnoresHiddenProperties(t *testing.T) {
	repositoryRoot := writeGeneratorFixture(t, defaultGeneratorSkills(), fixtureCatalogContent)

	generated, err := GenerateCatalogWithSkillDescriptions([]byte(fixtureCatalogContent), repositoryRoot)
	if err != nil {
		t.Fatalf("generation failed: %v", err)
	}

	if !strings.Contains(string(generated), `"description": "Not documented anywhere"`) {
		t.Errorf("a hidden property's description must be left alone:\n%s", string(generated))
	}
}

// Verifies every mismatch is reported in one run rather than one per invocation, since a table that
// fell behind usually did so for several options at once.
func TestGenerateCatalogReportsEveryMismatchAtOnce(t *testing.T) {
	skills := defaultGeneratorSkills()
	skills["FirstPartyTools/SimulateKeyboard"] = strings.NewReplacer(
		"| `--duration` | number | `0` | Hold duration in seconds |\n", "",
		"| `--key` | string | - | Key name matching the Input System Key enum |\n", "",
	).Replace(fixtureKeyboardSkill)
	repositoryRoot := writeGeneratorFixture(t, skills, fixtureCatalogContent)

	_, err := GenerateCatalogWithSkillDescriptions([]byte(fixtureCatalogContent), repositoryRoot)

	if err == nil {
		t.Fatal("two undocumented properties must stop generation")
	}
	for _, expected := range []string{"--duration", "--key"} {
		if !strings.Contains(err.Error(), expected) {
			t.Errorf("the error must name %s: %v", expected, err)
		}
	}
}

// Verifies a tool with no skill at all stops generation, so no tool's help text can quietly stay
// hand-maintained in the catalog.
func TestGenerateCatalogFailsWhenAToolHasNoSkill(t *testing.T) {
	skills := map[string]string{"CliOnlyTools~/FocusWindow": fixtureFocusWindowSkill}
	repositoryRoot := writeGeneratorFixture(t, skills, fixtureCatalogContent)

	_, err := GenerateCatalogWithSkillDescriptions([]byte(fixtureCatalogContent), repositoryRoot)

	if err == nil {
		t.Fatal("a tool with no skill must stop generation")
	}
	if !strings.Contains(err.Error(), "simulate-keyboard has no skill") {
		t.Errorf("the error must name the undocumented tool: %v", err)
	}
}

// Verifies check mode reports the committed catalog as stale without writing it, which is what CI
// needs: a red step and an untouched working tree.
func TestRunSyncToolDocsCheckModeReportsAStaleCatalog(t *testing.T) {
	repositoryRoot := writeGeneratorFixture(t, defaultGeneratorSkills(), fixtureCatalogContent)
	stdout := strings.Builder{}
	stderr := strings.Builder{}

	code := RunSyncToolDocs(&stdout, &stderr, SyncToolDocsConfig{RepositoryRoot: repositoryRoot, CheckOnly: true})

	if code == 0 {
		t.Fatalf("a stale catalog must fail check mode: %s", stdout.String())
	}
	if !strings.Contains(stderr.String(), "scripts/sync-tool-docs.sh") {
		t.Errorf("check mode must name the command that fixes it: %s", stderr.String())
	}
	content, err := os.ReadFile(filepath.Join(repositoryRoot, filepath.FromSlash(CatalogRelativePath)))
	if err != nil {
		t.Fatalf("failed to read the catalog back: %v", err)
	}
	if string(content) != fixtureCatalogContent {
		t.Error("check mode must not write the catalog")
	}
}

// Verifies writing then checking leaves check mode green, the sequence a developer runs before
// committing.
func TestRunSyncToolDocsWritesThenPassesCheck(t *testing.T) {
	repositoryRoot := writeGeneratorFixture(t, defaultGeneratorSkills(), fixtureCatalogContent)
	stdout := strings.Builder{}
	stderr := strings.Builder{}

	if code := RunSyncToolDocs(&stdout, &stderr, SyncToolDocsConfig{RepositoryRoot: repositoryRoot}); code != 0 {
		t.Fatalf("write mode failed: %s", stderr.String())
	}
	if code := RunSyncToolDocs(&stdout, &stderr, SyncToolDocsConfig{RepositoryRoot: repositoryRoot, CheckOnly: true}); code != 0 {
		t.Fatalf("check mode failed right after writing: %s", stderr.String())
	}
}
