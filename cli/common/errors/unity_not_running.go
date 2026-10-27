package clierrors

// UnityNotRunningError reports that a process lookup that succeeded found no Unity Editor for the
// project, after the server could not be reached. Only a lookup that answered may produce it: a lookup
// that failed observed nothing and must leave the connection error as it is.
//
// Why it neither unwraps to Cause nor repeats it in Error(): the callers that wait out a domain reload
// recognise a dropped connection through errors.Is and through message text such as "EOF". Matching
// either would send them into a wait for a server that cannot come back, which is the failure this error
// exists to end. The cause is kept for Details only.
type UnityNotRunningError struct {
	ProjectRoot string
	Endpoint    string
	Cause       error
	// LockfileLeftBehind is true when Temp/UnityLockfile, which Unity removes when it shuts down normally,
	// is still there.
	LockfileLeftBehind bool
	// EditorLogPath is the log this Editor wrote at Unity's default location; empty when no log there is
	// this Editor's.
	EditorLogPath string
	// ExitedDuringCommand is true when this command had reached Unity before it went away, so the command
	// may have partly run.
	ExitedDuringCommand bool
}

func (err UnityNotRunningError) Error() string {
	if err.ExitedDuringCommand {
		return "Unity exited while the command was running"
	}
	return "no Unity Editor is running for this project"
}

func (err UnityNotRunningError) ToCLIError(context ErrorContext) CLIError {
	launching := err.ExitedDuringCommand && context.Command == commandLaunch
	phase := ErrorPhaseConnection
	safeToRetry := true
	// Why launch stays safe: running it again starts a new Editor and cannot double-apply anything.
	if err.ExitedDuringCommand && !launching {
		phase = ErrorPhaseResponseWaiting
		safeToRetry = isSafeRetryCommand(context.Command)
	}
	return CLIError{
		ErrorCode:   ErrorCodeUnityNotRunning,
		Phase:       phase,
		Message:     err.message(launching),
		Retryable:   false,
		SafeToRetry: safeToRetry,
		ProjectRoot: firstNonEmpty(context.ProjectRoot, err.ProjectRoot),
		Command:     context.Command,
		NextActions: err.nextActions(launching, safeToRetry),
		Details:     err.details(),
	}
}

// Why "may have crashed" and not "crashed": a lockfile left behind proves only that the normal shutdown
// did not finish, which a forced quit or an interrupted cleanup also leaves.
func (err UnityNotRunningError) message(launching bool) string {
	message := "No Unity Editor is running for this project."
	if launching {
		message = "Unity exited before it finished starting; no Unity Editor is running for this project now."
	} else if err.ExitedDuringCommand {
		message = "Unity exited while this command was running; no Unity Editor is running for this project now."
	}
	if err.LockfileLeftBehind {
		message += " It left Temp/UnityLockfile behind, so it did not shut down normally and may have crashed."
	}
	return message
}

func (err UnityNotRunningError) nextActions(launching bool, safeToRetry bool) []string {
	actions := err.EditorLogNextActions()
	if launching {
		return append(actions, "Run `uloop launch` again.")
	}
	if !safeToRetry {
		actions = append(actions, "The command may have partly run before Unity exited; check the project and scene state before running it again.")
	}
	return append(actions, "Run `uloop launch` to start Unity, then run the command again.")
}

// EditorLogNextActions returns the step that sends the caller to this Editor's log before launching Unity
// again, or no step when the Editor shut down normally or no log of it was found.
// Why before the launch step: every Editor start renames Editor.log to Editor-prev.log and replaces the
// previous Editor-prev.log, so launching first can lose the record of why the Editor went away.
func (err UnityNotRunningError) EditorLogNextActions() []string {
	if !err.LockfileLeftBehind || err.EditorLogPath == "" {
		return []string{}
	}
	return []string{"Before launching, read the end of this Editor's log for why it exited: " + err.EditorLogPath +
		" (the next Editor start moves or replaces it)."}
}

func (err UnityNotRunningError) details() map[string]any {
	details := map[string]any{}
	if err.Endpoint != "" {
		details["Endpoint"] = err.Endpoint
	}
	if err.Cause != nil {
		details["Cause"] = err.Cause.Error()
	}
	if err.LockfileLeftBehind && err.EditorLogPath != "" {
		details["EditorLog"] = err.EditorLogPath
	}
	return details
}
