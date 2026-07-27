package projectrunner

import (
	"bytes"
	"context"
	"errors"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// runEnableAwaitWithStubbedTrigger drives the enable-pause-point --await hit path, the second hit
// payload builder, with the same stubs the plain await path's tests use.
func runEnableAwaitWithStubbedTrigger(t *testing.T, enableWarning string) (int, string) {
	t.Helper()

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointWaitAfterEnable(
		context.Background(),
		unityipc.Connection{},
		waitForPausePointOptions{
			id:                   "jump",
			timeoutSeconds:       1,
			timeout:              time.Second,
			matchingLogsMaxCount: 5,
			triggerCommand:       "simulate-keyboard",
			triggerArgs:          []string{"--action", "Press", "--key", "W"},
		},
		enablePausePointPropagatedFields{Warning: enableWarning},
		&stdout,
		&stderr,
	)
	if stderr.Len() > 0 {
		t.Logf("stderr: %s", stderr.String())
	}
	return code, stdout.String()
}

// Verifies enable-pause-point --await diagnoses a refused trigger exactly as await-pause-point does:
// the two commands build their hit payloads separately, so a diagnosis wired into only one is
// invisible to callers of the other, which is the form this project's own checklist exercises.
func TestRunPausePointWaitAfterEnableWarnsWhenTheTriggerWasRefusedByThisMarker(t *testing.T) {
	stubPausePointHit(t, "")
	stubPausePointMatchingLogs(t, nil)
	stubPausePointTriggerDispatch(t, pausePointRejectedTriggerResponse("jump"))

	code, output := runEnableAwaitWithStubbedTrigger(t, "")

	if code != 0 {
		t.Fatalf("expected the hit to stay a success, got %d: %s", code, output)
	}
	result := decodePausePointWaitResult(t, output)
	if !strings.Contains(result.Warning, "refused") {
		t.Errorf("expected a refusal warning: %q", result.Warning)
	}
	if result.TriggerFailed == nil || !*result.TriggerFailed {
		t.Errorf("TriggerFailed must be promoted to the top level: %#v", result.TriggerFailed)
	}
}

// Verifies the enable-time warning survives next to the CLI's refusal warning, and that the
// refusal warning also survives a failed matching-log fetch.
func TestRunPausePointWaitAfterEnableKeepsEnableWarningWithTheRefusalWarning(t *testing.T) {
	stubPausePointHit(t, "")
	stubPausePointMatchingLogs(t, errors.New("unity busy"))
	stubPausePointTriggerDispatch(t, pausePointRejectedTriggerResponse("jump"))

	_, output := runEnableAwaitWithStubbedTrigger(t, "Enable-time warning.")

	if strings.Contains(output, `"MatchingLogs"`) {
		t.Errorf("a failed fetch must omit MatchingLogs entirely: %s", output)
	}
	result := decodePausePointWaitResult(t, output)
	if !strings.Contains(result.Warning, "Enable-time warning.") {
		t.Errorf("the enable-time warning was dropped: %q", result.Warning)
	}
	if !strings.Contains(result.Warning, "refused") {
		t.Errorf("the refusal warning was dropped: %q", result.Warning)
	}
}

// Verifies a warning reported by both the enable response and the status poll is printed once:
// repeating identical text reads as two separate problems.
func TestRunPausePointWaitAfterEnableReportsARepeatedUnityWarningOnce(t *testing.T) {
	stubPausePointHit(t, "Same Unity warning.")
	stubPausePointMatchingLogs(t, nil)
	stubPausePointTriggerDispatch(t, `{"Success":true}`)

	_, output := runEnableAwaitWithStubbedTrigger(t, "Same Unity warning.")

	result := decodePausePointWaitResult(t, output)
	if strings.Count(result.Warning, "Same Unity warning.") != 1 {
		t.Errorf("expected the repeated warning exactly once: %q", result.Warning)
	}
}
