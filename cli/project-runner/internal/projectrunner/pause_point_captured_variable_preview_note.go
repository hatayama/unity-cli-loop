package projectrunner

const (
	// pausePointCapturedVariablePreviewNote explains how to recover a clipped captured
	// value: raise the enable-time preview cap, or read the live value while paused.
	pausePointCapturedVariablePreviewNote = "a captured value was clipped; re-enable with a larger --max-preview-elements, or read the full value while paused via UloopPausePoint.TryGetCapturedValue in execute-dynamic-code."
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

	response.CapturedVariablePreviewNote = pausePointCapturedVariablePreviewNote
	return response
}
