package projectrunner

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/vibelog"
)

// A value no log entry may contain. It is matched as a bare substring so that a parameter value
// leaking without its JSON quotes, for example through fmt.Sprint(params), is caught as well.
const plainToolLogSentinel = "sentinel-9f3c7a1e"

const plainToolLogTestCommand = "get-logs"

// Verifies a plain tool command writes one request entry and one response entry that share a
// correlation ID, name the parameter keys and array lengths, and never carry a parameter value.
func TestRunPlainToolWritesRequestAndResponseVibeLogs(t *testing.T) {
	// Both forms the CLI parses an array option into: a JSON array and a comma-separated list.
	arrayForms := map[string]any{
		"json array": []any{plainToolLogSentinel + "-first", plainToolLogSentinel + "-second"},
		"comma list": []string{plainToolLogSentinel + "-first", plainToolLogSentinel + "-second"},
	}
	for name, files := range arrayForms {
		t.Run(name, func(t *testing.T) {
			enableCliVibeLog(t)
			projectRoot := t.TempDir()
			params := map[string]any{"files": files, "verbose": true}

			result := runPlainToolAgainstFakeUnityResult(t, projectRoot, params, `{"Success":true}`)

			if result.exitCode != 0 {
				t.Fatalf("exit code = %d, want 0", result.exitCode)
			}
			logContent := readOnlyCliVibeLog(t, projectRoot)
			sent := singleCliVibeEntry(t, logContent, "cli_tool_request_sent")
			received := singleCliVibeEntry(t, logContent, "cli_tool_response_received")
			assertCliVibeEntryLevel(t, sent, "INFO")
			assertCliVibeEntryLevel(t, received, "INFO")
			sentContext := cliVibeEntryContext(t, sent)
			receivedContext := cliVibeEntryContext(t, received)
			if sentContext["command"] != plainToolLogTestCommand || receivedContext["command"] != plainToolLogTestCommand {
				t.Fatalf("command mismatch: sent %#v, received %#v", sentContext["command"], receivedContext["command"])
			}
			if sentContext["project_identity"] != vibelog.ProjectIdentity(projectRoot) {
				t.Fatalf("project_identity = %#v, want %q", sentContext["project_identity"], vibelog.ProjectIdentity(projectRoot))
			}
			if got := fmt.Sprint(sentContext["param_keys"]); got != "[files verbose]" {
				t.Fatalf("param_keys = %s, want [files verbose]", got)
			}
			arrayLengths, ok := sentContext["array_lengths"].(map[string]any)
			if !ok || arrayLengths["files"] != float64(2) || len(arrayLengths) != 1 {
				t.Fatalf("array_lengths = %#v, want only files: 2", sentContext["array_lengths"])
			}
			assertSharedCliVibeCorrelationID(t, sent, received)
			if receivedContext["exit_code"] != float64(0) {
				t.Fatalf("exit_code = %#v, want 0", receivedContext["exit_code"])
			}
			if resultBytes, ok := receivedContext["result_bytes"].(float64); !ok || resultBytes <= 0 {
				t.Fatalf("result_bytes = %#v, want a positive number", receivedContext["result_bytes"])
			}
			// The fake answers with the final frame only, never with an accepted frame first.
			if receivedContext["request_accepted"] != false {
				t.Fatalf("request_accepted = %#v, want false", receivedContext["request_accepted"])
			}
			assertCliVibeLogOmitsTheSentinel(t, logContent)
		})
	}
}

