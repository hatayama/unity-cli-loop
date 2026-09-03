package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

func runAwaitWithoutTrigger(t *testing.T) string {
	t.Helper()

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:                   "jump",
		timeoutSeconds:       1,
		timeout:              time.Second,
		matchingLogsMaxCount: 5,
	}, &stdout, &stderr)
	if code != 0 {
		t.Fatalf("expected the hit to be a success, got %d with stderr %s", code, stderr.String())
	}
	return stdout.String()
}

// Verifies Unity's own warning reaches the caller on a hit. The CLI's Warning field shadows the
// embedded Unity one — both serialize as "Warning" — so Unity's text is dropped unless joined in.
func TestRunWaitForPausePointKeepsUnityWarningOnAHit(t *testing.T) {
	stubPausePointHit(t, "Unity-side enable warning.")
	stubPausePointMatchingLogs(t, nil)

	result := decodePausePointWaitResult(t, runAwaitWithoutTrigger(t))

	if !strings.Contains(result.Warning, "Unity-side enable warning.") {
		t.Errorf("Unity's warning was dropped: %q", result.Warning)
	}
}

// Verifies Unity's warning also survives the failed-log-fetch branch, which builds a different
// payload shape and previously had no Warning field at all.
func TestRunWaitForPausePointKeepsUnityWarningWhenTheLogFetchFails(t *testing.T) {
	stubPausePointHit(t, "Unity-side enable warning.")
	stubPausePointMatchingLogs(t, errors.New("unity busy"))

	result := decodePausePointWaitResult(t, runAwaitWithoutTrigger(t))

	if !strings.Contains(result.Warning, "Unity-side enable warning.") {
		t.Errorf("Unity's warning was dropped on the failed-fetch branch: %q", result.Warning)
	}
}

// Verifies Warnings carries every topic Warning joins, so a caller reading only the array never
// sees fewer warnings than the string — the shape a hit payload used to emit.
func TestRunWaitForPausePointKeepsWarningAndWarningsInAgreement(t *testing.T) {
	stubPausePointHit(t, "Unity-side enable warning.")
	stubPausePointMatchingLogs(t, nil)

	result := decodePausePointWaitResult(t, runAwaitWithoutTrigger(t))

	if len(result.Warnings) == 0 {
		t.Fatalf("Warning must never be non-empty while Warnings is empty: %q", result.Warning)
	}
	if result.Warning != strings.Join(result.Warnings, " ") {
		t.Errorf("Warning must be the joined form of Warnings: %q vs %#v", result.Warning, result.Warnings)
	}
}

// Verifies a hit that warned about nothing omits both warning fields rather than emitting an
// empty string next to a missing array.
func TestRunWaitForPausePointOmitsBothWarningFieldsWhenNothingWarned(t *testing.T) {
	stubPausePointHit(t, "")
	stubPausePointMatchingLogs(t, nil)

	output := runAwaitWithoutTrigger(t)

	raw := map[string]json.RawMessage{}
	if err := json.Unmarshal([]byte(output), &raw); err != nil {
		t.Fatalf("failed to decode raw stdout: %v\n%s", err, output)
	}
	for _, warningKey := range []string{"Warning", "Warnings"} {
		if _, ok := raw[warningKey]; ok {
			t.Errorf("%s must be omitted when nothing warned: %s", warningKey, output)
		}
	}
}

// Verifies a hit Message ends by naming both aggregates, so an agent that stops at Message still
// learns a warning and a status note are waiting for it.
func TestRunWaitForPausePointPointsMessageAtWarningsAndStatusNote(t *testing.T) {
	stubPausePointHit(t, "Unity-side enable warning.")
	stubPausePointMatchingLogs(t, nil)

	result := decodePausePointWaitResult(t, runAwaitWithoutTrigger(t))

	if result.StatusNote == "" {
		t.Fatal("this scenario is meant to produce a StatusNote")
	}
	expectedSuffix := "1 warning(s). See Warnings. See StatusNote."
	if !strings.HasSuffix(result.Message, expectedSuffix) {
		t.Errorf("Message must end with %q, got %q", expectedSuffix, result.Message)
	}
}

// Verifies a hit that warned about nothing gets no warning-count clause, while the StatusNote
// pointer still lands.
func TestRunWaitForPausePointOmitsTheWarningCountWhenNothingWarned(t *testing.T) {
	stubPausePointHit(t, "")
	stubPausePointMatchingLogs(t, nil)

	result := decodePausePointWaitResult(t, runAwaitWithoutTrigger(t))

	if strings.Contains(result.Message, "warning(s). See Warnings.") {
		t.Errorf("Message must not claim warnings when there are none: %q", result.Message)
	}
	if result.Message != "See StatusNote." {
		t.Errorf("Message must still point at StatusNote: %q", result.Message)
	}
}

// Verifies pause-point-status appends the same pointers: the two commands shape their payloads
// separately, so a pointer wired into only one is invisible to callers of the other.
func TestRunPausePointStatusPointsMessageAtWarningsAndStatusNote(t *testing.T) {
	originalQuery := queryPausePointStatus
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
	})
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Success:  true,
			Id:       id,
			Status:   pausePointStatusHit,
			IsHit:    true,
			HitCount: 1,
			Message:  "Pause point hit.",
			Warnings: []string{"Suppressed by hot reload."},
		}, nil
	}

	code, output := runPausePointStatusForExpect(t, []string{"--id", "jump"})
	if code != 0 {
		t.Fatalf("expected success, got %d: %s", code, output)
	}

	result := pausePointStatusResult{}
	if err := json.Unmarshal([]byte(output), &result); err != nil {
		t.Fatalf("stdout parse failed: %v from %s", err, output)
	}
	expected := "Pause point hit. 1 warning(s). See Warnings. See StatusNote."
	if result.Message != expected {
		t.Errorf("Message = %q, want %q", result.Message, expected)
	}
	if result.Warning != "Suppressed by hot reload." {
		t.Errorf("Warning must be derived from Warnings: %q", result.Warning)
	}
}

// stubPausePointTruncatedMultiLogFetch reports more matching logs than it returns, which is the one
// situation that raises two log-side topics at once: truncation and multiple matches.
func stubPausePointTruncatedMultiLogFetch(t *testing.T) {
	t.Helper()

	originalFetch := fetchMatchingLogs
	t.Cleanup(func() {
		fetchMatchingLogs = originalFetch
	})

	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{
			SearchText:     searchText,
			TotalCount:     5,
			DisplayedCount: 2,
			MaxCount:       maxCount,
			Logs: []pausePointMatchingLog{
				{Type: "Log", Message: "[jump] hit"},
				{Type: "Log", Message: "[jump] hit again"},
			},
		}, nil
	}
}

// Verifies two simultaneous log diagnoses become two Warnings entries and a Message count of 2,
// rather than one entry holding both sentences under a count of 1.
func TestRunWaitForPausePointListsEachLogDiagnosisAsItsOwnWarning(t *testing.T) {
	stubPausePointHit(t, "")
	stubPausePointTruncatedMultiLogFetch(t)

	result := decodePausePointWaitResult(t, runAwaitWithoutTrigger(t))

	if len(result.Warnings) != 2 {
		t.Fatalf("each log diagnosis must be its own entry: %#v", result.Warnings)
	}
	if !strings.Contains(result.Message, "2 warning(s). See Warnings.") {
		t.Fatalf("Message must count the entries it points at: %q", result.Message)
	}
	if result.Warning != strings.Join(result.Warnings, " ") {
		t.Fatalf("Warning must be the joined form of Warnings: %q", result.Warning)
	}
}
