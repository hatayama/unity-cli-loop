package cli

import (
	"context"
	"errors"
	"fmt"
)

type launchStartupTimeoutError struct {
	projectRoot string
	cause       error
}

func (err launchStartupTimeoutError) Error() string {
	if err.cause == nil {
		return "Unity startup did not finish before the launch timeout"
	}
	return fmt.Sprintf("Unity startup did not finish before the launch timeout: %s", err.cause.Error())
}

func (err launchStartupTimeoutError) Unwrap() error {
	return err.cause
}

func waitForLaunchReadiness(ctx context.Context, projectRoot string) error {
	err := waitForToolReadinessForLaunch(ctx, projectRoot, launchReadinessTimeout)
	if err == nil {
		return nil
	}
	if ctx.Err() != nil || isReadinessCLIUpdateRequiredError(err) {
		return err
	}
	var notRespondingErr unityServerNotRespondingError
	if !errors.As(err, &notRespondingErr) {
		return err
	}
	return launchStartupTimeoutError{
		projectRoot: projectRoot,
		cause:       err,
	}
}

func unityStartupTimeoutCLIError(err launchStartupTimeoutError, context errorContext) cliError {
	projectRoot := firstNonEmpty(context.projectRoot, err.projectRoot)
	details := map[string]any{
		"timeoutSeconds": int(launchReadinessTimeout.Seconds()),
	}
	if err.cause != nil {
		details["cause"] = err.cause.Error()
	}
	return cliError{
		ErrorCode:   errorCodeUnityStartupTimeout,
		Phase:       errorPhaseConnection,
		Message:     "Unity is running, but the Editor did not finish startup before the launch timeout.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		Command:     context.command,
		NextActions: []string{
			"Wait for Unity to finish importing assets, compiling scripts, or reloading the domain.",
			"After the Editor becomes responsive, continue with the uloop command you wanted to run.",
			"If Unity appears stuck, focus the Editor and check the Console or Editor log.",
		},
		Details: details,
	}
}