// Verifies a tool that answers with a failed envelope is logged as a received response with the
// command's exit code 1, not as a failed request: the transport delivered the answer.
func TestRunPlainToolLogsTheEnvelopeExitCodeOfAFailedTool(t *testing.T) {
	enableCliVibeLog(t)
	projectRoot := t.TempDir()

	result := runPlainToolAgainstFakeUnityResult(t, projectRoot, map[string]any{}, `{"Success":false,"Message":"x"}`)

	if result.exitCode != 1 {
		t.Fatalf("exit code = %d, want 1", result.exitCode)
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	received := singleCliVibeEntry(t, logContent, "cli_tool_response_received")
	assertCliVibeEntryLevel(t, received, "INFO")
	if exitCode := cliVibeEntryContext(t, received)["exit_code"]; exitCode != float64(1) {
		t.Fatalf("exit_code = %#v, want 1", exitCode)
	}
	if failed := cliVibeEntriesForOperation(t, logContent, "cli_tool_request_failed"); len(failed) != 0 {
		t.Fatalf("cli_tool_request_failed entries = %d, want 0", len(failed))
	}
}

// Verifies a request Unity answers with an RPC error writes one failure entry, classified as an
// RPC error, that shares the request's correlation ID and carries no parameter value.
func TestRunPlainToolWritesFailureVibeLog(t *testing.T) {
	enableCliVibeLog(t)
	projectRoot := t.TempDir()
	params := map[string]any{"files": []any{plainToolLogSentinel}}

	result := runPlainToolAgainstFakeUnity(t, projectRoot, params, testUnityRPCFailureResponse)

	if result.exitCode != 1 {
		t.Fatalf("exit code = %d, want 1", result.exitCode)
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	sent := singleCliVibeEntry(t, logContent, "cli_tool_request_sent")
	failed := singleCliVibeEntry(t, logContent, "cli_tool_request_failed")
	assertCliVibeEntryLevel(t, failed, "ERROR")
	if errorKind, _ := cliVibeEntryContext(t, failed)["error_kind"].(string); !strings.HasPrefix(errorKind, "rpc:") {
		t.Fatalf("error_kind = %q, want an rpc: kind", errorKind)
	}
	assertSharedCliVibeCorrelationID(t, sent, failed)
	if received := cliVibeEntriesForOperation(t, logContent, "cli_tool_response_received"); len(received) != 0 {
		t.Fatalf("cli_tool_response_received entries = %d, want 0", len(received))
	}
	assertCliVibeLogOmitsTheSentinel(t, logContent)
}

// Verifies the failure entry does not copy Unity's error message, which can quote the raw
// parameter values when Unity rejects a parameter.
func TestRunPlainToolDoesNotCopyTheUnityErrorMessage(t *testing.T) {
	enableCliVibeLog(t)
	projectRoot := t.TempDir()
	response := strings.Replace(
		testUnityRPCFailureResponse,
		"tool exploded in Unity",
		"Invalid value '"+plainToolLogSentinel+"' for parameter",
		1)

	runPlainToolAgainstFakeUnity(t, projectRoot, map[string]any{}, response)

	logContent := readOnlyCliVibeLog(t, projectRoot)
	singleCliVibeEntry(t, logContent, "cli_tool_request_failed")
	assertCliVibeLogOmitsTheSentinel(t, logContent)
}

// Verifies the error kind names what failed without any of the error's text.
func TestClassifyPlainToolErrorNamesTheKindWithoutTheMessage(t *testing.T) {
	cases := []struct {
		name string
		err  error
		want string
	}{
		{
			name: "rpc error with a data type",
			err:  &unityipc.RPCError{Message: plainToolLogSentinel, Data: []byte(`{"type":"server_busy"}`)},
			want: "rpc:server_busy",
		},
		{
			name: "rpc error without data",
			err:  &unityipc.RPCError{Message: plainToolLogSentinel},
			want: "rpc:",
		},
		{
			name: "wrapped rpc error",
			err:  fmt.Errorf("send failed: %w", &unityipc.RPCError{Message: plainToolLogSentinel}),
			want: "rpc:",
		},
		{
			name: "final response timeout",
			err:  timeoutOnlyError{},
			want: "final_response_timeout",
		},
		{
			name: "anything else",
			err:  errors.New(plainToolLogSentinel),
			want: "other",
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if got := classifyPlainToolError(testCase.err); got != testCase.want {
				t.Fatalf("classifyPlainToolError = %q, want %q", got, testCase.want)
			}
		})
	}
}

// Verifies a plain tool command with the log off leaves no log directory behind.
func TestRunPlainToolWritesNothingWhenDebugIsOff(t *testing.T) {
	// Explicitly off, so the test also holds when it runs from a shell that exported ULOOP_DEBUG.
	t.Setenv(vibelog.CLIVibeLogEnvName, "")
	projectRoot := t.TempDir()

	runPlainToolAgainstFakeUnityResult(t, projectRoot, map[string]any{"verbose": true}, `{"Success":true}`)

	logDirectory := filepath.Join(projectRoot, vibelog.CLIVibeLogDirectory)
	if _, err := os.Stat(logDirectory); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("the log directory must not exist when the log is off: %v", err)
	}
}

