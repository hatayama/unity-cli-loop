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

// Verifies the enable-time warning joins the CLI's refusal warning in the single Warnings
// aggregate, carries the enable-time prefix, and survives a failed matching-log fetch.
func TestRunPausePointWaitAfterEnableKeepsEnableWarningWithTheRefusalWarning(t *testing.T) {
	stubPausePointHit(t, "")
	stubPausePointMatchingLogs(t, errors.New("unity busy"))
	stubPausePointTriggerDispatch(t, pausePointRejectedTriggerResponse("jump"))

	_, output := runEnableAwaitWithStubbedTrigger(t, "Enable-time warning.")

	if strings.Contains(output, `"MatchingLogs"`) {
		t.Errorf("a failed fetch must omit MatchingLogs entirely: %s", output)
	}
	result := decodePausePointWaitResult(t, output)
	if len(result.Warnings) != 2 {
		t.Fatalf("both topics must be their own Warnings entry: %#v", result.Warnings)
	}
	// Asserting the order and the exact join, not just membership: a regression that split the two
	// topics back into separate channels would still satisfy a Contains-only check.
	if !strings.Contains(result.Warnings[0], "refused") {
		t.Errorf("the refusal warning must come first: %#v", result.Warnings)
	}
	if result.Warnings[1] != pausePointEnableTimeWarningPrefix+"Enable-time warning." {
		t.Errorf("the prefixed enable-time warning must come last: %#v", result.Warnings)
	}
	if result.Warning != strings.Join(result.Warnings, " ") {
		t.Errorf("Warning must be the joined form of Warnings: %q vs %#v", result.Warning, result.Warnings)
	}
}
