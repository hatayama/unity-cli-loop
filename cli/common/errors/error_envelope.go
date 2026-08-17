package clierrors

import (
	"encoding/json"
	"errors"
	"io"
	"net"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	ErrorCodeInvalidArgument                 = "INVALID_ARGUMENT"
	ErrorCodeUnknownCommand                  = "UNKNOWN_COMMAND"
	errorCodeProjectNotFound                 = "PROJECT_NOT_FOUND"
	ErrorCodeUnityNotReachable               = "UNITY_NOT_REACHABLE"
	ErrorCodeUnityStartupTimeout             = "UNITY_STARTUP_TIMEOUT"
	ErrorCodeUnityProcessExitTimeout         = "UNITY_PROCESS_EXIT_TIMEOUT"
	errorCodeUnityDisconnectedAfterDispatch  = "UNITY_DISCONNECTED_AFTER_DISPATCH"
	errorCodeUnityDisconnectedAfterAccept    = "UNITY_DISCONNECTED_AFTER_ACCEPT"
	errorCodeUnityResponseTimeoutAfterAccept = "UNITY_RESPONSE_TIMEOUT_AFTER_ACCEPT"
	errorCodeUnityEditorUnresponsive         = "UNITY_EDITOR_UNRESPONSIVE"
	errorCodeUnityRPCError                   = "UNITY_RPC_ERROR"
	errorCodeUnityServerBusy                 = "UNITY_SERVER_BUSY"
	ErrorCodeCLIUpdateRequired               = "CLI_UPDATE_REQUIRED"
	ErrorCodeV2ProjectDetected               = "V2_PROJECT_DETECTED"
	ErrorCodeToolDisabled                    = "TOOL_DISABLED"
	ErrorCodeCompileWaitTimeout              = "COMPILE_WAIT_TIMEOUT"
	ErrorCodeControlPlayModeWaitTimeout      = "CONTROL_PLAY_MODE_WAIT_TIMEOUT"
	ErrorCodeControlPlayModeCompileErrors    = "CONTROL_PLAY_MODE_COMPILE_ERRORS"
	ErrorCodeControlPlayModeUnsavedChanges   = "CONTROL_PLAY_MODE_UNSAVED_CHANGES"
	ErrorCodePausePointNotEnabled            = "PAUSE_POINT_NOT_ENABLED"
	ErrorCodePausePointWaitTimeout           = "PAUSE_POINT_WAIT_TIMEOUT"
	ErrorCodePausePointExpired               = "PAUSE_POINT_EXPIRED"
	ErrorCodePausePointCleared               = "PAUSE_POINT_CLEARED"
	ErrorCodePausePointTriggerFailed         = "PAUSE_POINT_TRIGGER_FAILED"
	// ErrorCodePackageManifestInvalid is returned when Packages/manifest.json is missing or not valid JSON.
	ErrorCodePackageManifestInvalid = "PACKAGE_MANIFEST_INVALID"
	// ErrorCodePackageRegistryUnavailable is returned when the OpenUPM registry HTTP lookup fails.
	ErrorCodePackageRegistryUnavailable = "PACKAGE_REGISTRY_UNAVAILABLE"
	ErrorCodeInternalError              = "INTERNAL_ERROR"

	ErrorPhaseArgumentParsing = "argument_parsing"
	ErrorPhaseProjectResolve  = "project_resolution"
	ErrorPhaseDispatch        = "dispatch"
	ErrorPhaseConnection      = "connection"
	ErrorPhaseResponseWaiting = "response_waiting"
	errorPhaseUnityRPC        = "unity_rpc"
	ErrorPhaseCompileWaiting  = "compile_waiting"
	ErrorPhaseExecution       = "execution"
)

type CLIError struct {
	ErrorCode   string         `json:"ErrorCode"`
	Phase       string         `json:"Phase"`
	Message     string         `json:"Message"`
	Retryable   bool           `json:"Retryable"`
	SafeToRetry bool           `json:"SafeToRetry"`
	ProjectRoot string         `json:"ProjectRoot,omitempty"`
	Command     string         `json:"Command,omitempty"`
	NextActions []string       `json:"NextActions"`
	Details     map[string]any `json:"Details,omitempty"`
}

