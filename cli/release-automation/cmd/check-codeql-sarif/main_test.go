package main

import (
	"strings"
	"testing"
)

func TestFormatCodeQLWarningUsesGitHubActionsAnnotation(t *testing.T) {
	// Verifies nonblocking quality drift is visible as a GitHub Actions warning annotation.
	warning := formatCodeQLWarning("quality drift")

	if !strings.HasPrefix(warning, "::warning title=CodeQL database quality::") {
		t.Fatalf("expected GitHub Actions warning annotation, got %q", warning)
	}
}
