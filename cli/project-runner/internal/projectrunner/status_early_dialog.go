package projectrunner

import (
	"context"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// editorStatusEarlyStallSeconds is how long the main thread may be still before status asks again
// and looks for a dialog, well before it counts as blocked.
// Why one second: a healthy Editor's main thread always runs within a second, because the
// Editor loop is ticked at 6Hz or more even without focus. An Editor that predates OpenDialogs
// would otherwise be reported Ready for the first five seconds a dialog is open.
const editorStatusEarlyStallSeconds = 1.0

// letsReaderSend reports whether the state tells the reader it may send a command, which a dialog the Editor
// cannot report would make wrong.
// Why not Busy: it never tells the reader to send a command, so a late look cannot make it wrong.
// Why not Starting: its stall seconds do not describe a running main thread yet.
func letsReaderSend(state string) bool {
	switch state {
	case statusStateReady, statusStateCompiling, statusStateImportingAssets:
		return true
	default:
		return false
	}
}

// needsEarlyDialogInspection reports whether an answer that would let the reader send a command
// has a main thread still long enough that a dialog the Editor cannot report may hold it.
func needsEarlyDialogInspection(state string, response editorStatusResponse) bool {
	return letsReaderSend(state) && response.SecondsSinceLastMainThreadTick >= editorStatusEarlyStallSeconds
}

// looksOnEveryAnswer reports whether status looks at Unity's windows on every answer that would
// let the reader send a command, instead of only after a short stall.
// Why Windows only: listing a process's windows there is a few milliseconds and needs no
// permission, so the second of Ready a dialog would otherwise get is not worth saving. On macOS
// the look starts osascript and needs a permission that may be missing.
func looksOnEveryAnswer(goos string) bool {
	return goos == "windows"
}

// probeAfterShortStall asks the Editor again after a short stall and looks at Unity's windows
// only when the main thread did not run in between. A second answer that cannot be read is
// reported as the five-second recheck reports it, because an Editor that was still and then
// stopped answering must not be called Ready.
func probeAfterShortStall(
	ctx context.Context,
	connection unityipc.Connection,
	first editorStatusResponse,
	deps statusCommandDeps,
) (statusReport, error) {
	second, report, err := reprobeEditorStatus(ctx, connection, deps)
	if second == nil {
		return report, err
	}
	// A smaller stall means the main thread ran between the probes, so it was only sleeping. An
	// answer that changed state, such as one now reporting a dialog or a blocked main thread, takes
	// the usual path.
	if second.SecondsSinceLastMainThreadTick < first.SecondsSinceLastMainThreadTick ||
		!needsEarlyDialogInspection(classifyEditorState(*second), *second) {
		return withDialogInspection(ctx, connection.ProjectRoot, *second, reportFromEditorResponse(*second), deps), nil
	}
	return reportAfterQuietDialogLook(ctx, connection.ProjectRoot, *second, deps)
}

// reportAfterQuietDialogLook looks at Unity's windows once for an answer that would let the reader
// send a command: a dialog found makes it BlockedByDialog, and anything else keeps the answer's
// usual report.
// Why a failed look adds no hint: without the permission it would fail every time it runs, while
// the five-second look still explains its failure.
func reportAfterQuietDialogLook(
	ctx context.Context,
	projectRoot string,
	response editorStatusResponse,
	deps statusCommandDeps,
) (statusReport, error) {
	dialogs, err := deps.inspectDialogs(ctx, projectRoot, response.ProcessId)
	// Why before the quiet failure below: a cancelled caller must not get the answer's Ready and
	// exit code 0.
	if ctx.Err() != nil {
		return statusReport{}, ctx.Err()
	}
	if err != nil || len(dialogs) == 0 {
		return reportFromEditorResponse(response), nil
	}
	return blockedByDialogReport(response, dialogs), nil
}
