package dispatcher

import "github.com/hatayama/unity-cli-loop/common/clicore"

// unsupportedPlatformError builds a clicore.CLIError for bootstrap commands (install, update,
// uninstall) that fail because the current OS is not supported, matching on the
// well-known platform-guard messages returned by the internal install/update/uninstall
// packages.
func unsupportedPlatformError(message string, context clicore.ErrorContext) (clicore.CLIError, bool) {
	switch message {
	case updateUnsupportedOSMessage:
		return invalidArgumentExecutionError(
			message,
			context,
			[]string{
				"Run `uloop update` on macOS or Windows.",
				"Install the latest uloop launcher manually on this platform.",
			}), true
	case installUnsupportedOSMessage:
		return invalidArgumentExecutionError(
			message,
			context,
			[]string{
				"Run `uloop install` on macOS or Windows.",
				"Use the platform-specific installer for this system.",
			}), true
	case uninstallUnsupportedOSMessage:
		return invalidArgumentExecutionError(
			message,
			context,
			[]string{
				"Run `uloop uninstall` on macOS or Windows.",
				"Remove the uloop launcher binary manually on this platform.",
			}), true
	default:
		return clicore.CLIError{}, false
	}
}

func invalidArgumentExecutionError(message string, context clicore.ErrorContext, nextActions []string) clicore.CLIError {
	return clicore.CLIError{
		ErrorCode:   clicore.ErrorCodeInvalidArgument,
		Phase:       clicore.ErrorPhaseExecution,
		Message:     message,
		Retryable:   false,
		SafeToRetry: false,
		Command:     context.Command,
		NextActions: nextActions,
	}
}

// unsupportedPlatformCommandError wraps a raw bootstrap-command error that reports an
// unsupported OS, so it self-classifies through classifiableCLIError instead of relying
// on the shared CORE classifier to pattern-match error message text.
type unsupportedPlatformCommandError struct {
	message     string
	nextActions []string
}

func (err unsupportedPlatformCommandError) Error() string {
	return err.message
}

func (err unsupportedPlatformCommandError) ToCLIError(context clicore.ErrorContext) clicore.CLIError {
	return invalidArgumentExecutionError(err.message, context, err.nextActions)
}

// wrapUnsupportedPlatformError wraps install/update/uninstall errors that report an
// unsupported OS so they classify as invalid arguments. Errors that do not match a
// known unsupported-OS message are returned unchanged.
func wrapUnsupportedPlatformError(err error) error {
	if err == nil {
		return err
	}
	cliErr, ok := unsupportedPlatformError(err.Error(), clicore.ErrorContext{})
	if !ok {
		return err
	}
	return unsupportedPlatformCommandError{
		message:     err.Error(),
		nextActions: cliErr.NextActions,
	}
}
