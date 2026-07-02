package cli

import "fmt"

func compileWaitTimeoutError(projectRoot string) cliError {
	return cliError{
		ErrorCode: errorCodeCompileWaitTimeout,
		Phase:     errorPhaseCompileWaiting,
		Message: fmt.Sprintf(
			"Compile status wait timed out after %dms. This does not mean the Unity Editor is frozen; the compile may simply still be running.",
			compileWaitTimeout.Milliseconds()),
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		Command:     compileCommandName,
		// Agents have terminated whole sessions after misreading this timeout as a
		// frozen Editor, so the guidance must walk them through a responsiveness check.
		NextActions: []string{
			"Run a light command such as `uloop get-logs --max-count 1` to check whether Unity is responsive before treating this as a freeze.",
			"If Unity responds, retry `uloop compile`; the previous compile likely finished in the meantime.",
			"Only if Unity does not respond to any command, restart it with `uloop launch -r`.",
		},
	}
}
