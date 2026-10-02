package automation

import (
	"strings"
	"testing"
)

// Verifies conventional commit titles with common types and forms pass validation.
func TestCheckPRTitleAcceptsValidConventionalCommitTitles(t *testing.T) {
	validTitles := []string{
		"fix: x",
		"feat(scope): x",
		"chore!: x",
		"ci: x",
	}
	for _, title := range validTitles {
		if isValid, message := CheckPRTitle(title); !isValid {
			t.Errorf("expected %q to be valid, got violation message: %q", title, message)
		}
	}
}

// Verifies a title without any conventional commit prefix is rejected, reproducing the PR #1884 incident.
func TestCheckPRTitleRejectsTitleWithoutPrefix(t *testing.T) {
	title := "Round-3 pause-point/dynamic-code usability and reliability fixes"
	if isValid, _ := CheckPRTitle(title); isValid {
		t.Fatalf("expected %q to be rejected", title)
	}
}

// Verifies a title using an unknown conventional commit type is rejected.
func TestCheckPRTitleRejectsUnknownType(t *testing.T) {
	title := "foo: x"
	if isValid, _ := CheckPRTitle(title); isValid {
		t.Fatalf("expected %q to be rejected", title)
	}
}

// Verifies a title missing the required space after the colon is rejected.
func TestCheckPRTitleRejectsMissingSpaceAfterColon(t *testing.T) {
	title := "fix:x"
	if isValid, _ := CheckPRTitle(title); isValid {
		t.Fatalf("expected %q to be rejected", title)
	}
}

// Verifies a title with an empty summary after the colon is rejected.
func TestCheckPRTitleRejectsEmptySummary(t *testing.T) {
	title := "fix: "
	if isValid, _ := CheckPRTitle(title); isValid {
		t.Fatalf("expected %q to be rejected", title)
	}
}

// Verifies an empty title is rejected.
func TestCheckPRTitleRejectsEmptyString(t *testing.T) {
	title := ""
	if isValid, _ := CheckPRTitle(title); isValid {
		t.Fatalf("expected empty title to be rejected")
	}
}

// Verifies a title using an uppercase type token is rejected since types must be lowercase.
func TestCheckPRTitleRejectsUppercaseType(t *testing.T) {
	title := "Fix: x"
	if isValid, _ := CheckPRTitle(title); isValid {
		t.Fatalf("expected %q to be rejected", title)
	}
}

// Verifies the guard prints the violation to stderr with exit code 1 for a bad title and the pass line to stdout for a good one.
func TestRunPRTitleGuardReportsTheOutcome(t *testing.T) {
	stdout := strings.Builder{}
	stderr := strings.Builder{}
	if exitCode := RunPRTitleGuard(&stdout, &stderr, "Update things"); exitCode != 1 {
		t.Fatalf("expected exit code 1 for a bad title, got %d", exitCode)
	}
	if !strings.Contains(stderr.String(), `PR title "Update things" is not a conventional commit header.`) || stdout.Len() != 0 {
		t.Fatalf("stdout = %q, stderr = %q", stdout.String(), stderr.String())
	}

	stdout.Reset()
	stderr.Reset()
	if exitCode := RunPRTitleGuard(&stdout, &stderr, "fix: handle empty input"); exitCode != 0 {
		t.Fatalf("expected exit code 0 for a good title, got %d", exitCode)
	}
	if stdout.String() != "PR title guard passed.\n" || stderr.Len() != 0 {
		t.Fatalf("stdout = %q, stderr = %q", stdout.String(), stderr.String())
	}
}
