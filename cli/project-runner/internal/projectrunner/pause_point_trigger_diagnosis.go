package projectrunner

import "encoding/json"

// pausePointTriggerResponseView is the part of a triggered command's response this diagnosis reads.
// Success is a pointer so a response that omits it is treated as "unknown", not as a failure.
type pausePointTriggerResponseView struct {
	Success                      *bool  `json:"Success"`
	RejectedByActivePausePointId string `json:"RejectedByActivePausePointId"`
}

// pausePointTriggerRefusalWarning warns about the case where the marker was hit before the trigger
// ran, so Unity refused the trigger for being called while PlayMode was paused and no input reached
// the game at all. The hit still reports success, which makes this indistinguishable from a real
// input-driven hit unless it is called out.
//
// The refusing pause point's id must match the marker being awaited: a PlayMode paused by some other
// marker is a different problem, and the advice below would be wrong for it. InterruptedByPausePoint
// is deliberately not consulted — it marks the working case, where the marker was hit while the
// input was being applied.
func pausePointTriggerRefusalWarning(result *pausePointTriggerResult, awaitedPausePointID string) string {
	response, ok := decodePausePointTriggerResponse(result)
	if !ok || response.Success == nil || *response.Success {
		return ""
	}
	if response.RejectedByActivePausePointId != awaitedPausePointID {
		return ""
	}

	return "The marker was hit before the trigger ran, so Unity refused the trigger for running " +
		"while PlayMode was paused and no input reached the game. This hit is not evidence about the " +
		"trigger's input: hold the key down with a separate simulate-keyboard KeyDown call before " +
		"arming the marker, or move the marker to a line reached after the input is applied. A marker " +
		"placed after the input would have paused with the input already in effect."
}

// pausePointTriggerFailedPointer reports whether the trigger failed, or nil when there is nothing to
// report: no trigger ran, or it never finished so its outcome is unknown.
func pausePointTriggerFailedPointer(result *pausePointTriggerResult) *bool {
	if !pausePointTriggerFailed(result) {
		return nil
	}
	failed := true
	return &failed
}

// pausePointTriggerFailed reports whether a completed trigger is known to have failed, either
// because its dispatch failed outright (Error) or because it ran and reported Success:false.
func pausePointTriggerFailed(result *pausePointTriggerResult) bool {
	if result == nil || !result.Completed {
		return false
	}
	if result.Error != "" {
		return true
	}

	response, ok := decodePausePointTriggerResponse(result)
	return ok && response.Success != nil && !*response.Success
}

// decodePausePointTriggerResponse reads the fields this diagnosis needs out of the triggered
// command's raw response, leaving TriggerResult.Response itself untouched. An unfinished trigger or
// an unparseable response yields no view: neither can be diagnosed, and guessing either way is worse
// than reporting nothing.
func decodePausePointTriggerResponse(result *pausePointTriggerResult) (pausePointTriggerResponseView, bool) {
	if result == nil || !result.Completed || len(result.Response) == 0 {
		return pausePointTriggerResponseView{}, false
	}

	view := pausePointTriggerResponseView{}
	if err := json.Unmarshal(result.Response, &view); err != nil {
		return pausePointTriggerResponseView{}, false
	}
	return view, true
}