func (err CLIError) Error() string {
	return err.Message
}

type CLIErrorEnvelope struct {
	Success bool     `json:"Success"`
	Error   CLIError `json:"Error"`
}

type ErrorContext struct {
	ProjectRoot string
	Command     string
}

func WriteErrorEnvelope(writer io.Writer, err CLIError) {
	encoder := json.NewEncoder(writer)
	encoder.SetIndent("", "  ")
	_ = encoder.Encode(CLIErrorEnvelope{
		Success: false,
		Error:   err,
	})
}

func WriteClassifiedError(writer io.Writer, err error, context ErrorContext) {
	WriteErrorEnvelope(writer, ClassifyError(err, context))
}

func WriteToolFailure(writer io.Writer, err error, outcome unityipc.UnitySendOutcome, context ErrorContext) {
	if err != nil {
		if outcome.RequestAccepted && isResponseTimeoutError(err) {
			WriteErrorEnvelope(writer, responseTimeoutAfterAcceptError(err, context))
			return
		}
		if IsTransportDisconnectError(err) {
			if outcome.RequestAccepted {
				WriteErrorEnvelope(writer, disconnectedAfterAcceptError(err, context))
				return
			}
			if outcome.RequestDispatched {
				WriteErrorEnvelope(writer, disconnectedAfterDispatchError(err, context))
				return
			}
		}
		var notRespondingErr UnityServerNotRespondingError
		if outcome.RequestDispatched && !outcome.RequestAccepted && errors.As(err, &notRespondingErr) {
			WriteErrorEnvelope(writer, unityServerNotRespondingAfterDispatchError(notRespondingErr, context))
			return
		}
	}
	WriteClassifiedError(writer, err, context)
}

func isResponseTimeoutError(err error) bool {
	var netErr net.Error
	if errors.As(err, &netErr) {
		return netErr.Timeout()
	}
	return false
}

func responseTimeoutAfterAcceptError(err error, context ErrorContext) CLIError {
	return CLIError{
		ErrorCode:   errorCodeUnityResponseTimeoutAfterAccept,
		Phase:       ErrorPhaseResponseWaiting,
		Message:     "Unity accepted the request but did not return a final response before the CLI response timeout.",
		Retryable:   true,
		SafeToRetry: isSafeRetryCommand(context.Command),
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: []string{
			"Check Unity Console logs because Unity may still be running the accepted request.",
			"Retry after Unity finishes the command, compiling, reloading scripts, or restarting the bridge.",
		},
		Details: map[string]any{
			"Cause": err.Error(),
		},
	}
}

func unityServerBusyError(
	rpcErr *unityipc.RPCError,
	details map[string]any,
	data serverBusyErrorData,
	context ErrorContext,
) CLIError {
	if editorActivity := unityServerBusyEditorActivitySummary(data); editorActivity != nil {
		details["EditorActivity"] = editorActivity
	}

	return CLIError{
		ErrorCode:   errorCodeUnityServerBusy,
		Phase:       ErrorPhaseDispatch,
		Message:     unityServerBusyMessage(rpcErr.Message, data, context.Command),
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: unityServerBusyNextActions(data),
		Details:     details,
	}
}

func cliUpdateRequiredError(rpcErr *unityipc.RPCError, details map[string]any, data cliUpdateRequiredErrorData, context ErrorContext) CLIError {
	return CLIError{
		ErrorCode:   ErrorCodeCLIUpdateRequired,
		Phase:       errorPhaseUnityRPC,
		Message:     rpcErr.Message,
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: cliUpdateRequiredNextActions(data),
		Details:     details,
	}
}

