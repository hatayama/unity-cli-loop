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

// needsEarlyDialogInspection reports whether an answer that would let the reader send a command
// has a main thread still long enough that a dialog the Editor cannot report may hold it.
// Why not Busy: it never tells the reader to send a command, so a late look cannot make it wrong.
// Why not Starting: its stall seconds do not describe a running main thread yet.
func needsEarlyDialogInspection(state string, response editorStatusResponse) bool {
	switch state {
	case statusStateReady, statusStateCompiling, statusStateImportingAssets:
		return response.SecondsSinceLastMainThreadTick >= editorStatusEarlyStallSeconds
	default:
		return false
	}
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
	secondReport := reportFromEditorResponse(*second)
	// A smaller stall means the main thread ran between the probes, so it was only sleeping. An
	// answer that changed state, such as one now reporting a dialog or a blocked main thread, takes
	// the usual path.
	if second.SecondsSinceLastMainThreadTick < first.SecondsSinceLastMainThreadTick ||
		!needsEarlyDialogInspection(secondReport.State, *second) {
		return withDialogInspection(ctx, connection.ProjectRoot, *second, secondReport, deps), nil
	}
	dialogs, err := deps.inspectDialogs(ctx, connection.ProjectRoot)
	// Why before the quiet failure below: a cancelled caller must not get the second answer's
	// Ready and exit code 0.
	if ctx.Err() != nil {
		return statusReport{}, ctx.Err()
	}
	// Why a failed look adds no hint: without the permission it would fail every time the Editor
	// hesitates for a second, while the five-second look still explains its failure.
	if err != nil || len(dialogs) == 0 {
		return secondReport, nil
	}
	return blockedByDialogReport(*second, dialogs), nil
}
