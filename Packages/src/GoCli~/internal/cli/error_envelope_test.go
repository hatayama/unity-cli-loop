package cli

import (
	"bytes"
	"encoding/json"
	"errors"
	"testing"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/unity"
)

func TestWriteErrorEnvelopeWritesMachineReadableJSON(t *testing.T) {
	var stderr bytes.Buffer

	writeErrorEnvelope(&stderr, cliError{
		ErrorCode:   errorCodeInvalidArgument,
		Phase:       errorPhaseArgumentParsing,
		Message:     "Invalid boolean value for --enabled: maybe",
		Retryable:   false,
		SafeToRetry: false,
		ProjectRoot: "/tmp/MyProject",
		Command:     "sample",
		NextActions: []string{"Pass a valid boolean value for `--enabled`."},
		Details: map[string]any{
			"option":       "--enabled",
			"received":     "maybe",
			"expectedType": "boolean",
		},
	})

	var envelope cliErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Success {
		t.Fatal("error envelope reported success")
	}
	if envelope.Error.ErrorCode != errorCodeInvalidArgument {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Details["option"] != "--enabled" {
		t.Fatalf("details mismatch: %#v", envelope.Error.Details)
	}
}

// Tests that explicit boolean values are returned as structured CLI errors.
func TestBuildToolParamsReturnsStructuredBooleanValueError(t *testing.T) {
	tool := toolDefinition{
		Name: "sample-tool",
		InputSchema: inputSchema{
			Properties: map[string]toolProperty{
				"Enabled": {Type: "boolean"},
			},
		},
	}

	_, _, err := buildToolParams([]string{"--enabled", "true"}, tool)
	if err == nil {
		t.Fatal("expected argument error")
	}

	var argumentErr *argumentError
	if !errors.As(err, &argumentErr) {
		t.Fatalf("expected argumentError, got %T", err)
	}
	cliErr := argumentErr.toCLIError(errorContext{projectRoot: "/tmp/MyProject", command: "sample-tool"})
	if cliErr.ErrorCode != errorCodeInvalidArgument {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Details["expectedType"] != "flag" {
		t.Fatalf("details mismatch: %#v", cliErr.Details)
	}
}

func TestClassifyConnectionAttemptError(t *testing.T) {
	err := &unity.ConnectionAttemptError{
		ProjectRoot: "/tmp/MyProject",
		Endpoint:    "/tmp/uloop/UnityCliLoop-sample.sock",
		Cause:       errors.New("connect: no such file or directory"),
	}

	cliErr := classifyError(err, errorContext{command: "get-logs"})
	if cliErr.ErrorCode != errorCodeUnityNotReachable {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if !cliErr.Retryable || !cliErr.SafeToRetry {
		t.Fatalf("retry flags mismatch: %#v", cliErr)
	}
	if cliErr.ProjectRoot != "/tmp/MyProject" {
		t.Fatalf("project root mismatch: %#v", cliErr)
	}
}

func TestClassifyRPCErrorKeepsData(t *testing.T) {
	err := &unity.RPCError{
		Code:    -32000,
		Message: "Tool blocked by security settings",
		Data:    json.RawMessage(`{"type":"security_blocked","reason":"disabled"}`),
	}

	cliErr := classifyError(err, errorContext{projectRoot: "/tmp/MyProject", command: "execute-dynamic-code"})
	if cliErr.ErrorCode != errorCodeUnityRPCError {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	data, ok := cliErr.Details["data"].(map[string]any)
	if !ok {
		t.Fatalf("rpc data missing: %#v", cliErr.Details)
	}
	if data["type"] != "security_blocked" {
		t.Fatalf("rpc data mismatch: %#v", data)
	}
}

func TestWriteToolFailureClassifiesDispatchedDisconnect(t *testing.T) {
	var stderr bytes.Buffer

	writeToolFailure(
		&stderr,
		errors.New("EOF"),
		unity.SendOutcome{RequestDispatched: true},
		errorContext{projectRoot: "/tmp/MyProject", command: "execute-dynamic-code"},
	)

	var envelope cliErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != errorCodeUnityDisconnectedAfterDispatch {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.SafeToRetry {
		t.Fatalf("stateful command should not be safe to retry: %#v", envelope.Error)
	}
}

func TestProjectLocalCLIMissingError(t *testing.T) {
	cliErr := projectLocalCLIMissingError(
		"/tmp/MyProject/.uloop/bin/uloop-core",
		"/tmp/MyProject",
		"compile",
	)

	if cliErr.ErrorCode != errorCodeProjectLocalCLIMissing {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Details["path"] != "/tmp/MyProject/.uloop/bin/uloop-core" {
		t.Fatalf("details mismatch: %#v", cliErr.Details)
	}
}

func TestUnknownCommandErrorIncludesAvailableCommands(t *testing.T) {
	cliErr := unknownCommandError(
		"missing",
		toolsCache{Tools: []toolDefinition{{Name: "compile"}}},
		errorContext{projectRoot: "/tmp/MyProject"},
	)

	if cliErr.ErrorCode != errorCodeUnknownCommand {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	available, ok := cliErr.Details["availableCommands"].([]string)
	if !ok {
		t.Fatalf("available commands missing: %#v", cliErr.Details)
	}
	if len(available) == 0 || available[len(available)-1] != "compile" {
		t.Fatalf("available commands mismatch: %#v", available)
	}
}

func TestClassifyProjectNotFound(t *testing.T) {
	cliErr := classifyError(
		errors.New("unity project not found. Use --project-path option to specify the target"),
		errorContext{command: "compile"},
	)

	if cliErr.ErrorCode != errorCodeProjectNotFound {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
}

func TestCompileWaitTimeoutError(t *testing.T) {
	cliErr := compileWaitTimeoutError("/tmp/MyProject")

	if cliErr.ErrorCode != errorCodeCompileWaitTimeout {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if !cliErr.Retryable || !cliErr.SafeToRetry {
		t.Fatalf("retry flags mismatch: %#v", cliErr)
	}
	if cliErr.ProjectRoot != "/tmp/MyProject" {
		t.Fatalf("project root mismatch: %#v", cliErr)
	}
}

func TestClassifyConnectionAttemptUsesContextProjectRootFallback(t *testing.T) {
	err := &unity.ConnectionAttemptError{
		Endpoint: "/tmp/uloop/UnityCliLoop-sample.sock",
		Cause:    errors.New("connect failed"),
	}

	cliErr := classifyError(err, errorContext{projectRoot: "/tmp/ContextProject", command: "compile"})
	if cliErr.ProjectRoot != "/tmp/ContextProject" {
		t.Fatalf("project root mismatch: %#v", cliErr)
	}
}

func TestAvailableCommandNamesIncludesBuiltIns(t *testing.T) {
	names := availableCommandNames(toolsCache{})
	expectedBuiltIns := []string{"list", "sync", "focus-window", "fix"}
	for index, expected := range expectedBuiltIns {
		if names[index] != expected {
			t.Fatalf("built-in command mismatch: %#v", names)
		}
	}
}

func TestSafeRetryCommand(t *testing.T) {
	if !isSafeRetryCommand("get-logs") {
		t.Fatal("get-logs should be safe to retry")
	}
	if isSafeRetryCommand("execute-dynamic-code") {
		t.Fatal("execute-dynamic-code should not be safe to retry")
	}
}
