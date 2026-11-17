package dispatcher

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
	"github.com/hatayama/unity-cli-loop/common/unityprocess/unitydialog"
)

const olderPackageProtocolNoteFragment = "This uloop dispatcher speaks a newer IPC protocol than this project's Unity package"

// protocolMismatchError is the RPC error the package returns when the protocol versions differ,
// with data as its raw JSON payload.
func protocolMismatchError(data string) error {
	return fmt.Errorf("probe: %w", &unityipc.RPCError{Code: -32000, Message: "CLI protocol mismatch", Data: json.RawMessage(data)})
}

// olderPackageMismatch is the refusal from a package one protocol version behind this dispatcher.
func olderPackageMismatch() error {
	return protocolMismatchError(fmt.Sprintf(
		`{"type":"cli_update_required","currentProtocolVersion":%d,"requiredProtocolVersion":%d,"updateCommand":null}`,
		clicontract.ProtocolVersion(), clicontract.ProtocolVersion()-1))
}

func assertOlderPackageProtocolReady(t *testing.T, run launchRun) {
	t.Helper()
	if run.code != 0 {
		t.Fatalf("exit code = %d, want 0\nstderr=%s", run.code, run.stderr)
	}
	for _, expected := range []string{`"Ready": true`, `"OlderPackageProtocol": true`, olderPackageProtocolNoteFragment} {
		if !strings.Contains(run.stdout, expected) {
			t.Fatalf("launch output missing %q:\n%s", expected, run.stdout)
		}
	}
}

func TestLaunchSucceedsWhenTheEditorRefusesANewerDispatcherProtocol(t *testing.T) {
	// Verifies a fresh launch and a launch onto a running, not-yet-ready Editor both succeed and report OlderPackageProtocol when the package is one protocol behind.
	t.Run("fresh launch", func(t *testing.T) {
		projectRoot := createLaunchTestProject(t)
		deps, _ := safeModePromptLaunchDeps(t, nil, olderPackageMismatch())

		run := runLaunchForTest(t, launchOptions{projectPath: projectRoot, editorVersion: "6000.0.0f1"}, deps)

		assertOlderPackageProtocolReady(t, run)
		if !strings.Contains(run.stdout, `"Launched": true`) {
			t.Fatalf("launch output is not a fresh launch's:\n%s", run.stdout)
		}
	})
	t.Run("running editor", func(t *testing.T) {
		projectRoot := createLaunchTestProject(t)
		deps, _ := safeModePromptLaunchDeps(t, nil, olderPackageMismatch())
		deps.findRunningUnityProcess = func(context.Context, string) (*unityprocess.UnityProcess, error) {
			return &unityprocess.UnityProcess{Pid: 111}, nil
		}

		run := runLaunchForTest(t, launchOptions{projectPath: projectRoot}, deps)

		assertOlderPackageProtocolReady(t, run)
		if !strings.Contains(run.stdout, `"AlreadyRunning": true`) {
			t.Fatalf("launch output is not an already-running launch's:\n%s", run.stdout)
		}
	})
}

func TestIsNewerDispatcherProtocolMismatchAcceptsOnlyAnOlderPackage(t *testing.T) {
	// Verifies only a cli_update_required refusal naming a positive protocol below this dispatcher's counts; equal, newer, missing, null, zero, malformed and other errors do not.
	current := clicontract.ProtocolVersion()
	cases := []struct {
		name string
		err  error
		want bool
	}{
		{name: "older package", err: olderPackageMismatch(), want: true},
		{name: "same protocol", err: protocolMismatchError(fmt.Sprintf(`{"type":"cli_update_required","requiredProtocolVersion":%d}`, current))},
		{name: "newer package", err: protocolMismatchError(fmt.Sprintf(`{"type":"cli_update_required","requiredProtocolVersion":%d}`, current+1))},
		{name: "missing required version", err: protocolMismatchError(`{"type":"cli_update_required"}`)},
		{name: "null required version", err: protocolMismatchError(`{"type":"cli_update_required","requiredProtocolVersion":null}`)},
		{name: "zero required version", err: protocolMismatchError(`{"type":"cli_update_required","requiredProtocolVersion":0}`)},
		{name: "malformed data", err: protocolMismatchError(`{"type":"cli_update_required","requiredProtocolVersion":`)},
		{name: "other rpc error", err: protocolMismatchError(`{"type":"server_busy","requiredProtocolVersion":1}`)},
		{name: "not an rpc error", err: errors.New("connection refused")},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if got := isNewerDispatcherProtocolMismatch(testCase.err); got != testCase.want {
				t.Fatalf("got %t want %t", got, testCase.want)
			}
		})
	}
}

