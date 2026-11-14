package projectrunner

import "fmt"

// pausePointTriggerFailedNextActions replaces the generic enable/id-mismatch guidance, which does
// not apply here: the marker was confirmed armed and it is the trigger that never ran. What has to
// change depends on where the rejection came from — the --trigger value for a CLI-side rejection,
// the Editor state for a Unity-side refusal — so all three steps branch on it.
//
// Why re-running the same command comes first: this response answers the command the caller just
// ran, so "fix the --trigger value in that command and run it again" asks them to change one value
// they already typed, with no argument they have to guess. Re-enabling is also the cleaner reset —
// it starts a fresh marker entry (HitCount and IsHit back to zero, the --timeout-seconds countdown
// restarted) while re-patching an id that is already patched is a no-op.
//
// Why the await form carries the real id: it is the one recovery command this function can spell
// out completely, and naming a command without its arguments is exactly the failure this guidance
// exists to prevent.
//
// resumePlay is whether the wait used --resume-play. Why the await form keeps it: the wait re-pauses
// PlayMode it resumed when it abandons the trigger, so a retry without it reaches a paused PlayMode.
func pausePointTriggerFailedNextActions(id string, triggerResult *pausePointTriggerResult, resumePlay bool) []string {
	awaitAction := pausePointTriggerRejectionAwaitAction(id, triggerResult)
	if resumePlay {
		awaitAction += " --resume-play"
	}
	return []string{
		pausePointTriggerRejectionFirstAction(triggerResult),
		awaitAction,
		pausePointTriggerRejectionRecoveryAction(triggerResult),
	}
}

// pausePointTriggerRejectionFirstAction states what has to change, which depends on where the
// rejection came from. Telling a caller to fix a --trigger value that Unity itself accepted as
// well-formed sends them editing a correct command instead of the Editor state that refused it.
func pausePointTriggerRejectionFirstAction(triggerResult *pausePointTriggerResult) string {
	if pausePointTriggerRejectedBeforeExecution(triggerResult) {
		return "Fix the --trigger value in the command you just ran and run that command again. Re-running " +
			"`enable-pause-point --await` is safe and is the cleanest reset: it restarts the marker's " +
			"HitCount and --timeout-seconds countdown, and re-patching an already patched id is a no-op."
	}
	if pausePointTriggerNotRunOnStalledMainThread(triggerResult) {
		return "The trigger command itself was valid; Unity did not run it because its main thread did not " +
			"respond (see the trigger message). Nothing about the command has to change."
	}
	return "The trigger command itself was valid; Unity refused to run it because of the state named in the trigger message."
}

// pausePointTriggerRejectionAwaitAction spells out the await form the caller can fall back on, with
// the placeholder that matches where the rejection came from. Asking for a "corrected" trigger
// command after Unity refused a well-formed one would name a correction that does not exist: the
// command stays as it was and only the Editor state has to change.
func pausePointTriggerRejectionAwaitAction(id string, triggerResult *pausePointTriggerResult) string {
	if pausePointTriggerRejectedBeforeExecution(triggerResult) {
		return fmt.Sprintf(
			"The marker is still armed, so you can also wait on it directly: "+
				"uloop await-pause-point --id %q --trigger \"<corrected trigger command>\"", id)
	}
	if pausePointTriggerNotRunOnStalledMainThread(triggerResult) {
		return fmt.Sprintf(
			"The marker is still armed, so once Unity responds again you can wait on it directly: "+
				"uloop await-pause-point --id %q --trigger \"<the same trigger command>\"", id)
	}
	return fmt.Sprintf(
		"The marker is still armed, so once the precondition holds you can wait on it directly: "+
			"uloop await-pause-point --id %q --trigger \"<the same trigger command>\"", id)
}

// pausePointTriggerRejectionRecoveryAction picks the recovery step that matches where the rejection
// came from. A Unity-side refusal has nothing to do with argument syntax, so the argument/command-name
// advice would send the caller looking for a typo in a value that was already correct.
func pausePointTriggerRejectionRecoveryAction(triggerResult *pausePointTriggerResult) string {
	if pausePointTriggerRejectedBeforeExecution(triggerResult) {
		return "For an INVALID_ARGUMENT rejection, check the rejected value against the triggered command's own --help; for UNKNOWN_COMMAND, the first token must be a uloop subcommand name written without the leading 'uloop'."
	}
	if pausePointTriggerNotRunOnStalledMainThread(triggerResult) {
		return "Run uloop status until it reports Ready (a modal dialog, a long import, or running code can hold the main thread), then run the same command again."
	}
	return "Fix the precondition named in the trigger message (for example enter Play Mode with 'uloop control-play-mode --action Play'), then run the same enable-pause-point command again."
}
