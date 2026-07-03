package automation

import (
	"strings"
	"testing"
)

// Verifies changes outside the shared release inputs do not require triggers.
func TestReleaseTriggerGuardPassesWithoutSharedInputChanges(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{
		"cli/dispatcher/internal/dispatcher/launch.go",
		"cli/project-runner/internal/projectrunner/run.go",
		"scripts/check-go-cli.sh",
	})

	if len(result.Violations) != 0 {
		t.Fatalf("expected no violations, got %v", result.Violations)
	}
}

// Verifies common source changes require triggers in both release package roots.
func TestReleaseTriggerGuardRequiresBothTriggersForCommonChanges(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{"cli/common/clicore/output.go"})

	if len(result.Violations) != 1 {
		t.Fatalf("expected one violation, got %v", result.Violations)
	}
	violation := result.Violations[0]
	if len(violation.ChangedInputs) != 1 || violation.ChangedInputs[0] != "cli/common/clicore/output.go" {
		t.Fatalf("expected the changed common source to be listed, got %v", violation.ChangedInputs)
	}
	expectedRoots := []string{"cli/dispatcher/", "cli/project-runner/"}
	if len(violation.MissingTriggerRoots) != len(expectedRoots) {
		t.Fatalf("expected missing roots %v, got %v", expectedRoots, violation.MissingTriggerRoots)
	}
	for index, expectedRoot := range expectedRoots {
		if violation.MissingTriggerRoots[index] != expectedRoot {
			t.Fatalf("expected missing roots %v, got %v", expectedRoots, violation.MissingTriggerRoots)
		}
	}
}

// Verifies a partial trigger still fails for the missing package root.
func TestReleaseTriggerGuardDetectsMissingDispatcherTrigger(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{
		"cli/common/clicore/output.go",
		"cli/project-runner/shared-inputs-stamp.json",
	})

	if len(result.Violations) != 1 {
		t.Fatalf("expected one violation, got %v", result.Violations)
	}
	violation := result.Violations[0]
	if len(violation.MissingTriggerRoots) != 1 || violation.MissingTriggerRoots[0] != "cli/dispatcher/" {
		t.Fatalf("expected only cli/dispatcher/ to be missing, got %v", violation.MissingTriggerRoots)
	}
}

// Verifies common changes pass once both release package roots are touched.
func TestReleaseTriggerGuardAcceptsCommonChangesWithBothTriggers(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{
		"cli/common/clicore/output.go",
		"cli/dispatcher/shared-inputs-stamp.json",
		"cli/project-runner/shared-inputs-stamp.json",
	})

	if len(result.Violations) != 0 {
		t.Fatalf("expected no violations, got %v", result.Violations)
	}
}

// Verifies release-please stamp targets and test-only files under common are not release inputs.
func TestReleaseTriggerGuardIgnoresNonBinaryCommonChanges(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{
		"cli/common/clicore/output_test.go",
		"cli/common/clicontract/contract.json",
		"cli/common/tools/default-tools.json",
	})

	if len(result.Violations) != 0 {
		t.Fatalf("expected no violations, got %v", result.Violations)
	}
}

// Verifies common go.mod and go.sum changes count as shared release inputs.
func TestReleaseTriggerGuardCoversCommonModuleFiles(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{"cli/common/go.mod", "cli/common/go.sum"})

	if len(result.Violations) != 1 {
		t.Fatalf("expected one violation, got %v", result.Violations)
	}
	if len(result.Violations[0].ChangedInputs) != 2 {
		t.Fatalf("expected both module files to be listed, got %v", result.Violations[0].ChangedInputs)
	}
}

// Verifies installer script changes require a dispatcher release trigger only.
func TestReleaseTriggerGuardRequiresDispatcherTriggerForInstallerChanges(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{"scripts/install.sh"})

	if len(result.Violations) != 1 {
		t.Fatalf("expected one violation, got %v", result.Violations)
	}
	violation := result.Violations[0]
	if len(violation.MissingTriggerRoots) != 1 || violation.MissingTriggerRoots[0] != "cli/dispatcher/" {
		t.Fatalf("expected only cli/dispatcher/ to be missing, got %v", violation.MissingTriggerRoots)
	}
}

// Verifies installer changes pass once the dispatcher package root is touched.
func TestReleaseTriggerGuardAcceptsInstallerChangesWithDispatcherTrigger(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{
		"scripts/install.ps1",
		"cli/dispatcher/shared-inputs-stamp.json",
	})

	if len(result.Violations) != 0 {
		t.Fatalf("expected no violations, got %v", result.Violations)
	}
}

// Verifies backslash-separated changed paths are normalized before matching.
func TestReleaseTriggerGuardNormalizesWindowsPaths(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{
		`cli\common\clicore\output.go`,
		`cli\dispatcher\shared-inputs-stamp.json`,
		`cli\project-runner\shared-inputs-stamp.json`,
	})

	if len(result.Violations) != 0 {
		t.Fatalf("expected no violations after path normalization, got %v", result.Violations)
	}
}

// Verifies the warning lists the changed inputs, missing roots, and the stamp command.
func TestFormatReleaseTriggerWarningNamesStampCommand(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{"cli/common/clicore/output.go"})

	warning := FormatReleaseTriggerWarning(result)

	for _, expected := range []string{
		"common module sources",
		"`cli/common/clicore/output.go`",
		"`cli/dispatcher/`",
		"`cli/project-runner/`",
		"scripts/stamp-release-inputs.sh",
	} {
		if !strings.Contains(warning, expected) {
			t.Fatalf("expected warning to contain %q, got:\n%s", expected, warning)
		}
	}
}
