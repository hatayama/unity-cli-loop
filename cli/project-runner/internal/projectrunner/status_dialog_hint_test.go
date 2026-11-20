package projectrunner

import (
	"strings"
	"testing"
)

// Verifies the hint added to a failed press names both macOS permissions, Accessibility and
// Automation of System Events, without describing a failed look, and is empty where no permission
// is involved.
func TestDialogInspectorAnswerFailedHintNamesBothMacPermissions(t *testing.T) {
	macOS := dialogAnswerFailedHintFor("darwin")
	if !strings.Contains(macOS, "Accessibility and Automation (System Events)") || strings.Contains(macOS, "could not look") {
		t.Fatalf("macOS hint = %q, want both permissions without the look failure", macOS)
	}
	if windows := dialogAnswerFailedHintFor("windows"); windows != "" {
		t.Fatalf("Windows hint = %q, want none", windows)
	}
}

// Verifies the hint about a failed look names the macOS permissions, Accessibility and Automation
// of System Events, only on macOS; elsewhere it only asks to look at Unity, because Windows needs
// no permission to read another window.
func TestDialogInspectorCheckFailedNextActionDiffersByOperatingSystem(t *testing.T) {
	const askTheUser = "ask the user to look at Unity"

	macOS := dialogCheckFailedNextActionFor("darwin")
	if !strings.Contains(macOS, askTheUser) || !strings.Contains(macOS, "Accessibility and Automation (System Events)") {
		t.Fatalf("macOS hint = %q, want it to ask the user and name both permissions", macOS)
	}

	windows := dialogCheckFailedNextActionFor("windows")
	if !strings.Contains(windows, askTheUser) || strings.Contains(windows, "Accessibility") ||
		strings.Contains(windows, "Automation") || strings.Contains(windows, "macOS") {
		t.Fatalf("Windows hint = %q, want it to ask the user without the macOS permission", windows)
	}
}
