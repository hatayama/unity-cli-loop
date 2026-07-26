package clierrors

import (
	"bytes"
	"encoding/json"
	"errors"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies the stderr envelope written for a caller-facing error is machine-readable JSON.
func TestWriteErrorEnvelopeWritesMachineReadableJSON(t *testing.T) {
	var stderr bytes.Buffer

	WriteErrorEnvelope(&stderr, CLIError{
		ErrorCode:   ErrorCodeInvalidArgument,
		Phase:       ErrorPhaseArgumentParsing,
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

	var envelope CLIErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Success {
		t.Fatal("error envelope reported success")
	}
	if envelope.Error.ErrorCode != ErrorCodeInvalidArgument {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Details["Option"] != "--enabled" {
		t.Fatalf("details mismatch: %#v", envelope.Error.Details)
	}
}

// Verifies a connection-attempt failure classifies as a retryable, safe-to-retry reachability error.
func TestClassifyConnectionAttemptError(t *testing.T) {
	err := &unityipc.ConnectionAttemptError{
		ProjectRoot: "/tmp/MyProject",
		Endpoint:    "/tmp/uloop/UnityCliLoop-sample.sock",
		Cause:       errors.New("connect: no such file or directory"),
	}

	cliErr := ClassifyError(err, ErrorContext{Command: "get-logs"})
	if cliErr.ErrorCode != ErrorCodeUnityNotReachable {
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

	cliErr := ClassifyError(err, ErrorContext{Command: "get-logs"})
	if cliErr.ErrorCode != ErrorCodeUnityNotReachable {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Details["Cause"] != "" {
		t.Fatalf("cause should be empty for nil unwrap: %#v", cliErr.Details)
	}
}

func TestClassifyUnityServerNotRespondingError(t *testing.T) {
	// Verifies live Unity processes with no responding server avoid restart guidance even when the cause is a connection error.
	cliErr := ClassifyError(
		UnityServerNotRespondingError{
			ProjectRoot: "/tmp/MyProject",
			Endpoint:    "/tmp/uloop/UnityCliLoop-sample.sock",
			Cause: &unityipc.ConnectionAttemptError{
				ProjectRoot: "/tmp/MyProject",
				Endpoint:    "/tmp/uloop/UnityCliLoop-sample.sock",
				Cause:       errors.New("connect failed"),
			},
		},
		ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "get-logs"},
	)

	if cliErr.ErrorCode != ErrorCodeUnityNotReachable {
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

	cliErr := ClassifyError(err, ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "get-logs"})
	if cliErr.ErrorCode != errorCodeUnityEditorUnresponsive {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Phase != ErrorPhaseResponseWaiting {
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
	if !strings.Contains(joinedActions, "API Update") || !strings.Contains(joinedActions, "never auto-dismiss") {
		t.Fatalf("next actions should mention API Update consent guidance: %#v", cliErr.NextActions)
	}
	if !strings.Contains(joinedActions, "uloop focus-window") {
		t.Fatalf("next actions should mention focus-window: %#v", cliErr.NextActions)
	}
}

func TestWriteToolFailureWhenServerStopsBeforeAcceptingDispatchedRequestIsNotSafeToRetry(t *testing.T) {
	// Verifies pre-accept server silence does not advertise a dispatched state-changing command as safe to retry.
	var stderr bytes.Buffer

	WriteToolFailure(
		&stderr,
		UnityServerNotRespondingError{
			ProjectRoot: "/tmp/MyProject",
			Endpoint:    "/tmp/uloop/UnityCliLoop-sample.sock",
			Cause:       errors.New("read timeout"),
		},
		unityipc.UnitySendOutcome{RequestDispatched: true},
		ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "execute-dynamic-code"},
	)

	var envelope CLIErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != ErrorCodeUnityNotReachable {
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

	cliErr := ClassifyError(err, ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "execute-dynamic-code"})
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

	cliErr := ClassifyError(err, ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "compile"})
	if cliErr.ErrorCode != ErrorCodeCLIUpdateRequired {
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

	cliErr := ClassifyError(err, ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "compile"})
	if cliErr.ErrorCode != ErrorCodeCLIUpdateRequired {
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

	cliErr := ClassifyError(err, ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "get-logs"})
	if cliErr.ErrorCode != errorCodeUnityServerBusy {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Phase != ErrorPhaseDispatch {
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

func TestClassifyServerBusyRPCError_WhenCompiling_IncludesEditorActivityAndGuidance(t *testing.T) {
	// Verifies compiling busy payloads add editor activity details and compile-specific guidance.
	err := &unityipc.RPCError{
		Code:    -32603,
		Message: "Unity is busy running 'unity-compile'.",
		Data: json.RawMessage(
			`{"type":"server_busy","runningToolName":"unity-compile","requestedToolName":"get-logs","isCompiling":true}`),
	}

	cliErr := ClassifyError(err, ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "get-logs"})
	editorActivity, ok := cliErr.Details["EditorActivity"].(map[string]any)
	if !ok {
		t.Fatalf("editor activity missing: %#v", cliErr.Details)
	}
	if editorActivity["isCompiling"] != true {
		t.Fatalf("isCompiling mismatch: %#v", editorActivity)
	}
	if cliErr.NextActions[0] != "Unity is compiling scripts; wait for compilation to finish before retrying." {
		t.Fatalf("next actions mismatch: %#v", cliErr.NextActions)
	}
}

func TestWriteClassifiedServerBusyRPCErrorWritesErrorEnvelope(t *testing.T) {
	// Verifies server_busy output uses the same machine-readable error envelope as other failures.
	err := &unityipc.RPCError{
		Code:    -32603,
		Message: "Unity is busy running 'compile'. Retry 'get-logs' after the running tool completes.",
		Data: json.RawMessage(
			`{"type":"server_busy","runningToolName":"compile","requestedToolName":"get-logs","isPlaying":true,"isPaused":true,"message":"Unity is busy running 'compile'. Retry 'get-logs' after the running tool completes."}`),
	}
	var stderr bytes.Buffer

	WriteClassifiedError(&stderr, err, ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "get-logs"})

	var envelope CLIErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Success {
		t.Fatalf("busy envelope reported success: %#v", envelope)
	}
	if envelope.Error.ErrorCode != errorCodeUnityServerBusy {
		t.Fatalf("error code mismatch: %#v", envelope)
	}
	expectedMessage := "'get-logs' was not executed because Unity is busy running 'compile'. uloop is single-flight by design; never run uloop commands in parallel. The CLI already retried for up to 10 seconds, so wait for 'compile' to complete and run the command again."
	if envelope.Error.Message != expectedMessage {
		t.Fatalf("message mismatch: %#v", envelope)
	}
	data, ok := envelope.Error.Details["Data"].(map[string]any)
	if !ok {
		t.Fatalf("busy data missing: %#v", envelope)
	}
	if data["runningToolName"] != "compile" || data["requestedToolName"] != "get-logs" {
		t.Fatalf("tool names mismatch: %#v", data)
	}
}

func TestWriteErrorEnvelopeServerBusyUsesUnifiedEnvelopeForLegacyLowercaseDataDetails(t *testing.T) {
	// Verifies legacy lower-camel busy details no longer switch to the old status schema.
	cliErr := CLIError{
		ErrorCode: errorCodeUnityServerBusy,
		Message:   "Unity is busy.",
		Command:   "get-logs",
		Details: map[string]any{
			"data": map[string]any{
				"runningToolName":   "compile",
				"requestedToolName": "get-logs",
				"isPlaying":         true,
				"isPaused":          false,
			},
		},
	}
	var stderr bytes.Buffer

	WriteErrorEnvelope(&stderr, cliErr)

	var envelope CLIErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Success {
		t.Fatalf("busy envelope reported success: %#v", envelope)
	}
	if envelope.Error.ErrorCode != errorCodeUnityServerBusy {
		t.Fatalf("error code mismatch: %#v", envelope)
	}
	if bytes.Contains(stderr.Bytes(), []byte(`"Status"`)) {
		t.Fatalf("busy output should not use status schema: %s", stderr.String())
	}
}

func TestWriteToolFailureClassifiesDispatchedDisconnect(t *testing.T) {
	var stderr bytes.Buffer

	WriteToolFailure(
		&stderr,
		errors.New("EOF"),
		unityipc.UnitySendOutcome{RequestDispatched: true},
		ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "execute-dynamic-code"},
	)

	var envelope CLIErrorEnvelope
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

	WriteToolFailure(
		&stderr,
		errors.New("EOF"),
		unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true},
		ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "execute-dynamic-code"},
	)

	var envelope CLIErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != errorCodeUnityDisconnectedAfterAccept {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Phase != ErrorPhaseResponseWaiting {
		t.Fatalf("phase mismatch: %#v", envelope.Error)
	}
	if envelope.Error.SafeToRetry {
		t.Fatalf("stateful accepted command should not be safe to retry: %#v", envelope.Error)
	}
}

func TestWriteToolFailureClassifiesAcceptedResponseTimeout(t *testing.T) {
	// Verifies accepted requests that outlive the final response deadline stay retryable and response-scoped.
	var stderr bytes.Buffer

	WriteToolFailure(
		&stderr,
		timeoutTestError{},
		unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true},
		ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "execute-dynamic-code"},
	)

	var envelope CLIErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != errorCodeUnityResponseTimeoutAfterAccept {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Phase != ErrorPhaseResponseWaiting {
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
	cliErr := UnknownCommandError(
		"missing",
		[]string{"launch", "compile"},
		ErrorContext{ProjectRoot: "/tmp/MyProject"},
	)

	if cliErr.ErrorCode != ErrorCodeUnknownCommand {
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
	cliErr := ClassifyError(
		ProjectNotFoundError{},
		ErrorContext{Command: "compile"},
	)

	if cliErr.ErrorCode != errorCodeProjectNotFound {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
}

func TestClassifyPlainProjectNotFoundTextAsInternalError(t *testing.T) {
	// Verifies project resolution classification depends on typed errors, not copied text.
	cliErr := ClassifyError(
		errors.New("unity project not found. Use --project-path option to specify the target"),
		ErrorContext{Command: "compile"},
	)

	if cliErr.ErrorCode != ErrorCodeInternalError {
		t.Fatalf("plain text error should not classify as project not found: %#v", cliErr)
	}
}

func TestClassifyNotUnityProjectError(t *testing.T) {
	// Verifies explicit non-Unity project paths are classified by error type.
	cliErr := ClassifyError(
		NotUnityProjectError{ProjectRoot: "/tmp/not-unity"},
		ErrorContext{Command: "compile"},
	)

	if cliErr.ErrorCode != errorCodeProjectNotFound {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Message != "not a Unity project: /tmp/not-unity" {
		t.Fatalf("message mismatch: %#v", cliErr)
	}
}

func TestClassifyConnectionAttemptUsesContextProjectRootFallback(t *testing.T) {
	err := &unityipc.ConnectionAttemptError{
		Endpoint: "/tmp/uloop/UnityCliLoop-sample.sock",
		Cause:    errors.New("connect failed"),
	}

	cliErr := ClassifyError(err, ErrorContext{ProjectRoot: "/tmp/ContextProject", Command: "compile"})
	if cliErr.ProjectRoot != "/tmp/ContextProject" {
		t.Fatalf("project root mismatch: %#v", cliErr)
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
