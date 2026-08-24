package projectrunner

import (
	"bytes"
	"context"
	"strings"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies await on an already-hit continuous marker ignores the baseline snapshot and returns
// only after LastHitSequence advances.
func TestWaitForPausePointWaitsForNewHitOnAlreadyHitContinuousMarker(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
	}()

	queryCount := 0
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		queryCount++
		sequence := 5
		if queryCount >= 2 {
			sequence = 6
		}
		return pausePointStatusResponse{
			Id:              id,
			Status:          pausePointStatusHit,
			IsHit:           true,
			HitCount:        sequence,
			Mode:            pausePointModeContinuous,
			LastHitSequence: sequence,
			EditorState:     pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}

	response, state, _, _, hasNewHitBaseline, err := waitForPausePoint(
		context.Background(),
		unityipc.Connection{},
		waitForPausePointOptions{
			id:             "jump",
			timeoutSeconds: 1,
			timeout:        time.Second,
		},
	)
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateHit {
		t.Fatalf("state mismatch: %s", state)
	}
	if !hasNewHitBaseline {
		t.Fatal("expected a new-hit baseline for an already-hit continuous marker")
	}
	if response.LastHitSequence != 6 {
		t.Fatalf("expected the advanced hit sequence, got %#v", response)
	}
	if queryCount < 2 {
		t.Fatalf("expected at least two status polls, got %d", queryCount)
	}
}

// Verifies await on an already-hit continuous marker times out with PAUSE_POINT_WAIT_TIMEOUT and
// the already-hit baseline hint when LastHitSequence never advances, without clearing the still-armed
// marker (the hint tells the caller to await again).
func TestWaitForPausePointTimesOutWaitingForNewHitOnContinuousMarker(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalClear := clearPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		clearPausePointStatus = originalClear
		pausePointStatusPoll = originalPoll
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:              id,
			Status:          pausePointStatusHit,
			IsHit:           true,
			HitCount:        5,
			Mode:            pausePointModeContinuous,
			LastHitSequence: 5,
			EditorState:     pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}
	clearCalls := 0
	clearPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		clearCalls++
		t.Fatal("timeout while waiting for a new hit must not clear the still-armed continuous marker")
		return pausePointStatusResponse{}, nil
	}

	stderr := &bytes.Buffer{}
	exitCode := runWaitForPausePoint(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		waitForPausePointOptions{
			id:             "jump",
			timeoutSeconds: 1,
			timeout:        40 * time.Millisecond,
		},
		&bytes.Buffer{},
		stderr,
	)
	if exitCode != 1 {
		t.Fatalf("exit code mismatch: %d", exitCode)
	}
	if clearCalls != 0 {
		t.Fatalf("expected clear not to run, got %d calls", clearCalls)
	}
	stderrText := stderr.String()
	if !strings.Contains(stderrText, clierrors.ErrorCodePausePointWaitTimeout) {
		t.Fatalf("expected %s, got stderr: %s", clierrors.ErrorCodePausePointWaitTimeout, stderrText)
	}
	if !strings.Contains(stderrText, pausePointHintAlreadyHitWaitingForNew) {
		t.Fatalf("expected already-hit baseline hint, got stderr: %s", stderrText)
	}
	if strings.Contains(stderrText, clierrors.ErrorCodePausePointExpired) {
		t.Fatalf("timeout must not be reclassified as expired: %s", stderrText)
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	if _, exists := envelope.Error.Details["MarkerClearedByThisCommand"]; exists {
		t.Fatalf("new-hit baseline timeout must not claim this command cleared the marker: %#v", envelope.Error.Details)
	}
}

