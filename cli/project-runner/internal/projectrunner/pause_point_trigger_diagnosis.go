package projectrunner

import (
	"encoding/json"
	"strings"
)

// pausePointTriggerResponseView is the part of a triggered command's response this diagnosis reads.
// Success is a pointer so a response that omits it is treated as "unknown", not as a failure.
type pausePointTriggerResponseView struct {
	Success                      *bool  `json:"Success"`
	RejectedByActivePausePointId string `json:"RejectedByActivePausePointId"`

	// RejectedBeforeExecution is set by a triggered command whose preflight refused it, so the
	// command performed no action at all. Unity reports this on stdout as a normal response, which
	// is why the stderr error-envelope check alone cannot see it.
	RejectedBeforeExecution bool `json:"RejectedBeforeExecution"`

	// Message carries the triggered command's own reason for failing, so the wait can state it
	// instead of asserting a cause it did not observe.
	Message string `json:"Message"`
}

// pausePointTriggerRejectedByUnityBeforeExecution reports whether Unity refused the triggered
// command before it executed anything. Like the stderr-envelope check, this proves the trigger
// performed no action, so waiting out the marker's remaining lifetime cannot change the outcome.
//
// Why the awaited marker is excluded: a rejection naming the marker being awaited is the marker
// having been hit before the trigger ran (pausePointTriggerRefusalWarning diagnoses that case), and
// the hit itself is the wait's success, not a reason to abort.
func pausePointTriggerRejectedByUnityBeforeExecution(
	result *pausePointTriggerResult,
	awaitedPausePointID string,
) bool {
	response, ok := decodePausePointTriggerResponse(result)
	if !ok || response.Success == nil || *response.Success {
		return false
	}
	if !response.RejectedBeforeExecution {
		return false
	}
	return response.RejectedByActivePausePointId != awaitedPausePointID
}

// pausePointTriggerRejectionReason returns the triggered command's own reason for the failure,
// shaped to read inside a parenthesis: a trailing sentence period is dropped so the enclosing
// sentence does not end in "..)."
//
// Why the dispatch error is consulted before any fixed text: Error carries whatever the dispatch
// wrote to stderr, which is every failure shape and not only the two permanent rejections — a
// dropped connection and an unreachable Editor land here too. Naming argument parsing for one of
// those would be exactly the invented cause this diagnosis exists to remove, so the fixed text is
// the last resort, used only when the failure produced no text at all.
func pausePointTriggerRejectionReason(result *pausePointTriggerResult) string {
	response, ok := decodePausePointTriggerResponse(result)
	if ok && response.Message != "" {
		return trimPausePointReasonForParenthesis(response.Message)
	}
	if result != nil && strings.TrimSpace(result.Error) != "" {
		return trimPausePointReasonForParenthesis(pausePointTriggerDispatchErrorReason(result.Error))
	}
	return "argument parsing or an unknown command name"
}

// pausePointTriggerDispatchErrorReason reads the human-readable reason out of a dispatch failure's
// stderr: the error envelope's Message when it is one, and the raw text otherwise, because a
// dispatch that never produced an envelope still wrote the only explanation there is.
func pausePointTriggerDispatchErrorReason(dispatchError string) string {
	trimmed := strings.TrimSpace(dispatchError)
	envelope := struct {
		Error struct {
			Message string `json:"Message"`
		} `json:"Error"`
	}{}
	if err := json.Unmarshal([]byte(trimmed), &envelope); err == nil && envelope.Error.Message != "" {
		return envelope.Error.Message
	}
	return trimmed
}

func trimPausePointReasonForParenthesis(reason string) string {
	return strings.TrimSuffix(strings.TrimSpace(reason), ".")
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
