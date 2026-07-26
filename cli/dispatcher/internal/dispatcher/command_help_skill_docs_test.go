package dispatcher

import (
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

const skillDocsFixtureDurationDescription = "Hold duration written only in the fixture skill."

// Verifies option and tool help text come from the installed package's SKILL.md table rather than
// from the descriptions compiled into this binary, which is the drift this reader removes.
func TestCommandHelpReadsDescriptionsFromTheInstalledSkill(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	writeSkillDocsFixturePackage(t, projectRoot, skillDocsFixtureDurationDescription)
	writeToolCache(t, projectRoot, `{
  "tools": [
    {
      "name": "simulate-keyboard",
      "inputSchema": {
        "type": "object",
        "properties": {
          "Duration": {"type": "number", "description": "Parameter: Duration"}
        }
      }
    }
  ]
}`)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	handled, code := tryHandleCommandHelp("simulate-keyboard", projectRoot, projectRoot, &stdout, &stderr)

	if !handled || code != 0 {
		t.Fatalf("simulate-keyboard help was not handled: handled=%v code=%d stderr=%s", handled, code, stderr.String())
	}
	output := stdout.String()
	if !strings.Contains(output, skillDocsFixtureDurationDescription) {
		t.Errorf("the option description was not read from the skill:\n%s", output)
	}
	if !strings.Contains(output, "Simulate keyboard input from the fixture skill.") {
		t.Errorf("the tool description was not read from the skill:\n%s", output)
	}
}

// Verifies a project with no installed package still prints full help from the embedded catalog: a
// missing or unreadable skill may only cost freshness, never the help itself.
func TestCommandHelpKeepsEmbeddedDescriptionsWithoutAnInstalledSkill(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	writeToolCache(t, projectRoot, `{
  "tools": [
    {
      "name": "simulate-keyboard",
      "inputSchema": {
        "type": "object",
        "properties": {
          "Duration": {"type": "number", "description": "Parameter: Duration"}
        }
      }
    }
  ]
}`)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	handled, code := tryHandleCommandHelp("simulate-keyboard", projectRoot, projectRoot, &stdout, &stderr)

	if !handled || code != 0 {
		t.Fatalf("simulate-keyboard help was not handled: handled=%v code=%d stderr=%s", handled, code, stderr.String())
	}
	output := stdout.String()
	if !strings.Contains(output, "--duration") {
		t.Fatalf("the option was not listed at all:\n%s", output)
	}
	if strings.Contains(output, "Parameter: Duration") {
		t.Errorf("the placeholder description survived instead of the embedded text:\n%s", output)
	}
}

// writeSkillDocsFixturePackage installs a uloop package inside the project whose simulate-keyboard
// skill documents --duration with the given text.
func writeSkillDocsFixturePackage(t *testing.T, projectRoot string, durationDescription string) {
	t.Helper()

	packageRoot := filepath.Join(projectRoot, "Packages", "src")
	skillDirectory := filepath.Join(packageRoot, "Editor", "FirstPartyTools", "SimulateKeyboard", "Skill")
	if err := os.MkdirAll(skillDirectory, 0o755); err != nil {
		t.Fatalf("failed to create the skill directory: %v", err)
	}
	manifest := []byte(`{"name":"io.github.hatayama.uloopmcp"}`)
	if err := os.WriteFile(filepath.Join(packageRoot, "package.json"), manifest, 0o644); err != nil {
		t.Fatalf("failed to write the package manifest: %v", err)
	}

	skill := "---\n" +
		"name: uloop-simulate-keyboard\n" +
		"toolName: simulate-keyboard\n" +
		"description: \"Simulate keyboard input from the fixture skill.\"\n" +
		"---\n\n" +
		"| Parameter | Type | Default | Description |\n" +
		"|-----------|------|---------|-------------|\n" +
		"| `--duration` | number | `0` | " + durationDescription + " |\n"
	if err := os.WriteFile(filepath.Join(skillDirectory, "SKILL.md"), []byte(skill), 0o644); err != nil {
		t.Fatalf("failed to write the fixture skill: %v", err)
	}
}
