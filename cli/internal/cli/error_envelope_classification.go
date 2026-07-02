package cli

import (
	"encoding/json"
	"errors"
	"strings"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

func classifyError(err error, context errorContext) cliError {
	if err == nil {
		return internalCLIError("unknown CLI error", context)
	}

	if classifiedError, ok := classifyTypedError(err, context); ok {
		return classifiedError
	}

	return classifyMessageError(err.Error(), context)
}

// classifiableCLIError lets an error type self-classify into a cliError envelope,
// so classifyTypedError does not need a dedicated branch per error type.
type classifiableCLIError interface {
	toCLIError(context errorContext) cliError
}

func classifyTypedError(err error, context errorContext) (cliError, bool) {
	var classifiable classifiableCLIError
	if errors.As(err, &classifiable) {
		return classifiable.toCLIError(context), true
	}

	if classifiedError, ok := classifyUnityConnectionError(err, context); ok {
		return classifiedError, true
	}

	var rpcErr *unityipc.RPCError
	if errors.As(err, &rpcErr) {
		return classifyRPCError(rpcErr, context), true
	}

	return cliError{}, false
}

func classifyUnityConnectionError(err error, context errorContext) (cliError, bool) {
	var notRespondingErr unityServerNotRespondingError
	if errors.As(err, &notRespondingErr) {
		return unityServerNotRespondingCLIError(notRespondingErr, context), true
	}

	var editorUnresponsiveErr *unityipc.EditorUnresponsiveError
	if errors.As(err, &editorUnresponsiveErr) {
		return unityEditorUnresponsiveError(editorUnresponsiveErr, context), true
	}

	var connectionErr *unityipc.ConnectionAttemptError
	if errors.As(err, &connectionErr) {
		return connectionAttemptCLIError(connectionErr, context), true
	}

	return cliError{}, false
}

func unityServerNotRespondingCLIError(err unityServerNotRespondingError, context errorContext) cliError {
	return cliError{
		ErrorCode:   errorCodeUnityNotReachable,
		Phase:       errorPhaseConnection,
		Message:     "Unity is running for this project, but the Unity CLI Loop server is not responding.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: firstNonEmpty(context.projectRoot, err.projectRoot),
		Command:     context.command,
		NextActions: []string{
			"Wait and retry; Unity may be starting, importing assets, compiling, or reloading scripts.",
			"Run `uloop focus-window` if Unity appears stalled in the background.",
			"Confirm that the command targets the intended Unity project and the Editor package is installed.",
		},
		Details: map[string]any{
			"Endpoint": err.endpoint,
			"Cause":    err.causeText(),
		},
	}
}

func connectionAttemptCLIError(err *unityipc.ConnectionAttemptError, context errorContext) cliError {
	return cliError{
		ErrorCode:   errorCodeUnityNotReachable,
		Phase:       errorPhaseConnection,
		Message:     "The Unity CLI Loop server is not reachable for this project.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: firstNonEmpty(context.projectRoot, err.ProjectRoot),
		Command:     context.command,
		NextActions: []string{
			"If Unity is closed, run `uloop launch`.",
			"If Unity is starting, compiling, or reloading scripts, wait and retry.",
			"Confirm that the command targets the intended Unity project.",
		},
		Details: map[string]any{
			"Endpoint": err.Endpoint,
			"Cause":    connectionAttemptCause(err),
		},
	}
}

func classifyRPCError(rpcErr *unityipc.RPCError, context errorContext) cliError {
	details, decodedData := rpcErrorDetails(rpcErr)
	switch rpcDataType(decodedData) {
	case "cli_update_required":
		return cliUpdateRequiredError(rpcErr, details, decodedData, context)
	case "server_busy":
		return unityServerBusyError(rpcErr, details, decodedData, context)
	default:
		return genericRPCError(rpcErr, details, context)
	}
}

func rpcErrorDetails(rpcErr *unityipc.RPCError) (map[string]any, map[string]any) {
	details := map[string]any{
		"Code":    rpcErr.Code,
		"Message": rpcErr.Message,
	}
	if len(rpcErr.Data) == 0 {
		return details, nil
	}

	var data any
	if json.Unmarshal(rpcErr.Data, &data) != nil {
		details["Data"] = string(rpcErr.Data)
		return details, nil
	}

	details["Data"] = data
	decodedData, _ := data.(map[string]any)
	return details, decodedData
}

func genericRPCError(rpcErr *unityipc.RPCError, details map[string]any, context errorContext) cliError {
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

func classifyMessageError(message string, context errorContext) cliError {
	if isProjectNotFoundMessage(message) {
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

	return internalCLIError(message, context)
}

func isProjectNotFoundMessage(message string) bool {
	return message == "unity project not found. Use --project-path option to specify the target" ||
		strings.HasPrefix(message, "not a Unity project:") ||
		strings.HasPrefix(message, "--project-path does not point to a Unity project:")
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
