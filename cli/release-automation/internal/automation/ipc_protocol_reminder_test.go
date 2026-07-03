package automation

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestAnalyzeIPCProtocolReminder_WhenNoIPCFilesChanged_DoesNotRemind(t *testing.T) {
	// Verifies ordinary changes do not create protocol bump noise.
	result := AnalyzeIPCProtocolReminder([]string{
		"README.md",
		"Packages/src/Editor/Presentation/UnityCliLoopSettingsWindow.cs",
	})

	if result.NeedsProtocolBumpReview {
		t.Fatalf("unexpected reminder: %#v", result)
	}
	if result.HasIPCContractFileChange {
		t.Fatalf("unexpected IPC file detection: %#v", result)
	}
}

func TestAnalyzeIPCProtocolReminder_WhenIPCFilesChangedWithoutProtocolDeclarations_Reminds(t *testing.T) {
	// Verifies IPC-facing changes surface a non-blocking protocol bump review reminder.
	result := AnalyzeIPCProtocolReminder([]string{
		"cli/common/unityipc/client.go",
		"Packages/src/Editor/Infrastructure/Api/JsonRpcProcessor.cs",
	})

	if !result.NeedsProtocolBumpReview {
		t.Fatalf("expected reminder: %#v", result)
	}
	if len(result.ChangedIPCFiles) != 2 {
		t.Fatalf("changed IPC files mismatch: %#v", result.ChangedIPCFiles)
	}
}

func TestAnalyzeIPCProtocolReminder_WhenProtocolDeclarationsChanged_DoesNotRemind(t *testing.T) {
	// Verifies explicit protocol declaration edits satisfy the reminder check.
	result := AnalyzeIPCProtocolReminder([]string{
		"cli/common/unityipc/client.go",
		"cli/common/clicontract/contract.json",
		"Packages/src/Editor/Domain/CliConstants.cs",
	})

	if result.NeedsProtocolBumpReview {
		t.Fatalf("unexpected reminder: %#v", result)
	}
	if !result.HasProtocolFileChange {
		t.Fatalf("expected protocol file change: %#v", result)
	}
}

func TestAppendIPCProtocolReminderSummary_WritesChangedFilesAndGuidance(t *testing.T) {
	// Verifies the GitHub step summary contains enough context for reviewers.
	summaryPath := filepath.Join(t.TempDir(), "summary.md")
	result := AnalyzeIPCProtocolReminder([]string{
		"Packages/src/Editor/Infrastructure/Api/JsonRpcProcessor.cs",
	})

	if err := AppendIPCProtocolReminderSummary(summaryPath, result); err != nil {
		t.Fatalf("failed to append summary: %v", err)
	}
	content, err := os.ReadFile(summaryPath)
	if err != nil {
		t.Fatalf("failed to read summary: %v", err)
	}
	text := string(content)
	if !strings.Contains(text, "IPC protocol version reminder") {
		t.Fatalf("summary misses heading:\n%s", text)
	}
	if !strings.Contains(text, "JsonRpcProcessor.cs") {
		t.Fatalf("summary misses changed file:\n%s", text)
	}
	if !strings.Contains(text, "REQUIRED_CLI_PROTOCOL_VERSION") {
		t.Fatalf("summary misses protocol guidance:\n%s", text)
	}
}
