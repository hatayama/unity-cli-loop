package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies set-code-optimization rejects every mode except the required debug positional argument.
func TestParseSetCodeOptimizationArgumentsRejectsUnsupportedModes(t *testing.T) {
	for _, args := range [][]string{
		{},
		{"release"},
		{"Debug"},
		{"debug", "release"},
	} {
		_, err := parseSetCodeOptimizationArguments(args)
		if err == nil {
			t.Fatalf("args %v must be rejected", args)
		}
		if !strings.Contains(err.Error(), "debug") {
			t.Fatalf("args %v error must name the supported mode: %v", args, err)
		}
	}
}

// Verifies set-code-optimization rejects flags outside its single documented startup flag.
func TestParseSetCodeOptimizationArgumentsRejectsUnknownFlags(t *testing.T) {
	for _, args := range [][]string{
		{"debug", "--release"},
		{"debug", "--startup=true"},
		{"debug", "--startup", "--startup"},
	} {
		_, err := parseSetCodeOptimizationArguments(args)
		if err == nil {
			t.Fatalf("args %v must be rejected", args)
		}
	}
}

// Verifies the project-runner native command switch routes set-code-optimization before dynamic tool lookup.
func TestRunResolvedProjectCommandRoutesSetCodeOptimization(t *testing.T) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	exitCode := runResolvedProjectCommand(
		context.Background(),
		unityipc.Connection{},
		"set-code-optimization",
		[]string{"release"},
		"",
		&stdout,
		&stderr)

	if exitCode != 1 {
		t.Fatalf("unsupported mode exit code = %d, want 1", exitCode)
	}
	if !strings.Contains(stderr.String(), "only debug is supported") {
		t.Fatalf("native route did not return set-code-optimization validation: %s", stderr.String())
	}
}

// Verifies the session-only command selects the existing Debug bridge and preserves its response.
func TestRunSetCodeOptimizationCommandWithoutStartupUsesSessionBridge(t *testing.T) {
	assertSetCodeOptimizationBridgeSelection(
		t,
		[]string{"debug"},
		setCodeOptimizationDebugCommandName,
		`{"Success":true,"Previous":"Release","Current":"Debug"}`,
		"{\n  \"Success\": true,\n  \"Previous\": \"Release\",\n  \"Current\": \"Debug\"\n}\n")
}

// Verifies --startup selects the persistent Debug bridge and preserves its verification fields.
func TestRunSetCodeOptimizationCommandWithStartupUsesPersistentBridge(t *testing.T) {
	assertSetCodeOptimizationBridgeSelection(
		t,
		[]string{"debug", "--startup"},
		setCodeOptimizationDebugStartupCommandName,
		`{"Success":true,"Previous":"Release","Current":"Debug","StartupPrevious":false,"StartupCurrent":true,"StartupVerified":true}`,
		"{\n  \"Success\": true,\n  \"Previous\": \"Release\",\n  \"Current\": \"Debug\",\n  \"StartupPrevious\": false,\n  \"StartupCurrent\": true,\n  \"StartupVerified\": true\n}\n")
}

func assertSetCodeOptimizationBridgeSelection(
	t *testing.T,
	args []string,
	expectedBridgeCommand string,
	response string,
	expectedOutput string,
) {
	t.Helper()
	calledBridgeCommand := ""
	dependencies := setCodeOptimizationCommandDependencies{
		send: func(
			_ context.Context,
			_ unityipc.Connection,
			bridgeCommand string,
		) (json.RawMessage, error) {
			calledBridgeCommand = bridgeCommand
			return json.RawMessage(response), nil
		},
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	exitCode := runSetCodeOptimizationCommandWithDependencies(
		context.Background(),
		unityipc.Connection{},
		args,
		&stdout,
		&stderr,
		dependencies)

	if exitCode != 0 {
		t.Fatalf("command failed: code=%d stderr=%s", exitCode, stderr.String())
	}
	if calledBridgeCommand != expectedBridgeCommand {
		t.Fatalf("bridge command = %q, want %q", calledBridgeCommand, expectedBridgeCommand)
	}
	if stdout.String() != expectedOutput {
		t.Fatalf("stdout mismatch:\n got:\n%s\nwant:\n%s", stdout.String(), expectedOutput)
	}
}

// Verifies a bridge command that fails is reported on stderr with exit 1 and nothing on stdout.
func TestRunSetCodeOptimizationCommandReportsSendFailure(t *testing.T) {
	var sentCommand string
	dependencies := setCodeOptimizationCommandDependencies{
		send: func(_ context.Context, _ unityipc.Connection, bridgeCommand string) (json.RawMessage, error) {
			sentCommand = bridgeCommand
			return nil, io.ErrUnexpectedEOF
		},
	}
	var stdout, stderr bytes.Buffer

	code := runSetCodeOptimizationCommandWithDependencies(
		context.Background(), unityipc.Connection{ProjectRoot: t.TempDir()}, []string{"debug"}, &stdout, &stderr, dependencies)

	if code != 1 || stdout.Len() != 0 {
		t.Fatalf("code=%d stdout=%q", code, stdout.String())
	}
	if sentCommand != setCodeOptimizationDebugCommandName {
		t.Fatalf("sent %q, want %q", sentCommand, setCodeOptimizationDebugCommandName)
	}
	if !strings.Contains(stderr.String(), "unexpected EOF") {
		t.Fatalf("stderr must report the send failure:\n%s", stderr.String())
	}
}

// Verifies the default sender sends the named bridge command and returns Unity's result as is.
func TestSendSetCodeOptimizationBridgeCommandReturnsUnityResult(t *testing.T) {
	server := startFakeUnityResultServer(t, t.TempDir(), setCodeOptimizationDebugStartupCommandName, `{"Success":true,"Message":"startup"}`)

	result, err := sendSetCodeOptimizationBridgeCommand(context.Background(), server.connection, setCodeOptimizationDebugStartupCommandName)

	if err != nil || string(result) != `{"Success":true,"Message":"startup"}` {
		t.Fatalf("result=%s err=%v", result, err)
	}
	server.receivedRequest(t)
}
