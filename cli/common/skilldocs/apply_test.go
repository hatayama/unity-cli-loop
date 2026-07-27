package skilldocs

import (
	"os"
	"path/filepath"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/tools"
)

// writeFixtureProject builds a Unity project whose Packages/src holds the uloop package, which is
// the layout ResolvePackageRoot recognizes for a locally embedded package.
func writeFixtureProject(t *testing.T, skills map[string]string) string {
	t.Helper()

	projectRoot := t.TempDir()
	packageRoot := filepath.Join(projectRoot, "Packages", "src")
	if err := os.MkdirAll(packageRoot, 0o755); err != nil {
		t.Fatalf("failed to create the package root: %v", err)
	}
	manifest := []byte(`{"name":"io.github.hatayama.uloopmcp"}`)
	if err := os.WriteFile(filepath.Join(packageRoot, "package.json"), manifest, 0o644); err != nil {
		t.Fatalf("failed to write the package manifest: %v", err)
	}
	// Editor/FirstPartyTools is how ResolvePackageRoot recognizes a candidate as the uloop package,
	// so it exists in every install regardless of which skills this fixture writes.
	if err := os.MkdirAll(filepath.Join(packageRoot, "Editor", "FirstPartyTools"), 0o755); err != nil {
		t.Fatalf("failed to create the first-party tools directory: %v", err)
	}

	for relativeDirectory, content := range skills {
		skillDirectory := filepath.Join(packageRoot, "Editor", relativeDirectory, "Skill")
		if err := os.MkdirAll(skillDirectory, 0o755); err != nil {
			t.Fatalf("failed to create %s: %v", skillDirectory, err)
		}
		if err := os.WriteFile(filepath.Join(skillDirectory, "SKILL.md"), []byte(content), 0o644); err != nil {
			t.Fatalf("failed to write %s: %v", skillDirectory, err)
		}
	}
	return projectRoot
}

func fixtureCatalog() tools.ToolCatalog {
	return tools.ToolCatalog{Tools: []tools.ToolDefinition{
		{
			Name:        "simulate-keyboard",
			Description: "Stale catalog description.",
			InputSchema: tools.ToolInputSchema{
				Type: "object",
				Properties: map[string]tools.ToolProperty{
					"Action":   {Type: "string", Description: "Parameter: Action"},
					"Duration": {Type: "number", Description: "Stale duration description."},
				},
			},
		},
		{
			Name:        "my-custom-command",
			Description: "A project-local command with no skill.",
			InputSchema: tools.ToolInputSchema{
				Type:       "object",
				Properties: map[string]tools.ToolProperty{"Amount": {Type: "number", Description: "Author's own text."}},
			},
		},
	}}
}

// Verifies the skill table replaces both the placeholder and the non-placeholder descriptions a
// catalog carries, which is the drift this renderer exists to remove.
func TestApplyToCatalogPrefersTheSkillTable(t *testing.T) {
	projectRoot := writeFixtureProject(t, map[string]string{"FirstPartyTools/SimulateKeyboard": singleToolSkill})

	catalog := ApplyToCatalog(fixtureCatalog(), projectRoot)

	tool, ok := tools.Find(catalog, "simulate-keyboard")
	if !ok {
		t.Fatal("simulate-keyboard is missing from the catalog")
	}
	if tool.Description != "Simulate keyboard input in PlayMode." {
		t.Errorf("tool description was not taken from the skill: %q", tool.Description)
	}
	properties := tool.EffectiveInputSchema().Properties
	if got := properties["Action"].Description; got != "Press | KeyDown | KeyUp" {
		t.Errorf("Action description was not taken from the skill: %q", got)
	}
	if got := properties["Duration"].Description; got != "Hold duration in seconds." {
		t.Errorf("a real-looking description must still lose to the skill: %q", got)
	}
}

// Verifies a command the package documents nowhere keeps the description its own author wrote, so
// custom commands are unaffected by this layer.
func TestApplyToCatalogLeavesUndocumentedCommandsAlone(t *testing.T) {
	projectRoot := writeFixtureProject(t, map[string]string{"FirstPartyTools/SimulateKeyboard": singleToolSkill})

	catalog := ApplyToCatalog(fixtureCatalog(), projectRoot)

	tool, ok := tools.Find(catalog, "my-custom-command")
	if !ok {
		t.Fatal("my-custom-command is missing from the catalog")
	}
	if tool.Description != "A project-local command with no skill." {
		t.Errorf("tool description changed: %q", tool.Description)
	}
	if got := tool.EffectiveInputSchema().Properties["Amount"].Description; got != "Author's own text." {
		t.Errorf("property description changed: %q", got)
	}
}

// Verifies a project with no uloop package installed keeps the catalog exactly as it was: a missing
// skill must degrade the text, never the command.
func TestApplyToCatalogFallsBackWhenNoPackageIsInstalled(t *testing.T) {
	catalog := ApplyToCatalog(fixtureCatalog(), t.TempDir())

	tool, _ := tools.Find(catalog, "simulate-keyboard")
	if tool.Description != "Stale catalog description." {
		t.Errorf("tool description changed without a package: %q", tool.Description)
	}
	if got := tool.EffectiveInputSchema().Properties["Duration"].Description; got != "Stale duration description." {
		t.Errorf("property description changed without a package: %q", got)
	}
}

// Verifies an empty project root is a no-op, the shape taken by help resolved outside any project.
func TestApplyToToolWithoutAProjectRootIsANoOp(t *testing.T) {
	tool := ApplyToTool(fixtureCatalog().Tools[0], "")

	if tool.Description != "Stale catalog description." {
		t.Errorf("tool description changed with no project root: %q", tool.Description)
	}
}

// Verifies the skills of the CLI-only commands are read too, so the pause-point family - documented
// by a single multi-command skill in CliOnlyTools~ - is covered.
func TestApplyToToolReadsCliOnlySkills(t *testing.T) {
	projectRoot := writeFixtureProject(t, map[string]string{"CliOnlyTools~/PausePoint": multiToolSkill})

	tool := ApplyToTool(tools.ToolDefinition{
		Name:        "enable-pause-point",
		Description: "Stale.",
		InputSchema: tools.ToolInputSchema{
			Type:       "object",
			Properties: map[string]tools.ToolProperty{"MaxHistory": {Type: "integer", Description: "Parameter: MaxHistory"}},
		},
	}, projectRoot)

	if tool.Description != "Enable a pause point so Unity pauses when that code path is reached" {
		t.Errorf("tool description was not taken from the skill subsection: %q", tool.Description)
	}
	expected := "Maximum number of captured hit frames to retain (1-100)"
	if got := tool.EffectiveInputSchema().Properties["MaxHistory"].Description; got != expected {
		t.Errorf("MaxHistory description: %q", got)
	}
}