func TestLaunchStillFailsWhenTheDispatcherProtocolIsNotNewer(t *testing.T) {
	// Verifies a refusal that does not prove an older package (dispatcher older, or a zero version) still fails launch as before.
	for name, err := range map[string]error{
		"dispatcher older": protocolMismatchError(fmt.Sprintf(`{"type":"cli_update_required","requiredProtocolVersion":%d,"updateCommand":"uloop update"}`, clicontract.ProtocolVersion()+1)),
		"zero version":     protocolMismatchError(`{"type":"cli_update_required","requiredProtocolVersion":0}`),
	} {
		t.Run(name, func(t *testing.T) {
			projectRoot := createLaunchTestProject(t)
			deps, _ := safeModePromptLaunchDeps(t, nil, err)

			run := runLaunchForTest(t, launchOptions{projectPath: projectRoot, editorVersion: "6000.0.0f1"}, deps)

			if run.code == 0 || strings.Contains(run.stdout, "OlderPackageProtocol") {
				t.Fatalf("expected launch to fail without the field: code=%d stdout=%s", run.code, run.stdout)
			}
		})
	}
}

func TestLaunchReportsBothTheSafeModePromptAndTheOlderPackageProtocol(t *testing.T) {
	// Verifies a launch that pressed Ignore on the Safe Mode prompt and then met an older package reports both fields and both notes.
	projectRoot := createLaunchTestProject(t)
	deps, presses := safeModePromptLaunchDeps(t, []unitydialog.Dialog{safeModePromptDialog()}, olderPackageMismatch())

	run := runLaunchForTest(t, launchOptions{projectPath: projectRoot, editorVersion: "6000.0.0f1"}, deps)

	assertOlderPackageProtocolReady(t, run)
	assertOneIgnorePressOnThePrompt(t, presses)
	for _, expected := range []string{`"SafeModePromptIgnored": true`, "run uloop compile to list them."} {
		if !strings.Contains(run.stdout, expected) {
			t.Fatalf("launch output missing %q:\n%s", expected, run.stdout)
		}
	}
}

func TestLaunchFallbackProbeAcceptsAnOlderPackageRefusal(t *testing.T) {
	// Verifies that when the process scan fails, an older-package refusal from the IPC probe still proves the Editor runs and is reported.
	deps := defaultLaunchDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*unityprocess.UnityProcess, error) {
		return nil, errors.New("failed to retrieve Unity process list: /bin/ps: operation not permitted")
	}
	deps.probeProjectIpcFallback = func(context.Context, string) error {
		return olderPackageMismatch()
	}
	projectRoot := createLaunchTestProject(t)

	run := runLaunchForTest(t, launchOptions{projectPath: projectRoot}, deps)

	assertOlderPackageProtocolReady(t, run)
	if !strings.Contains(run.stdout, "window was not focused") {
		t.Fatalf("expected the fallback message: %s", run.stdout)
	}
}

func TestLaunchQuitDoesNotUseTheIpcProbeFallback(t *testing.T) {
	// Verifies --quit keeps skipping the IPC probe fallback, which cannot provide the process id it must stop.
	deps := defaultLaunchDeps()
	deps.findRunningUnityProcessDefinitively = func(context.Context, string) (*unityprocess.UnityProcess, error) {
		return nil, errors.New("failed to retrieve Unity process list: /bin/ps: operation not permitted")
	}
	deps.probeProjectIpcFallback = func(context.Context, string) error {
		t.Fatal("quit must not consult the IPC probe fallback")
		return nil
	}
	projectRoot := createLaunchTestProject(t)

	run := runLaunchForTest(t, launchOptions{quit: true, projectPath: projectRoot}, deps)

	if run.code != 1 {
		t.Fatalf("expected failure, got %d stdout=%s", run.code, run.stdout)
	}
}

func TestLaunchWithoutAProtocolMismatchLeavesTheResponseUnchanged(t *testing.T) {
	// Verifies an ordinary ready launch prints neither the OlderPackageProtocol field nor its note.
	projectRoot := createLaunchTestProject(t)
	deps, _ := safeModePromptLaunchDeps(t, nil, nil)

	run := runLaunchForTest(t, launchOptions{projectPath: projectRoot, editorVersion: "6000.0.0f1"}, deps)

	if run.code != 0 || strings.Contains(run.stdout, "OlderPackageProtocol") || strings.Contains(run.stdout, olderPackageProtocolNoteFragment) {
		t.Fatalf("expected the plain ready response: code=%d stdout=%s", run.code, run.stdout)
	}
}
