package projectrunner

import "time"

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
	// is never sent again. Otherwise they are reported only while enough wait time remains before
	// this moment for another attempt.
	resendBefore time.Time
	// Zero waits as long as --timeout-seconds says, for the first attempt and for entries that
	// never send again. A positive value is this attempt's wait limit.
	timeoutOverride time.Duration
}