func cliUpdateRequiredNextActions(data cliUpdateRequiredErrorData) []string {
	actions := []string{}
	if data.UpdateCommand != "" {
		actions = append(actions, "Run `"+data.UpdateCommand+"`.")
	} else if cliProtocolMismatchIsNewer(data) {
		actions = append(actions, "Update the Unity package to a version that supports this CLI protocol, or install the CLI from the same release as the package.")
	} else {
		actions = append(actions, "Install matching uloop CLI and Unity package versions.")
	}
	actions = append(actions, "Retry the original command after the versions match.")
	return actions
}

func cliProtocolMismatchIsNewer(data cliUpdateRequiredErrorData) bool {
	return data.CurrentProtocolVersion != nil && *data.CurrentProtocolVersion > data.RequiredProtocolVersion
}

func disconnectedAfterAcceptError(err error, context ErrorContext) CLIError {
	return CLIError{
		ErrorCode:   errorCodeUnityDisconnectedAfterAccept,
		Phase:       ErrorPhaseResponseWaiting,
		Message:     "Unity disconnected after accepting the request.",
		Retryable:   true,
		SafeToRetry: isSafeRetryCommand(context.Command),
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: []string{
			"Check Unity Console logs because Unity had already accepted the request.",
			"Retry after Unity finishes compiling, reloading scripts, or restarting the bridge.",
		},
		Details: map[string]any{
			"Cause": err.Error(),
		},
	}
}

func disconnectedAfterDispatchError(err error, context ErrorContext) CLIError {
	return CLIError{
		ErrorCode:   errorCodeUnityDisconnectedAfterDispatch,
		Phase:       ErrorPhaseResponseWaiting,
		Message:     "Unity disconnected after the CLI dispatched the request.",
		Retryable:   true,
		SafeToRetry: isSafeRetryCommand(context.Command),
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: []string{
			"Check Unity Console logs if the command may have changed project or scene state.",
			"Retry after Unity finishes compiling, reloading scripts, or restarting the bridge.",
		},
		Details: map[string]any{
			"Cause": err.Error(),
		},
	}
}

func unityServerNotRespondingAfterDispatchError(err UnityServerNotRespondingError, context ErrorContext) CLIError {
	return CLIError{
		ErrorCode:   ErrorCodeUnityNotReachable,
		Phase:       ErrorPhaseResponseWaiting,
		Message:     "Unity is running for this project, but the Unity CLI Loop server did not acknowledge the dispatched request.",
		Retryable:   true,
		SafeToRetry: false,
		ProjectRoot: firstNonEmpty(context.ProjectRoot, err.ProjectRoot),
		Command:     context.Command,
		NextActions: []string{
			"Check Unity Console logs and project state because Unity may have received the request.",
			"Retry only after confirming the previous command did not run or has finished.",
			"Run `uloop focus-window` if Unity appears stalled in the background.",
		},
		Details: map[string]any{
			"Endpoint": err.Endpoint,
			"Cause":    err.causeText(),
		},
	}
}

func UnknownCommandError(command string, availableCommands []string, context ErrorContext) CLIError {
	return CLIError{
		ErrorCode:   ErrorCodeUnknownCommand,
		Phase:       ErrorPhaseDispatch,
		Message:     "Unknown command: " + command,
		Retryable:   false,
		SafeToRetry: false,
		ProjectRoot: context.ProjectRoot,
		Command:     command,
		NextActions: []string{
			"Run `uloop list` to inspect available commands.",
			"Run `uloop sync` if the local tool cache may be stale.",
		},
		Details: map[string]any{
			"SuggestedCommands": suggestCommands(command, availableCommands),
		},
	}
}

func isSafeRetryCommand(command string) bool {
	switch command {
	case "list", "sync", "get-version", "get-logs", "get-tool-details":
		return true
	default:
		return false
	}
}

func InternalCLIError(message string, context ErrorContext) CLIError {
	return CLIError{
		ErrorCode:   ErrorCodeInternalError,
		Phase:       ErrorPhaseExecution,
		Message:     message,
		Retryable:   false,
		SafeToRetry: false,
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: []string{
			"Read the message and fix the local environment or command input before retrying.",
		},
	}
}
