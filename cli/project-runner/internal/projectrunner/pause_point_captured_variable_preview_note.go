package projectrunner

import "fmt"

const (
	// pausePointCapturedVariablePreviewNote explains that a clipped captured value cannot be
	// recovered after the fact: raising the enable-time preview cap only widens future previews,
	// and the raw capture holds a reference rather than a snapshot, so reading it while paused is
	// exact only for values that cannot change after capture.
	pausePointCapturedVariablePreviewNoteFormat = "a captured value was clipped at the current --max-preview-elements cap of %d elements; re-enable with a larger cap to widen future previews, but first read any CapturedVariables and CapturedVariableHistory you still need with pause-point-status, because re-enabling starts a new generation and discards them. A clipped collection or object cannot be recovered after the fact: the capture keeps a reference, not a snapshot, so while Unity is still paused UloopPausePoint.TryGetCapturedValue in execute-dynamic-code returns the current live object, whose contents may already differ from the paused line. It is exact only for values that cannot change after capture (numbers, enums, strings, other immutable values); anything that holds a collection or another object shares it with the running game. For at-line contents, re-enable with a larger cap."
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
