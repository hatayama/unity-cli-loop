package clierrors

import "fmt"

// Wire value of runningToolPhase; mirrors ToolExecutionPhase.WaitingForMainThread in the Unity package.
const runningToolPhaseWaitingForMainThread = "WaitingForMainThread"

// Why both conditions: the phase alone also matches a holder that waits a few milliseconds for a
// healthy main thread, and a long stall alone also matches a tool whose own code blocks the main
// thread, where calling it "running" is correct.
func isWaitingForStalledMainThread(data serverBusyErrorData) bool {
	return data.RunningToolPhase == runningToolPhaseWaitingForMainThread &&
		data.SecondsSinceLastMainThreadTick != nil &&
		*data.SecondsSinceLastMainThreadTick >= unityServerBusyResponsivenessStallThresholdSeconds
}

// Why not claim the holder is running: it has either not started its tool code or already
// finished it, so an agent reading "running" waits for, retries, or restarts the wrong thing.
func mainThreadWaitBusyMessage(requestedToolName string, runningToolName string, stalledSeconds float64) string {
	return fmt.Sprintf(
		"'%s' was not executed because '%s' holds Unity's single-flight slot but is not running tool code: it is waiting for the Editor main thread, which has not responded for %ds (e.g. a synchronous asset refresh or script compilation). No uloop command can run until the main thread resumes. Wait for the Editor to become responsive; restart with `uloop launch -r` only if it stays unresponsive for several minutes.",
		requestedToolName,
		runningToolName,
		int(stalledSeconds))
}

// Why restart is only a late fallback: a stall this long is usually a synchronous import or
// compile that ends by itself, but a true deadlock looks the same from outside.
func mainThreadWaitBusyNextActions(runningToolName string) []string {
	return []string{
		fmt.Sprintf(
			"'%s' is waiting for the Editor main thread, not running tool code, so waiting for it to finish does not help on its own.",
			runningToolName),
		"Wait for the Editor to become responsive (a synchronous asset refresh or script compilation can block it for minutes), then run the command again.",
		"Restart with `uloop launch -r` only if the Editor stays unresponsive for several minutes.",
	}
}
