package dispatcher

import (
	"context"
	"errors"
	"fmt"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

type launchStartupTimeoutError struct {
	projectRoot string
	cause       error
	// safeModePromptIgnored is whether launch pressed Ignore on Unity's Safe Mode prompt before the
	// wait ran out of time.
	safeModePromptIgnored bool
}

// launchReadiness is what a launch wait that ended saw on the way.
type launchReadiness struct {
	// safeModePromptIgnored is whether launch pressed Ignore on Unity's Safe Mode prompt.
	safeModePromptIgnored bool
	// olderPackageProtocol is whether the package refused this dispatcher's newer IPC protocol, so
	// launch confirmed only that the Editor answers.
	olderPackageProtocol bool
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

// waitForLaunchReadinessWithDeps waits for the Editor launch opened or found, pressing Ignore once on
// Unity's Safe Mode prompt; restart is whether launch ran with --restart.
// Why only a dialog stop and a startup timeout report the pressed prompt: every other end is an error
// clicore builds, and reporting it there would change an error type the project runner shares.
func waitForLaunchReadinessWithDeps(
	ctx context.Context, projectRoot string, restart bool, deps launchDeps,
) (launchReadiness, error) {
	answerer := newSafeModePromptAnswerer(projectRoot, deps)
	err := deps.waitForToolReadiness(ctx, projectRoot, launchReadinessTimeout, answerer.findDialogs)
	readiness := launchReadiness{safeModePromptIgnored: answerer.ignored}
	if err == nil {
		return readiness, nil
	}
	// Why only while ctx is live: a refusal that arrives after cancellation must not turn a canceled
	// launch into a success.
	if ctx.Err() == nil && isNewerDispatcherProtocolMismatch(err) {
		readiness.olderPackageProtocol = true
		return readiness, nil
	}
	var blocked clicore.ToolReadinessBlockedByDialogError
	if errors.As(err, &blocked) {
		return readiness, asLaunchDialogError(err, projectRoot, deps.canPressDialogs, restart, answerer.ignored)
	}
	if ctx.Err() != nil || clicore.IsReadinessCLIUpdateRequiredError(err) {
		return readiness, err
	}
	var notRespondingErr clierrors.UnityServerNotRespondingError
	if !errors.As(err, &notRespondingErr) {
		return readiness, err
	}
	return readiness, launchStartupTimeoutError{
		projectRoot:           projectRoot,
		cause:                 err,
		safeModePromptIgnored: answerer.ignored,
	}
}

func (err launchStartupTimeoutError) ToCLIError(context clierrors.ErrorContext) clierrors.CLIError {
	projectRoot := clicore.FirstNonEmpty(context.ProjectRoot, err.projectRoot)
	details := map[string]any{
		"TimeoutSeconds": int(launchReadinessTimeout.Seconds()),
	}
	if err.cause != nil {
		details["Cause"] = err.cause.Error()
	}
	// Why only when true: a launch that pressed nothing keeps the output it always had.
	if err.safeModePromptIgnored {
		details["SafeModePromptIgnored"] = true
	}
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeUnityStartupTimeout,
		Phase:       clierrors.ErrorPhaseConnection,
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
