package projectrunner

import "fmt"

const (
	// pausePointCapturedVariablePreviewNote explains how to recover a clipped captured
	// value: raise the enable-time preview cap after preserving evidence, or read the live value
	// while paused.
	pausePointCapturedVariablePreviewNoteFormat = "a captured value was clipped at the current --max-preview-elements cap of %d elements; re-enable with a larger cap to widen future previews, but first read any CapturedVariables and CapturedVariableHistory you still need with pause-point-status, because re-enabling starts a new generation and discards them. While Unity is still paused, UloopPausePoint.TryGetCapturedValue in execute-dynamic-code returns the full live value."
)

// applyPausePointCapturedVariablePreviewNote records that a listed captured value was
// clipped. It keys off remaining variable Truncated flags, not the top-level truncation
// flag, so a name filter that dropped every clipped variable does not emit this note.
func applyPausePointCapturedVariablePreviewNote(
	response pausePointStatusResponse,
) pausePointStatusResponse {
	if !pausePointResponseHasTruncatedCapturedVariable(response) {
		return response
	}

	response.CapturedVariablePreviewNote = fmt.Sprintf(
		pausePointCapturedVariablePreviewNoteFormat,
		response.MaxPreviewElements)
	return response
}
