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
