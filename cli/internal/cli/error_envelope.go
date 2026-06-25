package cli

import (
	"encoding/json"
	"errors"
	"io"
	"net"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

const (
	errorCodeInvalidArgument                 = "INVALID_ARGUMENT"
	errorCodeUnknownCommand                  = "UNKNOWN_COMMAND"
	errorCodeProjectNotFound                 = "PROJECT_NOT_FOUND"
	errorCodeUnityNotReachable               = "UNITY_NOT_REACHABLE"
	errorCodeUnityStartupTimeout             = "UNITY_STARTUP_TIMEOUT"
	errorCodeUnityProcessExitTimeout         = "UNITY_PROCESS_EXIT_TIMEOUT"
	errorCodeUnityDisconnectedAfterDispatch  = "UNITY_DISCONNECTED_AFTER_DISPATCH"
	errorCodeUnityDisconnectedAfterAccept    = "UNITY_DISCONNECTED_AFTER_ACCEPT"
	errorCodeUnityResponseTimeoutAfterAccept = "UNITY_RESPONSE_TIMEOUT_AFTER_ACCEPT"
	errorCodeUnityEditorUnresponsive         = "UNITY_EDITOR_UNRESPONSIVE"
	errorCodeUnityRPCError                   = "UNITY_RPC_ERROR"
	errorCodeUnityServerBusy                 = "UNITY_SERVER_BUSY"
	errorCodeCLIUpdateRequired               = "CLI_UPDATE_REQUIRED"
	errorCodeToolDisabled                    = "TOOL_DISABLED"
	errorCodeCompileWaitTimeout              = "COMPILE_WAIT_TIMEOUT"
	errorCodeControlPlayModeWaitTimeout      = "CONTROL_PLAY_MODE_WAIT_TIMEOUT"
	errorCodeControlPlayModeCompileErrors    = "CONTROL_PLAY_MODE_COMPILE_ERRORS"
	errorCodePausePointNotEnabled            = "PAUSE_POINT_NOT_ENABLED"
	errorCodePausePointWaitTimeout           = "PAUSE_POINT_WAIT_TIMEOUT"
	errorCodePausePointExpired               = "PAUSE_POINT_EXPIRED"
	errorCodePausePointCleared               = "PAUSE_POINT_CLEARED"
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

func (err cliError) Error() string {
	return err.Message
}

type cliErrorEnvelope struct {
	Success bool     `json:"Success"`
	Error   cliError `json:"Error"`
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
			"Cause": err.Error(),
		},
	}
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
	actions := []string{}
	if updateCommand != "" {
		actions = append(actions, "Run `"+updateCommand+"`.")
	} else if cliProtocolMismatchIsNewer(data) {
		actions = append(actions, "Update the Unity package to a version that supports this CLI protocol, or install the CLI from the same release as the package.")
	} else {
		actions = append(actions, "Install matching uloop CLI and Unity package versions.")
	}
	actions = append(actions, "Retry the original command after the versions match.")
	return actions
}

func cliProtocolMismatchIsNewer(data map[string]any) bool {
	currentProtocolVersion, currentOk := protocolVersionFromRPCData(data, "currentProtocolVersion")
	requiredProtocolVersion, requiredOk := protocolVersionFromRPCData(data, "requiredProtocolVersion")
	return currentOk && requiredOk && currentProtocolVersion > requiredProtocolVersion
}

func protocolVersionFromRPCData(data map[string]any, key string) (float64, bool) {
	value, ok := data[key].(float64)
	return value, ok
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
			"Cause": err.Error(),
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
			"Cause": err.Error(),
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
			"Endpoint": err.endpoint,
			"Cause":    err.causeText(),
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
			"AvailableCommands": availableCommandNames(cache),
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
