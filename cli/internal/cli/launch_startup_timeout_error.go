package cli

import (
	"context"
	"errors"
	"fmt"

	"github.com/hatayama/unity-cli-loop/cli/internal/clicore"
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
	if ctx.Err() != nil || clicore.IsReadinessCLIUpdateRequiredError(err) {
		return err
	}
	var notRespondingErr clicore.UnityServerNotRespondingError
	if !errors.As(err, &notRespondingErr) {
		return err
	}
	return launchStartupTimeoutError{
		projectRoot: projectRoot,
		cause:       err,
	}
}

func (err launchStartupTimeoutError) ToCLIError(context clicore.ErrorContext) clicore.CLIError {
	projectRoot := clicore.FirstNonEmpty(context.ProjectRoot, err.projectRoot)
	details := map[string]any{
		"TimeoutSeconds": int(launchReadinessTimeout.Seconds()),
	}
	if err.cause != nil {
		details["Cause"] = err.cause.Error()
	}
	return clicore.CLIError{
		ErrorCode:   clicore.ErrorCodeUnityStartupTimeout,
		Phase:       clicore.ErrorPhaseConnection,
		Message:     "Unity is running, but the Editor did not finish startup before the launch timeout.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		Command:     context.Command,
		NextActions: []string{
			"Wait for Unity to finish importing assets, compiling scripts, or reloading the domain.",
			"After the Editor becomes responsive, continue with the uloop command you wanted to run.",
			"If Unity appears stuck, focus the Editor and check the Console or Editor log.",
		},
		Details: details,
	}
}
