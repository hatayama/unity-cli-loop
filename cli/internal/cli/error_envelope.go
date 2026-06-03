package cli

import (
	"encoding/json"
	"errors"
	"io"
	"net"
	"strings"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

const (
	errorCodeInvalidArgument                 = "INVALID_ARGUMENT"
	errorCodeUnknownCommand                  = "UNKNOWN_COMMAND"
	errorCodeProjectNotFound                 = "PROJECT_NOT_FOUND"
	errorCodeUnityNotReachable               = "UNITY_NOT_REACHABLE"
	errorCodeUnityDisconnectedAfterDispatch  = "UNITY_DISCONNECTED_AFTER_DISPATCH"
	errorCodeUnityDisconnectedAfterAccept    = "UNITY_DISCONNECTED_AFTER_ACCEPT"
	errorCodeUnityResponseTimeoutAfterAccept = "UNITY_RESPONSE_TIMEOUT_AFTER_ACCEPT"
	errorCodeUnityRPCError                   = "UNITY_RPC_ERROR"
	errorCodeUnityServerBusy                 = "UNITY_SERVER_BUSY"
	errorCodeCLIUpdateRequired               = "CLI_UPDATE_REQUIRED"
	errorCodeCompileWaitTimeout              = "COMPILE_WAIT_TIMEOUT"
	errorCodeControlPlayModeWaitTimeout      = "CONTROL_PLAY_MODE_WAIT_TIMEOUT"
	errorCodeDebugBreakNotArmed              = "DEBUG_BREAK_NOT_ARMED"
	errorCodeDebugBreakWaitTimeout           = "DEBUG_BREAK_WAIT_TIMEOUT"
	errorCodeDebugBreakExpired               = "DEBUG_BREAK_EXPIRED"
	errorCodeDebugBreakCleared               = "DEBUG_BREAK_CLEARED"
	errorCodeInternalError                   = "INTERNAL_ERROR"

	errorPhaseArgumentParsing = "argument_parsing"
	errorPhaseProjectResolve  = "project_resolution"
	errorPhaseDispatch        = "dispatch"
	errorPhaseConnection      = "connection"
	errorPhaseResponseWaiting = "response_waiting"
	errorPhaseUnityRPC        = "unity_rpc"
	errorPhaseCompileWaiting  = "compile_waiting"
	errorPhaseExecution       = "execution"
)

type cliError struct {
	ErrorCode   string         `json:"errorCode"`
	Phase       string         `json:"phase"`
	Message     string         `json:"message"`
	Retryable   bool           `json:"retryable"`
	SafeToRetry bool           `json:"safeToRetry"`
	ProjectRoot string         `json:"projectRoot,omitempty"`
	Command     string         `json:"command,omitempty"`
	NextActions []string       `json:"nextActions"`
	Details     map[string]any `json:"details,omitempty"`
}

func (err cliError) Error() string {
	return err.Message
}

type cliErrorEnvelope struct {
	Success bool     `json:"success"`
	Error   cliError `json:"error"`
}

type errorContext struct {
	projectRoot string
	command     string
}

func writeErrorEnvelope(writer io.Writer, err cliError) {
	if err.ErrorCode == errorCodeUnityServerBusy {
		writeBusyStatusEnvelope(writer, err.Message, serverBusyStatusDetailsFromError(err))
		return
	}
	encoder := json.NewEncoder(writer)
	encoder.SetIndent("", "  ")
	_ = encoder.Encode(cliErrorEnvelope{
		Success: false,
		Error:   err,
	})
}

func writeClassifiedError(writer io.Writer, err error, context errorContext) {
	writeErrorEnvelope(writer, classifyError(err, context))
}

func writeToolFailure(writer io.Writer, err error, outcome unityipc.UnitySendOutcome, context errorContext) {
	if err != nil {
		if outcome.RequestAccepted && isResponseTimeoutError(err) {
			writeErrorEnvelope(writer, responseTimeoutAfterAcceptError(err, context))
			return
		}
		if isTransportDisconnectError(err) {
			if outcome.RequestAccepted {
				writeErrorEnvelope(writer, disconnectedAfterAcceptError(err, context))
				return
			}
			if outcome.RequestDispatched {
				writeErrorEnvelope(writer, disconnectedAfterDispatchError(err, context))
				return
			}
		}
		var notRespondingErr unityServerNotRespondingError
		if outcome.RequestDispatched && !outcome.RequestAccepted && errors.As(err, &notRespondingErr) {
			writeErrorEnvelope(writer, unityServerNotRespondingAfterDispatchError(notRespondingErr, context))
			return
		}
	}
	writeClassifiedError(writer, err, context)
}

func isResponseTimeoutError(err error) bool {
	var netErr net.Error
	if errors.As(err, &netErr) {
		return netErr.Timeout()
	}
	return false
}

func responseTimeoutAfterAcceptError(err error, context errorContext) cliError {
	return cliError{
		ErrorCode:   errorCodeUnityResponseTimeoutAfterAccept,
		Phase:       errorPhaseResponseWaiting,
		Message:     "Unity accepted the request but did not return a final response before the CLI response timeout.",
		Retryable:   true,
		SafeToRetry: isSafeRetryCommand(context.command),
		ProjectRoot: context.projectRoot,
		Command:     context.command,
		NextActions: []string{
			"Check Unity Console logs because Unity may still be running the accepted request.",
			"Retry after Unity finishes the command, compiling, reloading scripts, or restarting the bridge.",
		},
		Details: map[string]any{
			"cause": err.Error(),
		},
	}
}

func classifyError(err error, context errorContext) cliError {
	if err == nil {
		return internalCLIError("unknown CLI error", context)
	}

	var argumentErr *argumentError
	if errors.As(err, &argumentErr) {
		return argumentErr.toCLIError(context)
	}

	var notRespondingErr unityServerNotRespondingError
	if errors.As(err, &notRespondingErr) {
		return cliError{
			ErrorCode:   errorCodeUnityNotReachable,
			Phase:       errorPhaseConnection,
			Message:     "Unity is running for this project, but the Unity CLI Loop server is not responding.",
			Retryable:   true,
			SafeToRetry: true,
			ProjectRoot: firstNonEmpty(context.projectRoot, notRespondingErr.projectRoot),
			Command:     context.command,
			NextActions: []string{
				"Wait and retry; Unity may be starting, importing assets, compiling, or reloading scripts.",
				"Run `uloop focus-window` if Unity appears stalled in the background.",
				"Confirm that the command targets the intended Unity project and the Editor package is installed.",
			},
			Details: map[string]any{
				"endpoint": notRespondingErr.endpoint,
				"cause":    notRespondingErr.causeText(),
			},
		}
	}

	var connectionErr *unityipc.ConnectionAttemptError
	if errors.As(err, &connectionErr) {
		return cliError{
			ErrorCode:   errorCodeUnityNotReachable,
			Phase:       errorPhaseConnection,
			Message:     "The Unity CLI Loop server is not reachable for this project.",
			Retryable:   true,
			SafeToRetry: true,
			ProjectRoot: firstNonEmpty(context.projectRoot, connectionErr.ProjectRoot),
			Command:     context.command,
			NextActions: []string{
				"If Unity is closed, run `uloop launch`.",
				"If Unity is starting, compiling, or reloading scripts, wait and retry.",
				"Confirm that the command targets the intended Unity project.",
			},
			Details: map[string]any{
				"endpoint": connectionErr.Endpoint,
				"cause":    connectionAttemptCause(connectionErr),
			},
		}
	}

	var rpcErr *unityipc.RPCError
	if errors.As(err, &rpcErr) {
		details := map[string]any{
			"code":    rpcErr.Code,
			"message": rpcErr.Message,
		}
		var decodedData map[string]any
		if len(rpcErr.Data) > 0 {
			var data any
			if json.Unmarshal(rpcErr.Data, &data) == nil {
				details["data"] = data
				if typedData, ok := data.(map[string]any); ok {
					decodedData = typedData
				}
			} else {
				details["data"] = string(rpcErr.Data)
			}
		}
		if rpcDataType(decodedData) == "cli_update_required" {
			return cliUpdateRequiredError(rpcErr, details, decodedData, context)
		}
		if rpcDataType(decodedData) == "server_busy" {
			return unityServerBusyError(rpcErr, details, decodedData, context)
		}
		return cliError{
			ErrorCode:   errorCodeUnityRPCError,
			Phase:       errorPhaseUnityRPC,
			Message:     rpcErr.Message,
			Retryable:   false,
			SafeToRetry: false,
			ProjectRoot: context.projectRoot,
			Command:     context.command,
			NextActions: []string{
				"Read the Unity error details and fix the request or project state before retrying.",
			},
			Details: details,
		}
	}

	message := err.Error()
	if message == "unity project not found. Use --project-path option to specify the target" ||
		strings.HasPrefix(message, "not a Unity project:") ||
		strings.HasPrefix(message, "--project-path does not point to a Unity project:") {
		return cliError{
			ErrorCode:   errorCodeProjectNotFound,
			Phase:       errorPhaseProjectResolve,
			Message:     message,
			Retryable:   false,
			SafeToRetry: false,
			Command:     context.command,
			NextActions: []string{
				"Run the command from inside a Unity project.",
				"Pass `--project-path <path>` when targeting another Unity project.",
			},
		}
	}

	if message == updateUnsupportedOSMessage {
		return cliError{
			ErrorCode:   errorCodeInvalidArgument,
			Phase:       errorPhaseExecution,
			Message:     message,
			Retryable:   false,
			SafeToRetry: false,
			Command:     context.command,
			NextActions: []string{
				"Run `uloop update` on macOS or Windows.",
				"Install the latest uloop launcher manually on this platform.",
			},
		}
	}

	if message == installUnsupportedOSMessage {
		return cliError{
			ErrorCode:   errorCodeInvalidArgument,
			Phase:       errorPhaseExecution,
			Message:     message,
			Retryable:   false,
			SafeToRetry: false,
			Command:     context.command,
			NextActions: []string{
				"Run `uloop install` on Windows.",
				"Use the platform-specific installer for this system.",
			},
		}
	}

	if message == uninstallUnsupportedOSMessage {
		return cliError{
			ErrorCode:   errorCodeInvalidArgument,
			Phase:       errorPhaseExecution,
			Message:     message,
			Retryable:   false,
			SafeToRetry: false,
			Command:     context.command,
			NextActions: []string{
				"Run `uloop uninstall` on macOS or Windows.",
				"Remove the uloop launcher binary manually on this platform.",
			},
		}
	}

	return internalCLIError(message, context)
}

func connectionAttemptCause(err *unityipc.ConnectionAttemptError) string {
	if err == nil {
		return ""
	}
	cause := err.Unwrap()
	if cause == nil {
		return ""
	}
	return cause.Error()
}

func rpcDataType(data map[string]any) string {
	if data == nil {
		return ""
	}
	value, ok := data["type"].(string)
	if !ok {
		return ""
	}
	return value
}

func unityServerBusyError(
	rpcErr *unityipc.RPCError,
	details map[string]any,
	data map[string]any,
	context errorContext,
) cliError {
	return cliError{
		ErrorCode:   errorCodeUnityServerBusy,
		Phase:       errorPhaseDispatch,
		Message:     unityServerBusyMessage(rpcErr.Message, data, context.command),
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: context.projectRoot,
		Command:     context.command,
		NextActions: []string{
			"Wait for the running Unity command to complete.",
			"Retry the command after Unity reports it is no longer busy.",
		},
		Details: details,
	}
}

func cliUpdateRequiredError(rpcErr *unityipc.RPCError, details map[string]any, data map[string]any, context errorContext) cliError {
	return cliError{
		ErrorCode:   errorCodeCLIUpdateRequired,
		Phase:       errorPhaseUnityRPC,
		Message:     rpcErr.Message,
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: context.projectRoot,
		Command:     context.command,
		NextActions: cliUpdateRequiredNextActions(data),
		Details:     details,
	}
}

func cliUpdateRequiredNextActions(data map[string]any) []string {
	updateCommand, _ := data["updateCommand"].(string)
	targetCommand, _ := data["targetUpdateCommand"].(string)
	actions := []string{}
	if updateCommand != "" {
		actions = append(actions, "Run `"+updateCommand+"`.")
	}
	if targetCommand != "" && targetCommand != updateCommand {
		actions = append(actions, "If your CLI supports exact updates, run `"+targetCommand+"` instead.")
	}
	actions = append(actions, "Retry the original command after the update completes.")
	return actions
}

func disconnectedAfterAcceptError(err error, context errorContext) cliError {
	return cliError{
		ErrorCode:   errorCodeUnityDisconnectedAfterAccept,
		Phase:       errorPhaseResponseWaiting,
		Message:     "Unity disconnected after accepting the request.",
		Retryable:   true,
		SafeToRetry: isSafeRetryCommand(context.command),
		ProjectRoot: context.projectRoot,
		Command:     context.command,
		NextActions: []string{
			"Check Unity Console logs because Unity had already accepted the request.",
			"Retry after Unity finishes compiling, reloading scripts, or restarting the bridge.",
		},
		Details: map[string]any{
			"cause": err.Error(),
		},
	}
}

func disconnectedAfterDispatchError(err error, context errorContext) cliError {
	return cliError{
		ErrorCode:   errorCodeUnityDisconnectedAfterDispatch,
		Phase:       errorPhaseResponseWaiting,
		Message:     "Unity disconnected after the CLI dispatched the request.",
		Retryable:   true,
		SafeToRetry: isSafeRetryCommand(context.command),
		ProjectRoot: context.projectRoot,
		Command:     context.command,
		NextActions: []string{
			"Check Unity Console logs if the command may have changed project or scene state.",
			"Retry after Unity finishes compiling, reloading scripts, or restarting the bridge.",
		},
		Details: map[string]any{
			"cause": err.Error(),
		},
	}
}

func unityServerNotRespondingAfterDispatchError(err unityServerNotRespondingError, context errorContext) cliError {
	return cliError{
		ErrorCode:   errorCodeUnityNotReachable,
		Phase:       errorPhaseResponseWaiting,
		Message:     "Unity is running for this project, but the Unity CLI Loop server did not acknowledge the dispatched request.",
		Retryable:   true,
		SafeToRetry: false,
		ProjectRoot: firstNonEmpty(context.projectRoot, err.projectRoot),
		Command:     context.command,
		NextActions: []string{
			"Check Unity Console logs and project state because Unity may have received the request.",
			"Retry only after confirming the previous command did not run or has finished.",
			"Run `uloop focus-window` if Unity appears stalled in the background.",
		},
		Details: map[string]any{
			"endpoint": err.endpoint,
			"cause":    err.causeText(),
		},
	}
}

func unknownCommandError(command string, cache toolsCache, context errorContext) cliError {
	return cliError{
		ErrorCode:   errorCodeUnknownCommand,
		Phase:       errorPhaseDispatch,
		Message:     "Unknown command: " + command,
		Retryable:   false,
		SafeToRetry: false,
		ProjectRoot: context.projectRoot,
		Command:     command,
		NextActions: []string{
			"Run `uloop list` to inspect available commands.",
			"Run `uloop sync` if the local tool cache may be stale.",
		},
		Details: map[string]any{
			"availableCommands": availableCommandNames(cache),
		},
	}
}

func compileWaitTimeoutError(projectRoot string) cliError {
	return cliError{
		ErrorCode:   errorCodeCompileWaitTimeout,
		Phase:       errorPhaseCompileWaiting,
		Message:     "Compile status wait timed out after 180000ms.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		Command:     compileCommandName,
		NextActions: []string{
			"Retry `uloop compile` after Unity becomes responsive.",
		},
	}
}

func internalCLIError(message string, context errorContext) cliError {
	return cliError{
		ErrorCode:   errorCodeInternalError,
		Phase:       errorPhaseExecution,
		Message:     message,
		Retryable:   false,
		SafeToRetry: false,
		ProjectRoot: context.projectRoot,
		Command:     context.command,
		NextActions: []string{
			"Read the message and fix the local environment or command input before retrying.",
		},
	}
}

func availableCommandNames(cache toolsCache) []string {
	seen := map[string]bool{}
	names := []string{}
	for _, name := range nativeCommandNamesForCompletion() {
		seen[name] = true
		names = append(names, name)
	}
	for _, tool := range cache.Tools {
		if seen[tool.Name] {
			continue
		}
		seen[tool.Name] = true
		names = append(names, tool.Name)
	}
	return names
}

func isSafeRetryCommand(command string) bool {
	switch command {
	case "list", "sync", "get-version", "get-logs", "get-tool-details":
		return true
	default:
		return false
	}
}
