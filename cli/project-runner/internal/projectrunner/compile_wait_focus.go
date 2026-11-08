package projectrunner

import (
	"context"
	"encoding/json"
	"fmt"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

// compileStartPendingFocusThreshold is how long Unity may report a compile request as accepted
// without starting it before the CLI brings the Editor to the front. Why 10 s: a legitimate gap
// between the request and compilationStarted is a few seconds at most, and the known cause (Unity
// believing a mouse button is held while in the background) clears as soon as the window is focused.
const compileStartPendingFocusThreshold = 10 * time.Second

const compileNotStartedErrorCode = "COMPILE_NOT_STARTED"

// compileStartPendingFocus brings the Editor to the front once per compile wait when Unity keeps a
// compile request pending, and restores the previously frontmost window when the wait ends.
type compileStartPendingFocus struct {
	controller *connectionRetryFocusController
	report     compileWaitInterimReporter
	attempted  bool
}

// compileStartPendingFocusOutcome is what the wait recorded about its focus attempt, for the
// COMPILE_NOT_STARTED diagnostics.
type compileStartPendingFocusOutcome struct {
	attempted bool
	succeeded bool
}

func newCompileStartPendingFocus(options compileCompletionOptions, deps compileWaitDeps) *compileStartPendingFocus {
	controller := newConnectionRetryFocusController(options.connection, clicore.CompileCommandName, connectionRetryDeps{
		findRunningUnityProcess: deps.findRunningUnityProcess,
		focusUnityProcess:       deps.focusUnityProcess,
	})
	return &compileStartPendingFocus{controller: controller, report: deps.reportInterim}
}

// observe looks at one answered status poll and focuses the Editor the first time the pending-start
// time reaches the threshold. A failed poll carries no answer from Unity and is ignored, so a
// silent Editor (#3228) never triggers the focus.
func (focus *compileStartPendingFocus) observe(ctx context.Context, status compileStatusResponse, err error) {
	if err != nil || focus.attempted {
		return
	}
	if status.CompileStartPendingSeconds < compileStartPendingFocusThreshold.Seconds() {
		return
	}
	focus.attempted = true

	focus.controller.tryFocus(
		ctx,
		focusReasonCompileStartPending,
		fmt.Errorf("unity accepted the compile request but has not started compiling for %.0fs", status.CompileStartPendingSeconds),
	)
	// No running Editor was found: there is nothing to focus and nothing to tell the user.
	if !focus.controller.attempted {
		return
	}
	if focus.report != nil {
		focus.report(formatCompileStartPendingFocusLine(status.CompileStartPendingSeconds, focus.controller.focusSucceeded))
	}
}

// finish restores the previously frontmost window. Why on every wait end: the compile result, or
// the error that replaces it, is what the caller acts on next, and that happens in their own window.
func (focus *compileStartPendingFocus) finish() {
	restoreContext, cancel := context.WithTimeout(context.Background(), focusRestoreTimeout)
	defer cancel()
	focus.controller.restore(restoreContext)
}

func (focus *compileStartPendingFocus) outcome() compileStartPendingFocusOutcome {
	return compileStartPendingFocusOutcome{
		attempted: focus.controller.attempted,
		succeeded: focus.controller.focusSucceeded,
	}
}

func formatCompileStartPendingFocusLine(pendingSeconds float64, focused bool) string {
	if focused {
		return fmt.Sprintf(
			"compile: Unity accepted the compile request %.0fs ago but has not started compiling; brought the Unity Editor to the front so it can start. The previous window is restored when this command finishes.",
			pendingSeconds,
		)
	}
	return fmt.Sprintf(
		"compile: Unity accepted the compile request %.0fs ago but has not started compiling, and uloop could not bring the Unity Editor to the front. Click inside the Unity window so the compile can start.",
		pendingSeconds,
	)
}

// addCliDiagnosticsToNotStartedResult records, in a COMPILE_NOT_STARTED result's Details, what the
// CLI knows that Unity cannot: whether the Editor was brought to the front and whether the OS sees a
// mouse button held. Every other result is returned as it came.
func addCliDiagnosticsToNotStartedResult(
	result json.RawMessage,
	focusOutcome compileStartPendingFocusOutcome,
	readHeldMouseButtons func() ([]string, bool),
) json.RawMessage {
	var fields map[string]json.RawMessage
	if json.Unmarshal(result, &fields) != nil || fields == nil {
		return result
	}
	var errorCode string
	if json.Unmarshal(fields["ErrorCode"], &errorCode) != nil || errorCode != compileNotStartedErrorCode {
		return result
	}

	details := map[string]any{}
	if existing, present := fields["Details"]; present {
		// A null or undecodable Details is replaced by the CLI entries alone.
		_ = json.Unmarshal(existing, &details)
	}
	if details == nil {
		details = map[string]any{}
	}
	details["CliFocusAttempted"] = focusOutcome.attempted
	details["CliFocusSucceeded"] = focusOutcome.succeeded
	details["HeldMouseButtons"] = heldMouseButtonsDetail(readHeldMouseButtons)

	encodedDetails, err := json.Marshal(details)
	if err != nil {
		return result
	}
	fields["Details"] = encodedDetails
	encoded, err := json.Marshal(fields)
	if err != nil {
		return result
	}
	return encoded
}

func heldMouseButtonsDetail(readHeldMouseButtons func() ([]string, bool)) any {
	if readHeldMouseButtons == nil {
		return nil
	}
	buttons, available := readHeldMouseButtons()
	if !available {
		return nil
	}
	return buttons
}
