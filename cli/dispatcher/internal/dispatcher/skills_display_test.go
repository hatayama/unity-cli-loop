package dispatcher

import (
	"bytes"
	"strings"
	"testing"
)

func TestStatusIconAndTextForOutdated(t *testing.T) {
	// Verifies outdated skills get their own icon and label in listings.
	if statusIcon("outdated") != "^" || statusText("outdated") != "outdated" {
		t.Fatalf("outdated display mismatch: %q %q", statusIcon("outdated"), statusText("outdated"))
	}
}

func TestPrintSkillsSubcommandHelpDescribesV3MigrationUninstall(t *testing.T) {
	// Verifies uninstall-v3-migration help states it removes only the migration skill and omits --output-dir.
	var stdout bytes.Buffer

	printSkillsSubcommandHelp("uninstall-v3-migration", &stdout)

	if !strings.Contains(stdout.String(), "Removes only the temporary V3 CLI invocation migration skill.") {
		t.Fatalf("help missing migration note:\n%s", stdout.String())
	}
	if strings.Contains(stdout.String(), skillsOutputDirFlagName) {
		t.Fatalf("help must not advertise %s:\n%s", skillsOutputDirFlagName, stdout.String())
	}
}

func TestTryHandleSkillsRequestListsDefaultTargetsWithoutFlags(t *testing.T) {
	// Verifies list without target flags shows every default target and omits non-default ones.
	projectRoot := createSkillsTestProject(t)

	code, stdout, stderr := runSkillsRequestForTest(t, projectRoot, "list")

	if code != 0 {
		t.Fatalf("list failed: code=%d stderr=%s", code, stderr)
	}
	for _, targetID := range defaultSkillTargetIDs {
		if !strings.Contains(stdout, targetConfigs[targetID].displayName+" (Project)") {
			t.Fatalf("list missing default target %s:\n%s", targetID, stdout)
		}
	}
	if strings.Contains(stdout, targetConfigs["windsurf"].displayName) {
		t.Fatalf("list must not include non-default targets:\n%s", stdout)
	}
}
