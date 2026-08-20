package projectrunner

// enablePausePointPropagatedFields carries enable-time fields into the --await hit and Expired
// responses, matching how Warning alone used to be forwarded from runEnablePausePointAndAwait.
type enablePausePointPropagatedFields struct {
	Warning          string
	ResolvedLine     int
	ResolvedLineText string
	ResolvedMethod   string
	SnapshotTiming   string
}

// mergeEnablePausePointResolvedFields copies enable-time resolution onto a wait status for both
// hit and Expired. Why Line/Text as a pair: Unity SetResolvedLine always writes or clears both
// together, so field-by-field fallback can mix a status line number with enable-time text.
// Prefer the status pair when ResolvedLine is non-zero; otherwise keep enable-time.
// Why Method/SnapshotTiming always from enable: the Unity status DTO still does not carry them.
func mergeEnablePausePointResolvedFields(
	response pausePointStatusResponse,
	enableFields enablePausePointPropagatedFields,
) pausePointStatusResponse {
	if response.ResolvedLine == 0 {
		response.ResolvedLine = enableFields.ResolvedLine
		response.ResolvedLineText = enableFields.ResolvedLineText
	}
	response.ResolvedMethod = enableFields.ResolvedMethod
	response.SnapshotTiming = enableFields.SnapshotTiming
	return response
}
