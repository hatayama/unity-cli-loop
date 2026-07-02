package cli

// unsupportedPlatformError builds a cliError for bootstrap commands (install, update,
// uninstall) that fail because the current OS is not supported, matching on the
// well-known platform-guard messages returned by the internal install/update/uninstall
// packages.
func unsupportedPlatformError(message string, context errorContext) (cliError, bool) {
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
				"Run `uloop install` on Windows.",
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
		return cliError{}, false
	}
}

func invalidArgumentExecutionError(message string, context errorContext, nextActions []string) cliError {
	return cliError{
		ErrorCode:   errorCodeInvalidArgument,
		Phase:       errorPhaseExecution,
		Message:     message,
		Retryable:   false,
		SafeToRetry: false,
		Command:     context.command,
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

func (err unsupportedPlatformCommandError) toCLIError(context errorContext) cliError {
	return invalidArgumentExecutionError(err.message, context, err.nextActions)
}

// wrapUnsupportedPlatformError wraps install/update/uninstall errors that report an
// unsupported OS so they classify as invalid arguments. Errors that do not match a
// known unsupported-OS message are returned unchanged.
func wrapUnsupportedPlatformError(err error) error {
	if err == nil {
		return err
	}
	cliErr, ok := unsupportedPlatformError(err.Error(), errorContext{})
	if !ok {
		return err
	}
	return unsupportedPlatformCommandError{
		message:     err.Error(),
		nextActions: cliErr.NextActions,
	}
}
