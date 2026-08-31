package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"strings"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const enableTimePatchWarning = "GameObjects that already existed at enable time may never reach the marker."

// Verifies a successful enable --await hit keeps the enable-time patch warning out of Warning and
// exposes it on EnableTimeWarning instead.
func TestRunPausePointWaitAfterEnableSeparatesEnableTimeWarningOnHit(t *testing.T) {
	stubPausePointHit(t, "")
	stubPausePointMatchingLogs(t, nil)
	stubPausePointTriggerDispatch(t, `{"Success":true}`)

	code, output := runEnableAwaitWithStubbedTrigger(t, enableTimePatchWarning)
	if code != 0 {
		t.Fatalf("expected hit success, got %d: %s", code, output)
	}
	result := decodePausePointWaitResult(t, output)
	if result.EnableTimeWarning != enableTimePatchWarning {
		t.Fatalf("EnableTimeWarning mismatch: %q", result.EnableTimeWarning)
	}
	if strings.Contains(result.Warning, enableTimePatchWarning) {
		t.Fatalf("enable-time warning must not appear in Warning: %q", result.Warning)
	}
}

// Verifies a non-hit enable --await path still surfaces the enable-time warning on the existing
// Details.EnableWarning key (unchanged; not folded into Details.Warning).
func TestRunPausePointWaitAfterEnableKeepsEnableWarningDetailOnExpired(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusExpired,
			Expired:     true,
			EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
			Message:     "Pause point expired before it was hit.",
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointWaitAfterEnable(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		waitForPausePointOptions{
			id:                   "jump",
			timeoutSeconds:       1,
			timeout:              time.Second,
			matchingLogsMaxCount: 5,
			markerJustEnabled:    true,
		},
		enablePausePointPropagatedFields{Warning: enableTimePatchWarning},
		&stdout,
		&stderr,
	)
	if code != 1 {
		t.Fatalf("expected non-hit failure, got %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}

	var envelope struct {
		Error clierrors.CLIError `json:"Error"`
	}
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("unmarshal stderr: %v (%s)", err, stderr.String())
	}
	if envelope.Error.ErrorCode != clierrors.ErrorCodePausePointExpired {
		t.Fatalf("error code mismatch: %s", envelope.Error.ErrorCode)
	}
	enableWarning, ok := envelope.Error.Details["EnableWarning"].(string)
	if !ok || enableWarning != enableTimePatchWarning {
		t.Fatalf("Details.EnableWarning mismatch: %#v", envelope.Error.Details["EnableWarning"])
	}
	if warning, exists := envelope.Error.Details["Warning"]; exists {
		if warningText, isString := warning.(string); isString && strings.Contains(warningText, enableTimePatchWarning) {
			t.Fatalf("enable-time warning must stay on EnableWarning, not Details.Warning: %#v", warning)
		}
	}
}