// Verifies a transient arm-query failure does not decide "no baseline", so a later stale continuous
// Hit is not returned as an immediate wait success.
func TestWaitForPausePointBaseliningSurvivesTransientArmQueryFailure(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalClear := clearPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		clearPausePointStatus = originalClear
		pausePointStatusPoll = originalPoll
	}()

	queryCount := 0
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		queryCount++
		if queryCount == 1 {
			return pausePointStatusResponse{}, context.DeadlineExceeded
		}
		return pausePointStatusResponse{
			Id:              id,
			Status:          pausePointStatusHit,
			IsHit:           true,
			HitCount:        5,
			Mode:            pausePointModeContinuous,
			LastHitSequence: 5,
			EditorState:     pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}
	clearPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		t.Fatal("baseline timeout must not clear the still-armed continuous marker")
		return pausePointStatusResponse{}, nil
	}

	stderr := &bytes.Buffer{}
	exitCode := runWaitForPausePoint(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		waitForPausePointOptions{
			id:             "jump",
			timeoutSeconds: 1,
			timeout:        40 * time.Millisecond,
			resumePlay:     true,
		},
		&bytes.Buffer{},
		stderr,
	)
	if exitCode != 1 {
		t.Fatalf("exit code mismatch: %d", exitCode)
	}
	stderrText := stderr.String()
	if !strings.Contains(stderrText, clierrors.ErrorCodePausePointWaitTimeout) {
		t.Fatalf("expected %s after a stale continuous Hit, got stderr: %s", clierrors.ErrorCodePausePointWaitTimeout, stderrText)
	}
	if !strings.Contains(stderrText, pausePointHintAlreadyHitWaitingForNew) {
		t.Fatalf("expected already-hit baseline hint, got stderr: %s", stderrText)
	}
}

// Verifies enable --await never baselining a Hit that raced in before the first status query.
func TestWaitForPausePointAcceptsImmediateHitWhenMarkerJustEnabled(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Hour
	defer func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
	}()

	queryCount := 0
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		queryCount++
		return pausePointStatusResponse{
			Id:              id,
			Status:          pausePointStatusHit,
			IsHit:           true,
			HitCount:        1,
			Mode:            pausePointModeContinuous,
			LastHitSequence: 1,
			EditorState:     pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}

	response, state, _, _, hasNewHitBaseline, err := waitForPausePoint(
		context.Background(),
		unityipc.Connection{},
		waitForPausePointOptions{
			id:                "jump",
			timeoutSeconds:    1,
			timeout:           time.Second,
			markerJustEnabled: true,
		},
	)
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateHit {
		t.Fatalf("state mismatch: %s", state)
	}
	if hasNewHitBaseline {
		t.Fatal("enable --await must not establish a new-hit baseline")
	}
	if response.LastHitSequence != 1 {
		t.Fatalf("expected the raced enable-time hit, got %#v", response)
	}
	if queryCount != 1 {
		t.Fatalf("expected a single status query, got %d", queryCount)
	}
}

// Verifies an already-hit single-shot marker still returns immediately (baseline not applied).
func TestWaitForPausePointReturnsImmediatelyForAlreadyHitSingleShotMarker(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Hour
	defer func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
	}()

	queryCount := 0
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		queryCount++
		return pausePointStatusResponse{
			Id:              id,
			Status:          pausePointStatusHit,
			IsHit:           true,
			HitCount:        1,
			Mode:            "single-shot",
			LastHitSequence: 1,
			EditorState:     pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}

	response, state, _, _, hasNewHitBaseline, err := waitForPausePoint(
		context.Background(),
		unityipc.Connection{},
		waitForPausePointOptions{
			id:             "jump",
			timeoutSeconds: 1,
			timeout:        time.Second,
		},
	)
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateHit {
		t.Fatalf("state mismatch: %s", state)
	}
	if hasNewHitBaseline {
		t.Fatal("single-shot must not establish a new-hit baseline")
	}
	if response.LastHitSequence != 1 {
		t.Fatalf("response mismatch: %#v", response)
	}
	if queryCount != 1 {
		t.Fatalf("expected a single status query, got %d", queryCount)
	}
}

// Verifies the timeout hint for an already-hit baseline is distinct from the generic paused hint.
func TestPausePointTimeoutHintForNewHitBaseline(t *testing.T) {
	response := pausePointStatusResponse{
		Id:              "jump",
		Status:          pausePointStatusHit,
		Mode:            pausePointModeContinuous,
		LastHitSequence: 5,
		EditorState:     pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		HitCount:        5,
	}
	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, response, pausePointWaitStateTimeout, true, false, nil)

	if cliErr.ErrorCode != clierrors.ErrorCodePausePointWaitTimeout {
		t.Fatalf("error code mismatch: %s", cliErr.ErrorCode)
	}
	if cliErr.Details["Hint"] != pausePointHintAlreadyHitWaitingForNew {
		t.Fatalf("hint mismatch: %#v", cliErr.Details)
	}
}
