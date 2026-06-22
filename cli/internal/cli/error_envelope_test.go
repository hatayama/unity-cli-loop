package cli

import (
	"bytes"
	"encoding/json"
	"errors"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
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
			"Option":       "--enabled",
			"Received":     "maybe",
			"ExpectedType": "boolean",
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
	if envelope.Error.Details["Option"] != "--enabled" {
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
	if cliErr.Details["ExpectedType"] != "flag" {
		t.Fatalf("details mismatch: %#v", cliErr.Details)
	}
}

func TestClassifyConnectionAttemptError(t *testing.T) {
	err := &unityipc.ConnectionAttemptError{
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

func TestClassifyConnectionAttemptAllowsNilCause(t *testing.T) {
	// Verifies connection classification handles a missing low-level cause.
	err := &unityipc.ConnectionAttemptError{
		ProjectRoot: "/tmp/MyProject",
		Endpoint:    "/tmp/uloop/UnityCliLoop-sample.sock",
	}

	cliErr := classifyError(err, errorContext{command: "get-logs"})
	if cliErr.ErrorCode != errorCodeUnityNotReachable {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Details["Cause"] != "" {
		t.Fatalf("cause should be empty for nil unwrap: %#v", cliErr.Details)
	}
}

func TestClassifyUnityServerNotRespondingError(t *testing.T) {
	// Verifies live Unity processes with no responding server avoid restart guidance even when the cause is a connection error.
	cliErr := classifyError(
		unityServerNotRespondingError{
			projectRoot: "/tmp/MyProject",
			endpoint:    "/tmp/uloop/UnityCliLoop-sample.sock",
			cause: &unityipc.ConnectionAttemptError{
				ProjectRoot: "/tmp/MyProject",
				Endpoint:    "/tmp/uloop/UnityCliLoop-sample.sock",
				Cause:       errors.New("connect failed"),
			},
		},
		errorContext{projectRoot: "/tmp/MyProject", command: "get-logs"},
	)

	if cliErr.ErrorCode != errorCodeUnityNotReachable {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if len(cliErr.NextActions) == 0 ||
		cliErr.NextActions[0] != "Wait and retry; Unity may be starting, importing assets, compiling, or reloading scripts." {
		t.Fatalf("next actions mismatch: %#v", cliErr.NextActions)
	}
	for _, action := range cliErr.NextActions {
		if strings.Contains(action, "launch -r") || strings.Contains(strings.ToLower(action), "restart") {
			t.Fatalf("next action should not guide restart: %#v", cliErr.NextActions)
		}
	}
	if cliErr.Details["Endpoint"] != "/tmp/uloop/UnityCliLoop-sample.sock" {
		t.Fatalf("details mismatch: %#v", cliErr.Details)
	}
}

func TestClassifyEditorUnresponsiveError(t *testing.T) {
	// Verifies main-thread stall diagnostics guide users toward modal dialogs instead of generic transport failures.
	err := &unityipc.EditorUnresponsiveError{StallSeconds: 321}

	cliErr := classifyError(err, errorContext{projectRoot: "/tmp/MyProject", command: "get-logs"})
	if cliErr.ErrorCode != errorCodeUnityEditorUnresponsive {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Phase != errorPhaseResponseWaiting {
		t.Fatalf("phase mismatch: %#v", cliErr)
	}
	if strings.Contains(cliErr.Message, "launch -r") || strings.Contains(strings.ToLower(cliErr.Message), "restart") {
		t.Fatalf("message should not include stale restart advice: %s", cliErr.Message)
	}
	if !cliErr.Retryable || !cliErr.SafeToRetry {
		t.Fatalf("retry flags mismatch: %#v", cliErr)
	}
	if cliErr.Details["StallSeconds"] != float64(321) {
		t.Fatalf("stall seconds details mismatch: %#v", cliErr.Details)
	}
	if strings.Contains(cliErr.Details["Cause"].(string), "launch -r") {
		t.Fatalf("cause should not include stale restart advice: %#v", cliErr.Details)
	}
	joinedActions := strings.Join(cliErr.NextActions, "\n")
	if !strings.Contains(joinedActions, "modal dialog") {
		t.Fatalf("next actions should mention modal dialog: %#v", cliErr.NextActions)
	}
	if !strings.Contains(joinedActions, "uloop focus-window") {
		t.Fatalf("next actions should mention focus-window: %#v", cliErr.NextActions)
	}
}

func TestClassifyLaunchStartupTimeoutError(t *testing.T) {
	// Verifies launch startup timeouts do not look like generic reachability or package failures.
	cliErr := classifyError(
		launchStartupTimeoutError{
			projectRoot: "/tmp/MyProject",
			cause:       errors.New("timed out waiting for Unity tool readiness"),
		},
		errorContext{projectRoot: "/tmp/MyProject", command: launchCommandName},
	)

	if cliErr.ErrorCode != errorCodeUnityStartupTimeout {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Message != "Unity is running, but the Editor did not finish startup before the launch timeout." {
		t.Fatalf("message mismatch: %#v", cliErr)
	}
	for _, action := range cliErr.NextActions {
		lowerAction := strings.ToLower(action)
		if strings.Contains(lowerAction, "package") || strings.Contains(lowerAction, "uloop launch") {
			t.Fatalf("next action should avoid package guesses and launch retry guidance: %#v", cliErr.NextActions)
		}
	}
	if cliErr.Details["TimeoutSeconds"] != 600 {
		t.Fatalf("timeout details mismatch: %#v", cliErr.Details)
	}
}

func TestClassifyLaunchProcessExitTimeoutError(t *testing.T) {
	// Verifies restart and quit process-exit timeouts are structured as retryable launch failures.
	cliErr := classifyError(
		launchProcessExitTimeoutError{
			projectRoot: "/tmp/MyProject",
			pid:         123,
			timeout:     launchProcessExitTimeout,
		},
		errorContext{projectRoot: "/tmp/MyProject", command: launchCommandName},
	)

	if cliErr.ErrorCode != errorCodeUnityProcessExitTimeout {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if !cliErr.Retryable || !cliErr.SafeToRetry {
		t.Fatalf("process exit timeout should be retryable: %#v", cliErr)
	}
	if cliErr.Details["Pid"] != 123 {
		t.Fatalf("pid details mismatch: %#v", cliErr.Details)
	}
}

func TestWriteToolFailureWhenServerStopsBeforeAcceptingDispatchedRequestIsNotSafeToRetry(t *testing.T) {
	// Verifies pre-accept server silence does not advertise a dispatched state-changing command as safe to retry.
	var stderr bytes.Buffer

	writeToolFailure(
		&stderr,
		unityServerNotRespondingError{
			projectRoot: "/tmp/MyProject",
			endpoint:    "/tmp/uloop/UnityCliLoop-sample.sock",
			cause:       errors.New("read timeout"),
		},
		unityipc.UnitySendOutcome{RequestDispatched: true},
		errorContext{projectRoot: "/tmp/MyProject", command: "execute-dynamic-code"},
	)

	var envelope cliErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != errorCodeUnityNotReachable {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if !envelope.Error.Retryable || envelope.Error.SafeToRetry {
		t.Fatalf("retry flags mismatch: %#v", envelope.Error)
	}
	if len(envelope.Error.NextActions) == 0 ||
		!strings.Contains(envelope.Error.NextActions[0], "may have received the request") {
		t.Fatalf("next actions mismatch: %#v", envelope.Error.NextActions)
	}
}

func TestClassifyRPCErrorKeepsData(t *testing.T) {
	err := &unityipc.RPCError{
		Code:    -32000,
		Message: "Tool blocked by security settings",
		Data:    json.RawMessage(`{"type":"security_blocked","reason":"disabled"}`),
	}

	cliErr := classifyError(err, errorContext{projectRoot: "/tmp/MyProject", command: "execute-dynamic-code"})
	if cliErr.ErrorCode != errorCodeUnityRPCError {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	data, ok := cliErr.Details["Data"].(map[string]any)
	if !ok {
		t.Fatalf("rpc data missing: %#v", cliErr.Details)
	}
	if data["type"] != "security_blocked" {
		t.Fatalf("rpc data mismatch: %#v", data)
	}
}

func TestClassifyCliUpdateRequiredRPCError(t *testing.T) {
	// Verifies Unity compatibility errors become self-repair guidance for AI clients.
	err := &unityipc.RPCError{
		Code:    -32603,
		Message: "The installed uloop CLI uses an IPC protocol that does not match this Unity package.",
		Data: json.RawMessage(
			`{"type":"cli_update_required","currentCliVersion":"3.0.0-beta.5","currentProtocolVersion":1,"requiredProtocolVersion":2,"updateCommand":"uloop update","retryableAfterUpdate":true}`),
	}

	cliErr := classifyError(err, errorContext{projectRoot: "/tmp/MyProject", command: "compile"})
	if cliErr.ErrorCode != errorCodeCLIUpdateRequired {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if !cliErr.Retryable || !cliErr.SafeToRetry {
		t.Fatalf("retry flags mismatch: %#v", cliErr)
	}
	if len(cliErr.NextActions) == 0 || cliErr.NextActions[0] != "Run `uloop update`." {
		t.Fatalf("next actions mismatch: %#v", cliErr.NextActions)
	}
}

func TestClassifyCliUpdateRequiredRPCErrorForNewerProtocol(t *testing.T) {
	// Verifies a future CLI protocol mismatch does not tell users to update the CLI again.
	err := &unityipc.RPCError{
		Code:    -32603,
		Message: "The installed uloop CLI uses an IPC protocol that does not match this Unity package.",
		Data: json.RawMessage(
			`{"type":"cli_update_required","currentCliVersion":"3.0.0-beta.99","currentProtocolVersion":3,"requiredProtocolVersion":2,"retryableAfterUpdate":true}`),
	}

	cliErr := classifyError(err, errorContext{projectRoot: "/tmp/MyProject", command: "compile"})
	if cliErr.ErrorCode != errorCodeCLIUpdateRequired {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if len(cliErr.NextActions) == 0 || cliErr.NextActions[0] != "Update the Unity package to a version that supports this CLI protocol, or install the CLI from the same release as the package." {
		t.Fatalf("next actions mismatch: %#v", cliErr.NextActions)
	}
}

func TestClassifyServerBusyRPCError(t *testing.T) {
	// Verifies server_busy RPC failures keep retryable classification while using the lightweight message.
	err := &unityipc.RPCError{
		Code:    -32603,
		Message: "Unity is busy running 'compile'. Retry 'get-logs' after the running tool completes.",
		Data: json.RawMessage(
			`{"type":"server_busy","runningToolName":"compile","requestedToolName":"get-logs","isPlaying":true,"isPaused":true,"message":"Unity is busy running 'compile'. Retry 'get-logs' after the running tool completes."}`),
	}

	cliErr := classifyError(err, errorContext{projectRoot: "/tmp/MyProject", command: "get-logs"})
	if cliErr.ErrorCode != errorCodeUnityServerBusy {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Phase != errorPhaseDispatch {
		t.Fatalf("phase mismatch: %#v", cliErr)
	}
	if !cliErr.Retryable || !cliErr.SafeToRetry {
		t.Fatalf("retry flags mismatch: %#v", cliErr)
	}
	expectedMessage := "'get-logs' was not executed because Unity is busy running 'compile'. uloop is single-flight by design; never run uloop commands in parallel. The CLI already retried for up to 10 seconds, so wait for 'compile' to complete and run the command again."
	if cliErr.Message != expectedMessage {
		t.Fatalf("message mismatch: %s", cliErr.Message)
	}
	data, ok := cliErr.Details["Data"].(map[string]any)
	if !ok {
		t.Fatalf("busy data missing: %#v", cliErr.Details)
	}
	if data["isPlaying"] != true || data["isPaused"] != true {
		t.Fatalf("play state mismatch: %#v", data)
	}
}

func TestWriteClassifiedServerBusyRPCErrorWritesBusyStatus(t *testing.T) {
	// Verifies server_busy output avoids the full error envelope because busy is a temporary state.
	err := &unityipc.RPCError{
		Code:    -32603,
		Message: "Unity is busy running 'compile'. Retry 'get-logs' after the running tool completes.",
		Data: json.RawMessage(
			`{"type":"server_busy","runningToolName":"compile","requestedToolName":"get-logs","isPlaying":true,"isPaused":true,"message":"Unity is busy running 'compile'. Retry 'get-logs' after the running tool completes."}`),
	}
	var stderr bytes.Buffer

	writeClassifiedError(&stderr, err, errorContext{projectRoot: "/tmp/MyProject", command: "get-logs"})

	var envelope cliStatusEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Status != cliStatusBusy {
		t.Fatalf("status mismatch: %#v", envelope)
	}
	expectedMessage := "'get-logs' was not executed because Unity is busy running 'compile'. uloop is single-flight by design; never run uloop commands in parallel. The CLI already retried for up to 10 seconds, so wait for 'compile' to complete and run the command again."
	if envelope.Message != expectedMessage {
		t.Fatalf("message mismatch: %#v", envelope)
	}
	if envelope.RunningToolName != "compile" || envelope.RequestedToolName != "get-logs" {
		t.Fatalf("tool names mismatch: %#v", envelope)
	}
	if envelope.IsPlaying == nil || *envelope.IsPlaying != true {
		t.Fatalf("isPlaying mismatch: %#v", envelope)
	}
	if envelope.IsPaused == nil || *envelope.IsPaused != true {
		t.Fatalf("isPaused mismatch: %#v", envelope)
	}
	if bytes.Contains(stderr.Bytes(), []byte("Success")) || bytes.Contains(stderr.Bytes(), []byte("errorCode")) {
		t.Fatalf("busy output should not include error envelope fields: %s", stderr.String())
	}
}

func TestWriteToolFailureClassifiesDispatchedDisconnect(t *testing.T) {
	var stderr bytes.Buffer

	writeToolFailure(
		&stderr,
		errors.New("EOF"),
		unityipc.UnitySendOutcome{RequestDispatched: true},
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

func TestWriteToolFailureClassifiesAcceptedDisconnect(t *testing.T) {
	var stderr bytes.Buffer

	writeToolFailure(
		&stderr,
		errors.New("EOF"),
		unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true},
		errorContext{projectRoot: "/tmp/MyProject", command: "execute-dynamic-code"},
	)

	var envelope cliErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != errorCodeUnityDisconnectedAfterAccept {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Phase != errorPhaseResponseWaiting {
		t.Fatalf("phase mismatch: %#v", envelope.Error)
	}
	if envelope.Error.SafeToRetry {
		t.Fatalf("stateful accepted command should not be safe to retry: %#v", envelope.Error)
	}
}

func TestWriteToolFailureClassifiesAcceptedResponseTimeout(t *testing.T) {
	// Verifies accepted requests that outlive the final response deadline stay retryable and response-scoped.
	var stderr bytes.Buffer

	writeToolFailure(
		&stderr,
		timeoutTestError{},
		unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true},
		errorContext{projectRoot: "/tmp/MyProject", command: "execute-dynamic-code"},
	)

	var envelope cliErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != errorCodeUnityResponseTimeoutAfterAccept {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Phase != errorPhaseResponseWaiting {
		t.Fatalf("phase mismatch: %#v", envelope.Error)
	}
	if !envelope.Error.Retryable {
		t.Fatalf("accepted response timeout should be retryable: %#v", envelope.Error)
	}
	if envelope.Error.SafeToRetry {
		t.Fatalf("stateful accepted command should not be safe to retry: %#v", envelope.Error)
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
	available, ok := cliErr.Details["AvailableCommands"].([]string)
	if !ok {
		t.Fatalf("available commands missing: %#v", cliErr.Details)
	}
	if len(available) == 0 || available[len(available)-1] != "compile" {
		t.Fatalf("available commands mismatch: %#v", available)
	}
}

type timeoutTestError struct{}

func (timeoutTestError) Error() string {
	return "i/o timeout"
}

func (timeoutTestError) Timeout() bool {
	return true
}

func (timeoutTestError) Temporary() bool {
	return true
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

func TestClassifyInstallUnsupportedOS(t *testing.T) {
	// Verifies install platform guards are reported as invalid command input.
	cliErr := classifyError(
		errors.New(installUnsupportedOSMessage),
		errorContext{command: "install"},
	)

	if cliErr.ErrorCode != errorCodeInvalidArgument {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Phase != errorPhaseExecution {
		t.Fatalf("phase mismatch: %#v", cliErr)
	}
	expectedAction := "Run `uloop install` on Windows."
	if len(cliErr.NextActions) == 0 || cliErr.NextActions[0] != expectedAction {
		t.Fatalf("next actions mismatch: %#v", cliErr.NextActions)
	}
}

// Tests that compile wait timeout guidance teaches the caller to verify Editor
// responsiveness instead of assuming a freeze, because agents have terminated
// whole sessions after misreading this timeout as a frozen Editor.
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
	expectedMessage := "Compile status wait timed out after 180000ms. This does not mean the Unity Editor is frozen; the compile may simply still be running."
	if cliErr.Message != expectedMessage {
		t.Fatalf("message mismatch: %#v", cliErr.Message)
	}
	expectedActions := []string{
		"Run a light command such as `uloop get-logs --max-count 1` to check whether Unity is responsive before treating this as a freeze.",
		"If Unity responds, retry `uloop compile`; the previous compile likely finished in the meantime.",
		"Only if Unity does not respond to any command, restart it with `uloop launch -r`.",
	}
	if len(cliErr.NextActions) != len(expectedActions) {
		t.Fatalf("next actions mismatch: %#v", cliErr.NextActions)
	}
	for i, expected := range expectedActions {
		if cliErr.NextActions[i] != expected {
			t.Fatalf("next action %d mismatch: %#v", i, cliErr.NextActions)
		}
	}
}

func TestClassifyConnectionAttemptUsesContextProjectRootFallback(t *testing.T) {
	err := &unityipc.ConnectionAttemptError{
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
	expectedBuiltIns := []string{"launch", "list", "sync", "focus-window", "wait-for-pause-point", "pause-point-status", "skills", "completion", "install", "update"}
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
