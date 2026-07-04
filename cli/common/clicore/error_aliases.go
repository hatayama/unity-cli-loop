package clicore

import (
	"io"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	ErrorCodeInvalidArgument              = clierrors.ErrorCodeInvalidArgument
	ErrorCodeUnityNotReachable            = clierrors.ErrorCodeUnityNotReachable
	ErrorCodeUnityStartupTimeout          = clierrors.ErrorCodeUnityStartupTimeout
	ErrorCodeUnityProcessExitTimeout      = clierrors.ErrorCodeUnityProcessExitTimeout
	ErrorCodeCLIUpdateRequired            = clierrors.ErrorCodeCLIUpdateRequired
	ErrorCodeToolDisabled                 = clierrors.ErrorCodeToolDisabled
	ErrorCodeCompileWaitTimeout           = clierrors.ErrorCodeCompileWaitTimeout
	ErrorCodeControlPlayModeWaitTimeout   = clierrors.ErrorCodeControlPlayModeWaitTimeout
	ErrorCodeControlPlayModeCompileErrors = clierrors.ErrorCodeControlPlayModeCompileErrors
	ErrorCodePausePointNotEnabled         = clierrors.ErrorCodePausePointNotEnabled
	ErrorCodePausePointWaitTimeout        = clierrors.ErrorCodePausePointWaitTimeout
	ErrorCodePausePointExpired            = clierrors.ErrorCodePausePointExpired
	ErrorCodePausePointCleared            = clierrors.ErrorCodePausePointCleared
	ErrorCodeInternalError                = clierrors.ErrorCodeInternalError

	ErrorPhaseArgumentParsing = clierrors.ErrorPhaseArgumentParsing
	ErrorPhaseProjectResolve  = clierrors.ErrorPhaseProjectResolve
	ErrorPhaseDispatch        = clierrors.ErrorPhaseDispatch
	ErrorPhaseConnection      = clierrors.ErrorPhaseConnection
	ErrorPhaseResponseWaiting = clierrors.ErrorPhaseResponseWaiting
	ErrorPhaseCompileWaiting  = clierrors.ErrorPhaseCompileWaiting
	ErrorPhaseExecution       = clierrors.ErrorPhaseExecution
)

type (
	CLIError                      = clierrors.CLIError
	CLIErrorEnvelope              = clierrors.CLIErrorEnvelope
	ErrorContext                  = clierrors.ErrorContext
	ArgumentError                 = clierrors.ArgumentError
	UnityServerNotRespondingError = clierrors.UnityServerNotRespondingError
)

func WriteErrorEnvelope(writer io.Writer, err CLIError) {
	clierrors.WriteErrorEnvelope(writer, err)
}

func WriteClassifiedError(writer io.Writer, err error, context ErrorContext) {
	clierrors.WriteClassifiedError(writer, err, context)
}

func WriteToolFailure(writer io.Writer, err error, outcome unityipc.UnitySendOutcome, context ErrorContext) {
	clierrors.WriteToolFailure(writer, err, outcome, context)
}

func ClassifyError(err error, context ErrorContext) CLIError {
	return clierrors.ClassifyError(err, context)
}

func MissingValueArgumentError(option string) *ArgumentError {
	return clierrors.MissingValueArgumentError(option)
}

func InvalidValueArgumentError(option string, received string, expectedType string) *ArgumentError {
	return clierrors.InvalidValueArgumentError(option, received, expectedType)
}

func InternalCLIError(message string, context ErrorContext) CLIError {
	return clierrors.InternalCLIError(message, context)
}

func IsTransportDisconnectError(err error) bool {
	return clierrors.IsTransportDisconnectError(err)
}

func IsFinalResponseTimeoutError(err error) bool {
	return clierrors.IsFinalResponseTimeoutError(err)
}

func RPCDataType(data map[string]any) string {
	return clierrors.RPCDataType(data)
}

func UnknownCommandError(command string, cache ToolsCache, context ErrorContext) CLIError {
	return clierrors.UnknownCommandError(command, availableCommandNames(cache), context)
}

func availableCommandNames(cache ToolsCache) []string {
	seen := map[string]bool{}
	names := []string{}
	for _, name := range NativeCommandNamesForCompletion() {
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
