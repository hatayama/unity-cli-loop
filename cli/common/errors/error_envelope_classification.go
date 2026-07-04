package clierrors

import (
	"encoding/json"
	"errors"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

func ClassifyError(err error, context ErrorContext) CLIError {
	if err == nil {
		return InternalCLIError("unknown CLI error", context)
	}

	if classifiedError, ok := classifyTypedError(err, context); ok {
		return classifiedError
	}

	return InternalCLIError(err.Error(), context)
}

// classifiableCLIError lets an error type self-classify into a CLIError envelope,
// so classifyTypedError does not need a dedicated branch per error type.
type classifiableCLIError interface {
	ToCLIError(context ErrorContext) CLIError
}

func classifyTypedError(err error, context ErrorContext) (CLIError, bool) {
	var classifiable classifiableCLIError
	if errors.As(err, &classifiable) {
		return classifiable.ToCLIError(context), true
	}

	if classifiedError, ok := classifyUnityConnectionError(err, context); ok {
		return classifiedError, true
	}

	var rpcErr *unityipc.RPCError
	if errors.As(err, &rpcErr) {
		return classifyRPCError(rpcErr, context), true
	}

	return CLIError{}, false
}

func classifyUnityConnectionError(err error, context ErrorContext) (CLIError, bool) {
	var notRespondingErr UnityServerNotRespondingError
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

	return CLIError{}, false
}

func unityServerNotRespondingCLIError(err UnityServerNotRespondingError, context ErrorContext) CLIError {
	return CLIError{
		ErrorCode:   ErrorCodeUnityNotReachable,
		Phase:       ErrorPhaseConnection,
		Message:     "Unity is running for this project, but the Unity CLI Loop server is not responding.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: firstNonEmpty(context.ProjectRoot, err.ProjectRoot),
		Command:     context.Command,
		NextActions: []string{
			"Wait and retry; Unity may be starting, importing assets, compiling, or reloading scripts.",
			"Run `uloop focus-window` if Unity appears stalled in the background.",
			"Confirm that the command targets the intended Unity project and the Editor package is installed.",
		},
		Details: map[string]any{
			"Endpoint": err.Endpoint,
			"Cause":    err.causeText(),
		},
	}
}

func connectionAttemptCLIError(err *unityipc.ConnectionAttemptError, context ErrorContext) CLIError {
	return CLIError{
		ErrorCode:   ErrorCodeUnityNotReachable,
		Phase:       ErrorPhaseConnection,
		Message:     "The Unity CLI Loop server is not reachable for this project.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: firstNonEmpty(context.ProjectRoot, err.ProjectRoot),
		Command:     context.Command,
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

func classifyRPCError(rpcErr *unityipc.RPCError, context ErrorContext) CLIError {
	details, decodedData := rpcErrorDetails(rpcErr)
	switch RPCDataType(decodedData) {
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

func genericRPCError(rpcErr *unityipc.RPCError, details map[string]any, context ErrorContext) CLIError {
	return CLIError{
		ErrorCode:   errorCodeUnityRPCError,
		Phase:       errorPhaseUnityRPC,
		Message:     rpcErr.Message,
		Retryable:   false,
		SafeToRetry: false,
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: []string{
			"Read the Unity error details and fix the request or project state before retrying.",
		},
		Details: details,
	}
}

func RPCDataType(data map[string]any) string {
	if data == nil {
		return ""
	}
	value, ok := data["type"].(string)
	if !ok {
		return ""
	}
	return value
}
