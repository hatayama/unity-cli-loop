package skilldocs

import (
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/vibelog"
)

const noToolSkill = `---
name: some-unrelated-skill
description: "Not a uloop tool skill."
---
`

// readCLIVibeLogs returns the concatenated CLI vibe log content written under projectRoot.
func readCLIVibeLogs(t *testing.T, projectRoot string) string {
	t.Helper()
	pattern := filepath.Join(projectRoot, vibelog.CLIVibeLogDirectory, vibelog.CLIVibeLogPrefix+"_*.json")
	paths, err := filepath.Glob(pattern)
	if err != nil {
		t.Fatalf("failed to glob vibe logs: %v", err)
	}
	var builder strings.Builder
	for _, path := range paths {
		content, err := os.ReadFile(path)
		if err != nil {
			t.Fatalf("failed to read %s: %v", path, err)
		}
		builder.Write(content)
	}
	return builder.String()
}

// Verifies that an empty project root loads nothing even when the working directory holds an
// installed package that a relative lookup would otherwise find.
func TestLoadWithoutAProjectRootReturnsNil(t *testing.T) {
	projectRoot := writeFixtureProject(t, map[string]string{"FirstPartyTools/SimulateKeyboard": singleToolSkill})
	t.Chdir(projectRoot)

	if docs := Load(""); docs != nil {
		t.Fatalf("expected nil docs, got %v", docs)
	}
}

// Verifies that a project without the uloop package loads nothing and, with debug logging on, records
// why the embedded descriptions were kept.
func TestLoadWithoutAPackageLogsTheFallback(t *testing.T) {
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
	projectRoot := t.TempDir()

	if docs := Load(projectRoot); docs != nil {
		t.Fatalf("expected nil docs, got %v", docs)
	}

	logs := readCLIVibeLogs(t, projectRoot)
	if !strings.Contains(logs, skillDocsLogOperation) || !strings.Contains(logs, "uloop package root not found") {
		t.Fatalf("fallback was not logged: %q", logs)
	}
}

// Verifies that unreadable skills and skills documenting no tool are skipped and logged, while the
// readable skills beside them still load and stray files in the container are ignored.
func TestLoadSkipsUnusableSkillsAndKeepsTheRest(t *testing.T) {
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
	projectRoot := writeFixtureProject(t, map[string]string{
		"FirstPartyTools/SimulateKeyboard": singleToolSkill,
		"FirstPartyTools/Unrelated":        noToolSkill,
	})
	firstPartyTools := filepath.Join(projectRoot, "Packages", "src", "Editor", "FirstPartyTools")
	// A directory named SKILL.md passes the existence check but cannot be read as a file.
	if err := os.MkdirAll(filepath.Join(firstPartyTools, "Broken", "Skill", "SKILL.md"), 0o755); err != nil {
		t.Fatalf("failed to create the unreadable skill: %v", err)
	}
	if err := os.MkdirAll(filepath.Join(firstPartyTools, "NoSkill"), 0o755); err != nil {
		t.Fatalf("failed to create the skill-less tool folder: %v", err)
	}
	if err := os.WriteFile(filepath.Join(firstPartyTools, "README.md"), []byte("not a tool"), 0o644); err != nil {
		t.Fatalf("failed to write a stray file: %v", err)
	}

	docs := Load(projectRoot)

	if len(docs) != 1 {
		t.Fatalf("expected only simulate-keyboard, got %v", docs)
	}
	if _, ok := docs["simulate-keyboard"]; !ok {
		t.Fatalf("simulate-keyboard is missing: %v", docs)
	}
	logs := readCLIVibeLogs(t, projectRoot)
	// Exactly one unreadable skill: a tool folder without a SKILL.md must be skipped before reading.
	if count := strings.Count(logs, "skill file could not be read"); count != 1 {
		t.Errorf("expected one unreadable-skill log entry, got %d: %q", count, logs)
	}
	if strings.Contains(logs, "NoSkill") {
		t.Errorf("a tool folder without a skill must not be logged: %q", logs)
	}
	if !strings.Contains(logs, "skill file documented no tool") {
		t.Errorf("log is missing the tool-less skill entry: %q", logs)
	}
}

// Verifies that with debug logging off the fallback leaves no log files behind.
func TestLoadWithoutDebugLoggingWritesNoLog(t *testing.T) {
	t.Setenv(vibelog.CLIVibeLogEnvName, "")
	projectRoot := t.TempDir()

	Load(projectRoot)

	if _, err := os.Stat(filepath.Join(projectRoot, vibelog.CLIVibeLogDirectory)); !os.IsNotExist(err) {
		t.Fatalf("expected no vibe log directory, stat error: %v", err)
	}
}
