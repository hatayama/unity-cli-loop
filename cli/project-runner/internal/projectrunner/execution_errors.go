package projectrunner

import (
	"fmt"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

func compileWaitTimeoutError(
	projectRoot string,
	timeout time.Duration,
	lastStatus *compileStatusResponse,
	waited time.Duration,
	retentionRemaining time.Duration,
) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode: clierrors.ErrorCodeCompileWaitTimeout,
		Phase:     clierrors.ErrorPhaseCompileWaiting,
		Message: fmt.Sprintf(
			"Compile status wait timed out after %dms. This does not mean the Unity Editor is frozen; the compile may simply still be running.",
			timeout.Milliseconds()),
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		Command:     clicore.CompileCommandName,
		// Why: agents historically treated this timeout as a frozen Editor and ran
		// launch -r. Recovery is reattach via a later uloop compile while Unity still
		// holds the result (CompileResultLifetime / compilePendingRecordLifetime).
		NextActions: compileWaitTimeoutNextActions(retentionRemaining),
		Details:     compileWaitTimeoutDetails(lastStatus, waited),
	}
}

func compileWaitTimeoutDetails(lastStatus *compileStatusResponse, waited time.Duration) map[string]any {
	details := map[string]any{
		"WaitedMs": waited.Milliseconds(),
	}
	if lastStatus == nil {
		return details
	}
	details["IsCompiling"] = lastStatus.IsCompiling
	details["IsUpdating"] = lastStatus.IsUpdating
	details["IsDomainReloadInProgress"] = lastStatus.IsDomainReloadInProgress
	return details
}

func compileWaitTimeoutNextActions(retentionRemaining time.Duration) []string {
	reattachAction := "Unity keeps compiling and refuses other commands with UNITY_SERVER_BUSY until it finishes. Re-run `uloop compile`: it will reattach to the in-flight compile and wait for its result instead of starting a new one."
	// Why the caller passes remaining: attach re-timeouts keep the original TimedOutAtUtc,
	// so remaining retention is wall-clock from that first timeout, not (TTL - this wait).
	remaining := retentionRemaining
	if remaining < 0 {
		remaining = 0
	}
	remainingMinutes := int(remaining / time.Minute)
	if remainingMinutes > 0 {
		reattachAction = fmt.Sprintf(
			"%s The result stays retrievable for roughly %d more minutes.",
			reattachAction,
			remainingMinutes,
		)
	}

	return []string{
		"Run a light command such as `uloop get-logs --max-count 1` to check whether Unity is responsive before treating this as a freeze.",
		reattachAction,
		clierrors.ApiUpdateConsentModalNextAction,
		"Only if Unity does not respond to any command, restart it with `uloop launch -r`.",
		"If repeated waits keep showing is_compiling=true with no progress, the Editor's compile pipeline may be stalled (for example by a modal dialog); restart Unity with 'uloop launch -r' and rerun 'uloop compile'.",
	}
}
