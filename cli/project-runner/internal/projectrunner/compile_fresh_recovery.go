package projectrunner

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/vibelog"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	// The first send and two resends.
	freshCompileMaxAttempts = 3
	// Why: with less wait time left, a resent compile would start in Unity just as the command
	// times out, which adds a compile and turns a rejection into a timeout.
	compileResendMinimumBudget = 10 * time.Second
)

var errCompileRequestMissing = errors.New("the compile request has no record in Unity")

// freshCompileAttemptOutcome says how one fresh compile attempt ended, so the caller can decide
// whether to send the compile again.
type freshCompileAttemptOutcome int

const (
	// The returned result is the command's result.
	freshCompileAttemptFinal freshCompileAttemptOutcome = iota
	// Unity lost the request, so sending it again is safe.
	freshCompileAttemptRequestMissing
	// Unity rejected the compile because it was compiling or updating, and the wait has seen it
	// Ready since.
	freshCompileAttemptEditorBusy
)

// freshCompileAttemptOptions configures one fresh compile attempt.
type freshCompileAttemptOptions struct {
	// Zero never reports RequestMissing or EditorBusy, which keeps the behavior of a compile that
	// is never sent again. Otherwise they are reported only while at least
	// compileResendMinimumBudget is left before this moment.
	resendBefore time.Time
	// Zero waits as long as --timeout-seconds says, for the first attempt and for entries that
	// never send again. A positive value is this attempt's wait limit.
	timeoutOverride time.Duration
}

// runFreshCompileRecoveringWithDeps sends a fresh compile and sends it again when Unity lost the
// request or rejected it because it was still compiling or updating, at most
// freshCompileMaxAttempts sends in all within the one wait the caller asked for.
func runFreshCompileRecoveringWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stderr io.Writer,
	compileWait compileWaitDeps,
) compileExecutionResult {
	waitTimeout, err := compileWaitTimeoutFromParams(params)
	if err != nil {
		// The entry that never resends reports an invalid --timeout-seconds the way it always has.
		return runFreshCompileWithDomainReloadWaitResultWithDeps(ctx, connection, params, stderr, compileWait)
	}
	resendBefore := time.Now().Add(waitTimeout)
	options := freshCompileAttemptOptions{resendBefore: resendBefore}
	for attempt := 1; ; attempt++ {
		if attempt >= freshCompileMaxAttempts {
			// Why: the last attempt behaves exactly like a compile that is never resent, so reaching
			// the limit never makes a new kind of failure.
			options.resendBefore = time.Time{}
		}
		result, outcome := runFreshCompileAttempt(ctx, connection, params, stderr, compileWait, options)
		if outcome == freshCompileAttemptFinal {
			return result
		}
		logCompileRequestResend(connection, params, outcome, attempt)
		// Why a new request ID: Unity keeps the rejection it stored under the old one, and a status
		// query with that ID would return it at once.
		delete(params, compileRequestIDParam)
		// Why positive: an attempt reports a resend only while canResendCompile holds.
		options.timeoutOverride = time.Until(resendBefore)
	}
}

// compileRequestMissingTracker recognizes a request that Unity lost: Ready answers without a result
// that keep coming after Unity's server was recreated by a domain reload or a restart.
type compileRequestMissingTracker struct {
	serverRestartSeen bool
	missingStreak     int
}

// observe records one status query and reports whether the request is now known to be lost.
// Why a restart must be seen first: a request that reached Unity's main thread has a result by the
// first Ready answer after a domain reload, because Unity builds one from the pending request it
// registered there. So Ready answers without a result after the server was recreated come only
// from a request that never got there. Without a restart, a live request answers the same way for
// the seconds before its compile starts, and a resend then would be rejected by the single-flight
// slot the first request still holds.
func (tracker *compileRequestMissingTracker) observe(status compileStatusResponse, err error) bool {
	if err != nil {
		if isServerGoneError(err) {
			tracker.serverRestartSeen = true
		}
		tracker.missingStreak = 0
		return false
	}
	if status.IsDomainReloadInProgress {
		tracker.serverRestartSeen = true
	}
	if !status.Ready || status.HasResult {
		tracker.missingStreak = 0
		return false
	}
	tracker.missingStreak++
	return tracker.serverRestartSeen && tracker.missingStreak >= compileAttachMissingResultStreak
}

// isServerGoneError reports whether a failed status query shows that Unity's server went away: the
// connection dropped mid-query, or nobody was listening.
func isServerGoneError(err error) bool {
	if clierrors.IsTransportDisconnectError(err) {
		return true
	}
	var connectionErr *unityipc.ConnectionAttemptError
	if !errors.As(err, &connectionErr) {
		return false
	}
	// Why not a connect timeout or a denied connect: a Windows named pipe times out while all of its
	// instances are busy, and a sandbox can deny the connect, both while the server is alive.
	return !clierrors.IsFinalResponseTimeoutError(err) && !clierrors.IsPermanentConnectError(err)
}

// canResendCompile reports whether at least compileResendMinimumBudget is left before resendBefore.
// A zero resendBefore never allows a resend.
func canResendCompile(resendBefore time.Time) bool {
	return !resendBefore.IsZero() && time.Until(resendBefore) >= compileResendMinimumBudget
}

type compileErrorCodeProbe struct {
	ErrorCode string `json:"ErrorCode"`
}

// isCompileEditorBusyRejection reports whether a compile result only says that Unity was still
// compiling or updating when the request arrived.
// Why ErrorCode only: the collision is a structured compile result, not a message string.
func isCompileEditorBusyRejection(raw []byte) bool {
	var probe compileErrorCodeProbe
	if json.Unmarshal(raw, &probe) != nil {
		return false
	}
	return probe.ErrorCode == compileAlreadyInProgressErrorCode ||
		probe.ErrorCode == compileEditorUpdatingErrorCode
}

func logCompileRequestResend(
	connection unityipc.Connection,
	params map[string]any,
	outcome freshCompileAttemptOutcome,
	attempt int,
) {
	requestID, _ := params[compileRequestIDParam].(string)
	writeCompileVibeLog(connection.ProjectRoot, func() vibelog.CLIVibeLogEntry {
		return vibelog.CLIVibeLogEntry{
			Level:     "INFO",
			Operation: "cli_compile_request_resend",
			Message:   "Sending the compile request again.",
			Context: map[string]any{
				"command":          clicore.CompileCommandName,
				"request_id":       requestID,
				"reason":           compileResendReason(outcome),
				"attempt":          attempt,
				"project_identity": vibelog.ProjectIdentity(connection.ProjectRoot),
				"endpoint":         connection.Endpoint.Address,
			},
			CorrelationID: requestID,
		}
	})
}

func compileResendReason(outcome freshCompileAttemptOutcome) string {
	if outcome == freshCompileAttemptEditorBusy {
		return "editor_busy"
	}
	return "request_missing"
}