// Verifies the entry is not even built when the log is off. The file system cannot show this:
// vibelog.WriteCLIVibeLog writes nothing when the log is off, whoever calls it.
func TestWritePlainToolVibeLogDoesNotBuildTheEntryWhenDisabled(t *testing.T) {
	t.Setenv(vibelog.CLIVibeLogEnvName, "")

	writePlainToolVibeLog(t.TempDir(), func() vibelog.CLIVibeLogEntry {
		t.Fatal("the entry must not be built while the log is off")
		return vibelog.CLIVibeLogEntry{}
	})
}

// Runs the plain tool path against a fake Unity that answers the one request with result.
func runPlainToolAgainstFakeUnityResult(
	t *testing.T,
	projectRoot string,
	params map[string]any,
	result string,
) toolExecutionResult {
	t.Helper()
	return runPlainToolAgainstFakeUnity(t, projectRoot, params, fmt.Sprintf(`{"jsonrpc":"2.0","result":%s,"id":1}`, result))
}

// Runs the plain tool path against a fake Unity that answers the one request with the raw
// JSON-RPC response, so a test can return an error as well as a result.
func runPlainToolAgainstFakeUnity(
	t *testing.T,
	projectRoot string,
	params map[string]any,
	response string,
) toolExecutionResult {
	t.Helper()
	server := startFakeUnityServer(t, projectRoot, plainToolLogTestCommand, response)
	var stderr bytes.Buffer
	result := runPlainTool(context.Background(), server.connection, plainToolLogTestCommand, params, &stderr)
	server.receivedRequest(t)
	return result
}

// Returns the only entry of the operation in the log.
func singleCliVibeEntry(t *testing.T, logContent string, operation string) map[string]any {
	t.Helper()
	entries := cliVibeEntriesForOperation(t, logContent, operation)
	if len(entries) != 1 {
		t.Fatalf("%s entries = %d, want 1\n%s", operation, len(entries), logContent)
	}
	return entries[0]
}

// Returns the entry's context as decoded JSON, so a test can read arrays, objects and booleans.
func cliVibeEntryContext(t *testing.T, entry map[string]any) map[string]any {
	t.Helper()
	contextMap, ok := entry["context"].(map[string]any)
	if !ok {
		t.Fatalf("vibe log context missing: %#v", entry)
	}
	return contextMap
}

func assertCliVibeEntryLevel(t *testing.T, entry map[string]any, want string) {
	t.Helper()
	if entry["level"] != want {
		t.Fatalf("%v level = %#v, want %s", entry["operation"], entry["level"], want)
	}
}

// Asserts both entries carry one correlation ID, in their context and at the top level.
func assertSharedCliVibeCorrelationID(t *testing.T, first map[string]any, second map[string]any) {
	t.Helper()
	firstID := vibeLogContextString(t, first, "correlation_id")
	secondID := vibeLogContextString(t, second, "correlation_id")
	if firstID == "" || firstID != secondID {
		t.Fatalf("correlation_id must be shared: %q, %q", firstID, secondID)
	}
	if first["correlation_id"] != firstID || second["correlation_id"] != secondID {
		t.Fatalf("top-level correlation_id must match the context: %#v, %#v", first["correlation_id"], second["correlation_id"])
	}
}

func assertCliVibeLogOmitsTheSentinel(t *testing.T, logContent string) {
	t.Helper()
	if strings.Contains(logContent, plainToolLogSentinel) {
		t.Fatalf("the vibe log must not contain a parameter value or an error message:\n%s", logContent)
	}
}
