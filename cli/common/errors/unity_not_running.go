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
	// EditorLogPath is Unity's default Editor log location; empty when it could not be resolved.
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
	phase := ErrorPhaseConnection
	safeToRetry := true
	if err.ExitedDuringCommand {
		phase = ErrorPhaseResponseWaiting
		safeToRetry = isSafeRetryCommand(context.Command)
	}
	return CLIError{
		ErrorCode:   ErrorCodeUnityNotRunning,
		Phase:       phase,
		Message:     err.message(),
		Retryable:   false,
		SafeToRetry: safeToRetry,
		ProjectRoot: firstNonEmpty(context.ProjectRoot, err.ProjectRoot),
		Command:     context.Command,
		NextActions: err.nextActions(safeToRetry),
		Details:     err.details(),
	}
}

// Why "may have crashed" and not "crashed": a lockfile left behind proves only that the normal shutdown
// did not finish, which a forced quit or an interrupted cleanup also leaves.
func (err UnityNotRunningError) message() string {
	message := "No Unity Editor is running for this project."
	if err.ExitedDuringCommand {
		message = "Unity exited while this command was running; no Unity Editor is running for this project now."
	}
	if err.LockfileLeftBehind {
		message += " It left Temp/UnityLockfile behind, so it did not shut down normally and may have crashed."
	}
	return message
}

func (err UnityNotRunningError) nextActions(safeToRetry bool) []string {
	actions := []string{}
	if err.LockfileLeftBehind {
		actions = append(actions, err.EditorLogNextAction())
	}
	if !safeToRetry {
		actions = append(actions, "The command may have partly run before Unity exited; check the project and scene state before running it again.")
	}
	return append(actions, "Run `uloop launch` to start Unity, then run the command again.")
}

// EditorLogNextAction is the step that sends the caller to the Editor log before launching Unity again.
// Why before the launch step: Unity renames Editor.log to Editor-prev.log when it starts, and keeps only
// that one previous log, so a second launch loses the record of why the Editor went away.
func (err UnityNotRunningError) EditorLogNextAction() string {
	if err.EditorLogPath == "" {
		return "Before launching, read the end of Unity's Editor log for why it exited; the next launch renames it to Editor-prev.log."
	}
	return "Before launching, read the end of the Editor log for why it exited: " + err.EditorLogPath +
		" (the next launch renames it to Editor-prev.log)."
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
