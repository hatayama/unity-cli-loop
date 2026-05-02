package cli

import (
	"encoding/json"
	"errors"
	"io"
	"strings"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/unity"
)

const (
	errorCodeInvalidArgument                = "INVALID_ARGUMENT"
	errorCodeUnknownCommand                 = "UNKNOWN_COMMAND"
	errorCodeProjectNotFound                = "PROJECT_NOT_FOUND"
	errorCodeProjectLocalCLIMissing         = "PROJECT_LOCAL_CLI_MISSING"
	errorCodeUnityNotReachable              = "UNITY_NOT_REACHABLE"
	errorCodeUnityDisconnectedAfterDispatch = "UNITY_DISCONNECTED_AFTER_DISPATCH"
	errorCodeUnityRPCError                  = "UNITY_RPC_ERROR"
	errorCodeCompileWaitTimeout             = "COMPILE_WAIT_TIMEOUT"
	errorCodeInternalError                  = "INTERNAL_ERROR"

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

func writeToolFailure(writer io.Writer, err error, outcome unity.SendOutcome, context errorContext) {
	if err != nil && outcome.RequestDispatched && isTransportDisconnectError(err) {
		writeErrorEnvelope(writer, disconnectedAfterDispatchError(err, context))
		return
	}
	writeClassifiedError(writer, err, context)
}

func classifyError(err error, context errorContext) cliError {
	if err == nil {
		return internalCLIError("unknown CLI error", context)
	}

	var argumentErr *argumentError
	if errors.As(err, &argumentErr) {
		return argumentErr.toCLIError(context)
	}

	var connectionErr *unity.ConnectionAttemptError
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
				"cause":    connectionErr.Unwrap().Error(),
			},
		}
	}

	var rpcErr *unity.RPCError
	if errors.As(err, &rpcErr) {
		details := map[string]any{
			"code":    rpcErr.Code,
			"message": rpcErr.Message,
		}
		if len(rpcErr.Data) > 0 {
			var data any
			if json.Unmarshal(rpcErr.Data, &data) == nil {
				details["data"] = data
			} else {
				details["data"] = string(rpcErr.Data)
			}
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
				"Pass `--project-path <path>` before the command.",
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

	return internalCLIError(message, context)
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

func projectLocalCLIMissingError(localPath string, projectRoot string, command string) cliError {
	return cliError{
		ErrorCode:   errorCodeProjectLocalCLIMissing,
		Phase:       errorPhaseDispatch,
		Message:     "Project-local uloop-core CLI was not found.",
		Retryable:   false,
		SafeToRetry: false,
		ProjectRoot: projectRoot,
		Command:     command,
		NextActions: []string{
			"Open the Unity project so the package can install the project-local CLI.",
			"Reinstall or update Unity CLI Loop in this project if the file is still missing.",
		},
		Details: map[string]any{
			"path": localPath,
		},
	}
}

func compileWaitTimeoutError(projectRoot string) cliError {
	return cliError{
		ErrorCode:   errorCodeCompileWaitTimeout,
		Phase:       errorPhaseCompileWaiting,
		Message:     "Compile wait timed out after 90000ms.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		Command:     compileCommandName,
		NextActions: []string{
			"Run `uloop fix` to remove stale lock files.",
			"Retry `uloop compile --wait-for-domain-reload true` after Unity becomes responsive.",
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
	for _, name := range []string{"list", "sync", "focus-window", "fix"} {
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

func firstNonEmpty(values ...string) string {
	for _, value := range values {
		if value != "" {
			return value
		}
	}
	return ""
}
