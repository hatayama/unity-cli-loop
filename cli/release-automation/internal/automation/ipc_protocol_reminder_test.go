package automation

import (
	"bytes"
	"context"
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
		"Packages/src/Editor/Infrastructure/Api/JsonRpcRequestProcessor.cs",
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
		"Packages/src/Editor/Infrastructure/Api/JsonRpcRequestProcessor.cs",
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
	if !strings.Contains(text, "JsonRpcRequestProcessor.cs") {
		t.Fatalf("summary misses changed file:\n%s", text)
	}
	if !strings.Contains(text, "REQUIRED_CLI_PROTOCOL_VERSION") {
		t.Fatalf("summary misses protocol guidance:\n%s", text)
	}
}

// writeIPCProtocolReminderRepo creates a repository whose second commit touches an IPC-facing file
// without a protocol declaration, and makes it the working directory so git resolves it as the root.
func writeIPCProtocolReminderRepo(t *testing.T) {
	t.Helper()
	repoRoot := t.TempDir()
	runGitInRepo(t, repoRoot, "init", "-b", "main")
	writePackagePinConsistencyFile(t, repoRoot, "README.md", "base\n")
	runGitInRepo(t, repoRoot, "add", "-A")
	runGitInRepo(t, repoRoot, "commit", "-m", "base")
	runGitInRepo(t, repoRoot, "tag", "base")
	writePackagePinConsistencyFile(t, repoRoot, "cli/common/unityipc/client.go", "package unityipc\n")
	runGitInRepo(t, repoRoot, "add", "-A")
	runGitInRepo(t, repoRoot, "commit", "-m", "ipc change")
	t.Chdir(repoRoot)
}

func TestRunIPCProtocolReminder_WhenIPCFileChangesWithoutDeclaration_NotifiesAndWritesSummary(t *testing.T) {
	// Verifies an IPC-only change prints the review notice and appends the step summary.
	writeIPCProtocolReminderRepo(t)
	summaryPath := filepath.Join(t.TempDir(), "summary.md")
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}

	exitCode := RunIPCProtocolReminder(context.Background(), &stdout, &stderr, IPCProtocolReminderConfig{BaseRef: "base", StepSummaryPath: summaryPath})

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr.String())
	}
	if !strings.Contains(stdout.String(), "::notice title=Review IPC protocol version::") {
		t.Fatalf("expected a review notice, got %q", stdout.String())
	}
	summary, err := os.ReadFile(summaryPath)
	if err != nil {
		t.Fatalf("read summary: %v", err)
	}
	if !strings.Contains(string(summary), "- `cli/common/unityipc/client.go`") {
		t.Fatalf("summary does not list the changed file:\n%s", summary)
	}
}

func TestRunIPCProtocolReminder_FailsOnUnusableInputs(t *testing.T) {
	// Verifies a missing base ref, an unknown base ref, and an unwritable summary path each fail with exit code 1.
	cases := []struct {
		name    string
		config  func(t *testing.T) IPCProtocolReminderConfig
		wantErr string
	}{
		{"missing base", func(*testing.T) IPCProtocolReminderConfig { return IPCProtocolReminderConfig{} }, "--base is required"},
		{"unknown base", func(*testing.T) IPCProtocolReminderConfig {
			return IPCProtocolReminderConfig{BaseRef: "no-such-ref"}
		}, "failed to inspect changed files"},
		{"summary is a directory", func(t *testing.T) IPCProtocolReminderConfig {
			return IPCProtocolReminderConfig{BaseRef: "base", StepSummaryPath: t.TempDir()}
		}, "failed to append GitHub step summary"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			writeIPCProtocolReminderRepo(t)
			stdout := bytes.Buffer{}
			stderr := bytes.Buffer{}

			exitCode := RunIPCProtocolReminder(context.Background(), &stdout, &stderr, testCase.config(t))

			if exitCode != 1 {
				t.Fatalf("expected exit code 1, got %d", exitCode)
			}
			if !strings.Contains(stderr.String(), testCase.wantErr) {
				t.Fatalf("stderr = %q, want it to contain %q", stderr.String(), testCase.wantErr)
			}
		})
	}
}

func TestRunIPCProtocolReminder_WhenGitIsUnavailable_Fails(t *testing.T) {
	// Verifies a repository root lookup failure is reported instead of treating the change set as empty.
	t.Setenv("PATH", t.TempDir())
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}

	exitCode := RunIPCProtocolReminder(context.Background(), &stdout, &stderr, IPCProtocolReminderConfig{BaseRef: "base"})

	if exitCode != 1 || !strings.Contains(stderr.String(), "failed to resolve git repository root") {
		t.Fatalf("expected a repository root failure, got exit %d and %q", exitCode, stderr.String())
	}
}

func TestFormatIPCProtocolReminder_WhenDeclarationsChangeWithIPCFiles_AsksToVerifyTheBump(t *testing.T) {
	// Verifies IPC and declaration changes together ask for bump verification, and blank or ./-prefixed paths are normalized.
	result := AnalyzeIPCProtocolReminder([]string{"", "./cli/common/clicontract/contract.json", " cli/common/tools/x.json "})

	message := FormatIPCProtocolReminder(result)

	if !strings.Contains(message, "verify the bump is intentional") {
		t.Fatalf("message = %q", message)
	}
	if len(result.ChangedProtocolFiles) != 1 || result.ChangedProtocolFiles[0] != "cli/common/clicontract/contract.json" {
		t.Fatalf("protocol files = %v", result.ChangedProtocolFiles)
	}
	if strings.Join(result.ChangedIPCFiles, ",") != "cli/common/clicontract/contract.json,cli/common/tools/x.json" {
		t.Fatalf("IPC files = %v", result.ChangedIPCFiles)
	}
}

// Verifies a change set without IPC contract files reports that no contract surface changed.
func TestFormatIPCProtocolReminderWithoutContractChanges(t *testing.T) {
	message := FormatIPCProtocolReminder(IPCProtocolReminderResult{})

	if message != "No IPC contract surfaces changed." {
		t.Fatalf("message = %q", message)
	}
}
