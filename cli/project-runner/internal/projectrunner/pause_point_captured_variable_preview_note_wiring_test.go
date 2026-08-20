package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

func stubPausePointStatusHitWithTruncatedCapturedVariable(t *testing.T) {
	t.Helper()

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
			Success:     true,
			Id:          id,
			Status:      pausePointStatusHit,
			IsEnabled:   true,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
			CapturedVariables: []pausePointCapturedVariable{
				{
					Name:      "board",
					Scope:     "Local",
					TypeName:  "System.Int32[,]",
					Truncated: true,
				},
			},
		}, nil
	}
}

func capturedVariablePreviewNoteFromStdout(t *testing.T, output string) string {
	t.Helper()

	var decoded map[string]json.RawMessage
	if err := json.Unmarshal([]byte(output), &decoded); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, output)
	}

	rawNote, ok := decoded["CapturedVariablePreviewNote"]
	if !ok {
		t.Fatalf("CapturedVariablePreviewNote missing from JSON: %s", output)
	}

	var note string
	if err := json.Unmarshal(rawNote, &note); err != nil {
		t.Fatalf("unmarshal CapturedVariablePreviewNote failed: %v from %s", err, output)
	}
	return note
}

func assertStdoutCapturedVariablePreviewNote(t *testing.T, output string) {
	t.Helper()

	note := capturedVariablePreviewNoteFromStdout(t, output)
	if note != wantCapturedVariablePreviewNote {
		t.Fatalf("CapturedVariablePreviewNote mismatch: got %#v, want %#v", note, wantCapturedVariablePreviewNote)
	}
}

// Verifies pause-point-status stdout includes CapturedVariablePreviewNote when a remaining
// captured variable is truncated, so dropping the status-command apply call fails this test.
func TestRunPausePointStatusCommandIncludesCapturedVariablePreviewNote(t *testing.T) {
	stubPausePointStatusHitWithTruncatedCapturedVariable(t)

	code, output := runPausePointStatusForExpect(t, []string{"--id", "jump"})

	if code != 0 {
		t.Fatalf("expected success, got %d: %s", code, output)
	}
	assertStdoutCapturedVariablePreviewNote(t, output)
}

// Verifies await-pause-point stdout includes CapturedVariablePreviewNote on a truncated hit, so
// dropping the plain-await apply call fails this test.
func TestRunWaitForPausePointIncludesCapturedVariablePreviewNote(t *testing.T) {
	stubPausePointStatusHitWithTruncatedCapturedVariable(t)
	stubPausePointMatchingLogs(t, nil)

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		waitForPausePointOptions{
			id:             "jump",
			timeoutSeconds: 1,
			timeout:        time.Second,
		},
		&stdout,
		&stderr)
	if stderr.Len() > 0 {
		t.Logf("stderr: %s", stderr.String())
	}
	if code != 0 {
		t.Fatalf("expected success, got %d: %s", code, stdout.String())
	}
	assertStdoutCapturedVariablePreviewNote(t, stdout.String())
}

// Verifies enable-pause-point --await stdout includes CapturedVariablePreviewNote on a truncated
// hit, so dropping the enable-await apply call fails this test.
func TestRunPausePointWaitAfterEnableIncludesCapturedVariablePreviewNote(t *testing.T) {
	stubPausePointStatusHitWithTruncatedCapturedVariable(t)
	stubPausePointMatchingLogs(t, nil)
	stubPausePointTriggerDispatch(t, `{"Success":true}`)

	code, output := runEnableAwaitWithStubbedTrigger(t, "")

	if code != 0 {
		t.Fatalf("expected success, got %d: %s", code, output)
	}
	assertStdoutCapturedVariablePreviewNote(t, output)
}
