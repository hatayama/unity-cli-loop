package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies a successful extend request writes no warning to stderr.
func TestExtendPausePointExpiryBeforeWaitWritesNothingOnSuccess(t *testing.T) {
	originalExtend := extendPausePointExpiry
	defer func() { extendPausePointExpiry = originalExtend }()

	extendPausePointExpiry = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
		minimumRemainingSeconds int,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Id: id, Status: pausePointStatusEnabled}, nil
	}

	var stderr bytes.Buffer
	extendPausePointExpiryBeforeWait(
		context.Background(),
		unityipc.Connection{},
		waitForPausePointOptions{id: "jump", timeoutSeconds: 30},
		&stderr)

	if stderr.Len() != 0 {
		t.Fatalf("expected no warning, got: %s", stderr.String())
	}
}

// Verifies a failed extend request is best-effort: it writes a warning but never blocks the wait.
func TestExtendPausePointExpiryBeforeWaitWritesWarningOnFailure(t *testing.T) {
	originalExtend := extendPausePointExpiry
	defer func() { extendPausePointExpiry = originalExtend }()

	extendPausePointExpiry = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
		minimumRemainingSeconds int,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{}, errors.New("unknown internal bridge command")
	}

	var stderr bytes.Buffer
	extendPausePointExpiryBeforeWait(
		context.Background(),
		unityipc.Connection{},
		waitForPausePointOptions{id: "jump", timeoutSeconds: 30},
		&stderr)

	if !strings.Contains(stderr.String(), "jump") || !strings.Contains(stderr.String(), "unknown internal bridge command") {
		t.Fatalf("expected a warning mentioning the id and cause, got: %s", stderr.String())
	}
}

// Verifies the await-pause-point command extends the marker's expiry to its own timeout before
// the first status poll, so a slow multi-step CLI round trip cannot let the marker expire first.
func TestRunWaitForPausePointCommandExtendsExpiryBeforePolling(t *testing.T) {
	originalExtend := extendPausePointExpiry
	originalQuery := queryPausePointStatus
	defer func() {
		extendPausePointExpiry = originalExtend
		queryPausePointStatus = originalQuery
	}()

	var callOrder []string
	var extendedID string
	var extendedMinimumRemainingSeconds int
	extendPausePointExpiry = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
		minimumRemainingSeconds int,
	) (pausePointStatusResponse, error) {
		callOrder = append(callOrder, "extend")
		extendedID = id
		extendedMinimumRemainingSeconds = minimumRemainingSeconds
		return pausePointStatusResponse{Id: id, Status: pausePointStatusEnabled}, nil
	}
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		callOrder = append(callOrder, "query")
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePointCommand(
		context.Background(),
		unityipc.Connection{},
		[]string{"--id", "jump", "--timeout-seconds", "7"},
		"",
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	if len(callOrder) == 0 || callOrder[0] != "extend" {
		t.Fatalf("expected extend to run before the first status query, got order: %v", callOrder)
	}
	if extendedID != "jump" {
		t.Fatalf("extended id mismatch: %s", extendedID)
	}
	if extendedMinimumRemainingSeconds != 7 {
		t.Fatalf("extended minimum remaining seconds mismatch: %d", extendedMinimumRemainingSeconds)
	}
}

// Verifies await-pause-point prints the wait-start line to stderr immediately (before the
// first status poll) and keeps stdout a single JSON object.
func TestRunWaitForPausePointCommandAnnouncesWaitStartOnStderr(t *testing.T) {
	originalExtend := extendPausePointExpiry
	originalQuery := queryPausePointStatus
	defer func() {
		extendPausePointExpiry = originalExtend
		queryPausePointStatus = originalQuery
	}()

	const wantAnnounce = "Waiting for pause point jump (up to 7s). The JSON response prints only when the wait ends. If this output gets cut off before then, read the outcome with: uloop pause-point-status --id \"jump\"\n"
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	statusCallCount := 0

	extendPausePointExpiry = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
		minimumRemainingSeconds int,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Id: id, Status: pausePointStatusEnabled}, nil
	}
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		if statusCallCount == 0 && stderr.String() != wantAnnounce {
			t.Fatalf("announce must appear before the first status poll:\nwant %q\ngot  %q", wantAnnounce, stderr.String())
		}
		statusCallCount++
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}

	code := runWaitForPausePointCommand(
		context.Background(),
		unityipc.Connection{},
		[]string{"--id", "jump", "--timeout-seconds", "7"},
		"",
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	if stderr.String() != wantAnnounce {
		t.Fatalf("stderr mismatch:\nwant %q\ngot  %q", wantAnnounce, stderr.String())
	}
	assertStdoutIsSingleJSONObject(t, stdout.Bytes())

	var response pausePointWaitResult
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("failed to decode stdout: %v\n%s", err, stdout.String())
	}
	if response.Status != pausePointStatusHit || response.HitCount != 1 {
		t.Fatalf("response mismatch: %#v", response)
	}
}

// Verifies await-pause-point quotes a whitespace-containing marker id in the recovery
// command so the stderr line stays copy-pasteable.
func TestRunWaitForPausePointCommandQuotesWhitespaceIdOnStderr(t *testing.T) {
	originalExtend := extendPausePointExpiry
	originalQuery := queryPausePointStatus
	defer func() {
		extendPausePointExpiry = originalExtend
		queryPausePointStatus = originalQuery
	}()

	const wantAnnounce = "Waiting for pause point Assets/My Folder/Foo.cs:42 (up to 7s). The JSON response prints only when the wait ends. If this output gets cut off before then, read the outcome with: uloop pause-point-status --id \"Assets/My Folder/Foo.cs:42\"\n"
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	statusCallCount := 0

	extendPausePointExpiry = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
		minimumRemainingSeconds int,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Id: id, Status: pausePointStatusEnabled}, nil
	}
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		if statusCallCount == 0 && stderr.String() != wantAnnounce {
			t.Fatalf("announce must appear before the first status poll:\nwant %q\ngot  %q", wantAnnounce, stderr.String())
		}
		statusCallCount++
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}

	code := runWaitForPausePointCommand(
		context.Background(),
		unityipc.Connection{},
		[]string{"--id", "Assets/My Folder/Foo.cs:42", "--timeout-seconds", "7"},
		"",
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	if stderr.String() != wantAnnounce {
		t.Fatalf("stderr mismatch:\nwant %q\ngot  %q", wantAnnounce, stderr.String())
	}
	assertStdoutIsSingleJSONObject(t, stdout.Bytes())
}

// Verifies await-pause-point polls until Unity reports the marker hit.
func TestWaitForPausePointReturnsHitAfterEnabledStatus(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
	}()

	responses := []pausePointStatusResponse{
		{Id: "jump", Status: pausePointStatusEnabled, IsEnabled: true},
		{
			Id:                      "jump",
			Status:                  pausePointStatusHit,
			IsHit:                   true,
			HitCount:                1,
			Mode:                    "continuous",
			MaxHistory:              20,
			MaxPreviewElements:      15,
			CapturedVariableHistory: []pausePointCapturedHistoryFrame{{HitSequence: 1, FrameCount: 42, HitAtUtc: "2026-06-03T00:00:01.0000000Z"}},
			HistoryDroppedCount:     0,
			EditorState:             pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		},
	}
	requestCount := 0
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		if id != "jump" {
			t.Fatalf("id mismatch: %s", id)
		}
		response := responses[requestCount]
		requestCount++
		return response, nil
	}

	response, state, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
	})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateHit {
		t.Fatalf("state mismatch: %s", state)
	}
	if response.Status != pausePointStatusHit || response.HitCount != 1 {
		t.Fatalf("response mismatch: %#v", response)
	}
	if response.Mode != "continuous" || response.MaxHistory != 20 || response.MaxPreviewElements != 15 || len(response.CapturedVariableHistory) != 1 {
		t.Fatalf("capture history mismatch: %#v", response)
	}
	if requestCount != 2 {
		t.Fatalf("request count mismatch: %d", requestCount)
	}
}

// Verifies await-pause-point clears the enabled marker after its own timeout.
func TestRunWaitForPausePointClearsEnabledMarkerAfterTimeout(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalClear := clearPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		clearPausePointStatus = originalClear
		pausePointStatusPoll = originalPoll
	}()

	cleared := false
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		if cleared {
			return pausePointStatusResponse{
				Id:                              id,
				Status:                          pausePointStatusCleared,
				ClearedReason:                   pausePointAwaitTimeoutAutoClearReason,
				StatusBeforeClear:               pausePointStatusEnabled,
				TimeoutSeconds:                  1,
				ElapsedSinceEnabledMilliseconds: 100,
				EditorState:                     pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
				Message:                         "Pause point cleared.",
			}, nil
		}
		return pausePointStatusResponse{
			Id:                              id,
			Status:                          pausePointStatusEnabled,
			IsEnabled:                       true,
			TimeoutSeconds:                  1,
			ElapsedSinceEnabledMilliseconds: 100,
			EditorState:                     pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
			Message:                         "Pause point enabled.",
		}, nil
	}

	clearedID := ""
	clearPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		cleared = true
		clearedID = id
		return pausePointStatusResponse{Id: id, Status: pausePointStatusCleared}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        5 * time.Millisecond,
	}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected failure, got %d with stdout %s", code, stdout.String())
	}
	if clearedID != "jump" {
		t.Fatalf("cleared id mismatch: %s", clearedID)
	}
	if !strings.Contains(stderr.String(), clierrors.ErrorCodePausePointWaitTimeout) {
		t.Fatalf("timeout error missing from stderr: %s", stderr.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	if envelope.Error.Details["Status"] != pausePointStatusCleared {
		t.Fatalf("status detail mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["MarkerClearedByThisCommand"] != true {
		t.Fatalf("MarkerClearedByThisCommand mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["ClearedReason"] != pausePointAwaitTimeoutAutoClearReason {
		t.Fatalf("ClearedReason mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["StatusBeforeClear"] != pausePointStatusEnabled {
		t.Fatalf("StatusBeforeClear mismatch: %#v", envelope.Error.Details)
	}
	wantHint := pausePointHintTimeoutAutoCleared + pausePointNonFiringPatternsHint
	if envelope.Error.Details["Hint"] != wantHint {
		t.Fatalf("hint mismatch: %#v", envelope.Error.Details["Hint"])
	}
}

// Verifies a failed post-clear status re-read keeps the pre-clear snapshot but still reports
// MarkerClearedByThisCommand so the timeout envelope does not hide that this command disarmed it.
func TestRunWaitForPausePointTimeoutAutoClearKeepsPreviousSnapshotWhenRereadFails(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalClear := clearPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		clearPausePointStatus = originalClear
		pausePointStatusPoll = originalPoll
	}()

	cleared := false
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		if cleared {
			return pausePointStatusResponse{}, errors.New("pause point status query failed after clear")
		}
		return pausePointStatusResponse{
			Id:                              id,
			Status:                          pausePointStatusEnabled,
			IsEnabled:                       true,
			TimeoutSeconds:                  1,
			ElapsedSinceEnabledMilliseconds: 100,
			EditorState:                     pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
			Message:                         "Pause point enabled.",
		}, nil
	}

	clearPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		cleared = true
		return pausePointStatusResponse{Id: id, Status: pausePointStatusCleared}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        5 * time.Millisecond,
	}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected failure, got %d with stdout %s", code, stdout.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	if envelope.Error.Details["Status"] != pausePointStatusEnabled {
		t.Fatalf("fallback status mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["MarkerClearedByThisCommand"] != true {
		t.Fatalf("MarkerClearedByThisCommand mismatch: %#v", envelope.Error.Details)
	}
	wantHint := pausePointHintTimeoutAutoCleared + pausePointNonFiringPatternsHint
	if envelope.Error.Details["Hint"] != wantHint {
		t.Fatalf("hint mismatch: %#v", envelope.Error.Details["Hint"])
	}
}

// Verifies a failed timeout auto-clear does not claim this command disarmed the marker, because
// the marker is still armed and the conventional wait-again hint is the truthful recovery.
func TestRunWaitForPausePointTimeoutDoesNotClaimClearWhenClearIpcFails(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalClear := clearPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		clearPausePointStatus = originalClear
		pausePointStatusPoll = originalPoll
	}()

	enabledResponse := pausePointStatusResponse{
		Id:                              "jump",
		Status:                          pausePointStatusEnabled,
		IsEnabled:                       true,
		TimeoutSeconds:                  1,
		ElapsedSinceEnabledMilliseconds: 100,
		EditorState:                     pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		Message:                         "Pause point enabled.",
	}
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return enabledResponse, nil
	}
	clearPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{}, errors.New("pause point clear failed")
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        5 * time.Millisecond,
	}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected failure, got %d with stdout %s", code, stdout.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	if envelope.Error.Details["Status"] != pausePointStatusEnabled {
		t.Fatalf("status detail mismatch: %#v", envelope.Error.Details)
	}
	if _, exists := envelope.Error.Details["MarkerClearedByThisCommand"]; exists {
		t.Fatalf("failed clear must not claim this command cleared the marker: %#v", envelope.Error.Details)
	}
	wantHint := pausePointTimeoutHint(enabledResponse, false, false, nil)
	if envelope.Error.Details["Hint"] != wantHint {
		t.Fatalf("hint mismatch:\n got: %#v\nwant: %#v", envelope.Error.Details["Hint"], wantHint)
	}
}

// Verifies pause point wait errors expose recovery and generation details from Unity.
func TestPausePointExpiredErrorReportsRecoveryFields(t *testing.T) {
	response := pausePointStatusResponse{
		Id:                              "jump",
		Status:                          pausePointStatusExpired,
		Expired:                         true,
		TimeoutSeconds:                  1,
		EnabledAtUtc:                    "2026-06-03T00:00:00.0000000Z",
		ElapsedSinceEnabledMilliseconds: 1200,
		RemainingMilliseconds:           0,
		Generation:                      7,
		EditorState:                     pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		Message:                         "Pause point expired before it was hit.",
		RecommendedNextAction:           "Re-enable the marker with a longer --timeout-seconds and trigger the code path again; clearing the expired marker first is not required.",
	}

	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, response, pausePointWaitStateExpired, false, false, nil)

	if cliErr.Details["Expired"] != true {
		t.Fatalf("expired detail mismatch: %#v", cliErr.Details)
	}
	if cliErr.Details["EnabledAtUtc"] != "2026-06-03T00:00:00.0000000Z" {
		t.Fatalf("enabledAtUtc detail mismatch: %#v", cliErr.Details)
	}
	if cliErr.Details["Generation"] != 7 {
		t.Fatalf("generation detail mismatch: %#v", cliErr.Details)
	}
	if cliErr.Details["RecommendedNextAction"] != response.RecommendedNextAction {
		t.Fatalf("recommendedNextAction detail mismatch: %#v", cliErr.Details)
	}
}

// Verifies an Expired error prepends Unity's RecommendedNextAction onto NextActions
// so the recovery hint is visible without opening Details.
func TestPausePointExpiredErrorPrependsRecommendedNextAction(t *testing.T) {
	const expiredNextAction = "Re-enable the marker with a longer --timeout-seconds and trigger the code path again; clearing the expired marker first is not required."
	response := pausePointStatusResponse{
		Id:                    "jump",
		Status:                pausePointStatusExpired,
		Expired:               true,
		EditorState:           pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		Message:               "Pause point expired before it was hit.",
		RecommendedNextAction: expiredNextAction,
	}

	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, response, pausePointWaitStateExpired, false, false, nil)

	wantNextActions := []string{
		expiredNextAction,
		"Run `uloop enable-pause-point --id <marker-id>` before waiting.",
		"Confirm the code path calls `UloopPausePoint.Pause(\"<marker-id>\")` with the same id.",
		"Check `Details.Status`, `Details.EditorState`, `Details.ElapsedSinceEnabledMilliseconds`, and `Details.RemainingMilliseconds` to distinguish a missed code path from an already-paused Editor.",
		"If the marker is inside a custom asmdef, add a reference to `UnityCLILoop.PausePoints.Runtime`.",
	}
	if !reflect.DeepEqual(cliErr.NextActions, wantNextActions) {
		t.Fatalf("NextActions mismatch:\n got: %#v\nwant: %#v",
			cliErr.NextActions, wantNextActions)
	}
}

// Verifies skipped conditional hits suppress the resolved-line guidance that
// would otherwise contradict the expired hint by claiming the line never ran.
func TestPausePointExpiredErrorWithSkippedHitsOmitsNeverExecutedGuidance(t *testing.T) {
	response := pausePointStatusResponse{
		Id:                  "Assets/Scripts/Foo.cs:72",
		Status:              pausePointStatusExpired,
		HitWhen:             "speed > 5",
		HitWhenSkippedCount: 3,
		ResolvedLine:        72,
		EditorState:         pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
	}

	cliErr := pausePointWaitError("/tmp/project", waitForPausePointOptions{
		id:             response.Id,
		timeoutSeconds: 30,
	}, response, pausePointWaitStateExpired, false, false, nil)

	if cliErr.Message != "Pause point expired before it was hit." {
		t.Fatalf("Message mismatch: got %#v, want %#v", cliErr.Message, "Pause point expired before it was hit.")
	}
	if cliErr.Details["Hint"] != "The marker expired after its line executed, but no hit matched --hit-when. Re-enable it with a longer --timeout-seconds, then adjust the --hit-when condition or trigger input so a hit matches." {
		t.Fatalf("Hint mismatch: got %#v", cliErr.Details["Hint"])
	}
}

// Verifies an empty RecommendedNextAction does not prepend a blank NextActions entry.
func TestPausePointExpiredErrorOmitsEmptyRecommendedNextAction(t *testing.T) {
	response := pausePointStatusResponse{
		Id:          "jump",
		Status:      pausePointStatusExpired,
		Expired:     true,
		EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		Message:     "Pause point expired before it was hit.",
	}

	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, response, pausePointWaitStateExpired, false, false, nil)

	wantNextActions := []string{
		"Run `uloop enable-pause-point --id <marker-id>` before waiting.",
		"Confirm the code path calls `UloopPausePoint.Pause(\"<marker-id>\")` with the same id.",
		"Check `Details.Status`, `Details.EditorState`, `Details.ElapsedSinceEnabledMilliseconds`, and `Details.RemainingMilliseconds` to distinguish a missed code path from an already-paused Editor.",
		"If the marker is inside a custom asmdef, add a reference to `UnityCLILoop.PausePoints.Runtime`.",
	}
	if !reflect.DeepEqual(cliErr.NextActions, wantNextActions) {
		t.Fatalf("NextActions mismatch:\n got: %#v\nwant: %#v",
			cliErr.NextActions, wantNextActions)
	}
}

// Verifies an Expired error for a file:line id uses --file/--line NextActions, not Pause(...) wording.
func TestPausePointExpiredErrorUsesFileLineNextActions(t *testing.T) {
	response := pausePointStatusResponse{
		Id:          "Assets/Scripts/Foo.cs:42",
		Status:      pausePointStatusExpired,
		Expired:     true,
		EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		Message:     "Pause point expired before it was hit.",
	}

	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "Assets/Scripts/Foo.cs:42",
		timeoutSeconds: 1,
	}, response, pausePointWaitStateExpired, false, false, nil)

	wantNextActions := []string{
		"Re-arm it with uloop enable-pause-point --file \"Assets/Scripts/Foo.cs\" --line 42 before waiting.",
		"Confirm the code path executes line 42 of Assets/Scripts/Foo.cs while the marker is armed.",
		"Check `Details.Status`, `Details.EditorState`, `Details.ElapsedSinceEnabledMilliseconds`, and `Details.RemainingMilliseconds` to distinguish a missed code path from an already-paused Editor.",
		"If the marker is inside a custom asmdef, add a reference to `UnityCLILoop.PausePoints.Runtime`.",
	}
	if !reflect.DeepEqual(cliErr.NextActions, wantNextActions) {
		t.Fatalf("NextActions mismatch:\n got: %#v\nwant: %#v",
			cliErr.NextActions, wantNextActions)
	}
}

// Verifies recovery details use the marker lifetime instead of the wait deadline.
func TestPausePointExpiredErrorReportsMarkerTimeoutSeconds(t *testing.T) {
	response := pausePointStatusResponse{
		Id:             "jump",
		Status:         pausePointStatusExpired,
		TimeoutSeconds: 30,
		EditorState:    pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		Message:        "Pause point expired before it was hit.",
	}

	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 5,
	}, response, pausePointWaitStateExpired, false, false, nil)

	if cliErr.Details["TimeoutSeconds"] != 30 {
		t.Fatalf("timeoutSeconds detail mismatch: %#v", cliErr.Details)
	}
}

// Verifies wait errors derive Expired from Status when older Unity packages omit the bool field.
func TestPausePointExpiredErrorDerivesExpiredFromStatus(t *testing.T) {
	response := pausePointStatusResponse{
		Id:          "jump",
		Status:      pausePointStatusExpired,
		EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		Message:     "Pause point expired before it was hit.",
	}

	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, response, pausePointWaitStateExpired, false, false, nil)

	if cliErr.Details["Expired"] != true {
		t.Fatalf("expired detail mismatch: %#v", cliErr.Details)
	}
}

// Verifies await-pause-point does one final status probe before treating timeout as missed.
func TestRunWaitForPausePointReturnsFinalHitAtTimeoutBoundary(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalClear := clearPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
		clearPausePointStatus = originalClear
	}()

	requestCount := 0
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		requestCount++
		if requestCount == 1 {
			return pausePointStatusResponse{
				Id:        id,
				Status:    pausePointStatusEnabled,
				IsEnabled: true,
			}, nil
		}
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}

	clearedID := ""
	clearPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		clearedID = id
		return pausePointStatusResponse{Id: id, Status: pausePointStatusCleared}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        5 * time.Millisecond,
	}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	if clearedID != "" {
		t.Fatalf("marker should not be cleared after final hit: %s", clearedID)
	}
	var response pausePointStatusResponse
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout.String())
	}
	if response.Status != pausePointStatusHit || response.HitCount != 1 {
		t.Fatalf("response mismatch: %#v", response)
	}
	if requestCount != 2 {
		t.Fatalf("request count mismatch: %d", requestCount)
	}
}

// Verifies await-pause-point rejects calls before the marker is enabled.
func TestWaitForPausePointReturnsNotEnabledStateImmediately(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusNotEnabled,
			EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		}, nil
	}

	response, state, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
	})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateNotEnabled {
		t.Fatalf("state mismatch: %s", state)
	}
	if response.Status != pausePointStatusNotEnabled {
		t.Fatalf("response mismatch: %#v", response)
	}
}

// Verifies not-enabled failures use the user-facing enabled terminology.
func TestRunWaitForPausePointReportsNotEnabledError(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusNotEnabled,
			EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
			Message:     "Pause point is not enabled.",
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
	}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected failure, got %d with stdout %s", code, stdout.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	if envelope.Error.ErrorCode != clierrors.ErrorCodePausePointNotEnabled {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Details["Status"] != pausePointStatusNotEnabled {
		t.Fatalf("status detail mismatch: %#v", envelope.Error.Details)
	}
	editorState, ok := envelope.Error.Details["EditorState"].(map[string]any)
	if !ok || editorState["IsPlaying"] != true || editorState["IsPaused"] != false || editorState["CapturedAt"] != "Current" {
		t.Fatalf("editorState details mismatch: %#v", envelope.Error.Details)
	}
}

// Verifies the matching-log count flag parses with a safe default and rejects non-positive counts.
func TestParseWaitForPausePointOptionsParsesMatchingLogFlags(t *testing.T) {
	defaults, err := parseWaitForPausePointOptions([]string{"--id", "jump"})
	if err != nil {
		t.Fatalf("default parse failed: %v", err)
	}
	if defaults.matchingLogsMaxCount != pausePointDefaultLogsMaxCount {
		t.Fatalf("default matching-log options mismatch: %#v", defaults)
	}

	options, err := parseWaitForPausePointOptions([]string{
		"--id", "jump", "--matching-logs-max-count", "5",
	})
	if err != nil {
		t.Fatalf("parse failed: %v", err)
	}
	if options.matchingLogsMaxCount != 5 {
		t.Fatalf("matching-log options mismatch: %#v", options)
	}

	if _, err := parseWaitForPausePointOptions([]string{"--id", "jump", "--matching-logs-max-count", "0"}); err == nil {
		t.Fatalf("expected error for non-positive max count")
	}

	// Log embedding is always on, so the retired opt-in flag must be rejected as unknown.
	if _, err := parseWaitForPausePointOptions([]string{"--id", "jump", "--include-matching-logs"}); err == nil {
		t.Fatalf("expected error for the retired include flag")
	}
}

// Verifies --captured-variables defaults to full, accepts "names", and rejects other values,
// for both await-pause-point and pause-point-status option parsing.
func TestParsePausePointCapturedVariablesModeFlag(t *testing.T) {
	waitDefaults, err := parseWaitForPausePointOptions([]string{"--id", "jump"})
	if err != nil {
		t.Fatalf("default parse failed: %v", err)
	}
	if waitDefaults.capturedVariablesMode != pausePointCapturedVariablesModeFull {
		t.Fatalf("default captured-variables mode mismatch: %#v", waitDefaults)
	}

	waitNames, err := parseWaitForPausePointOptions([]string{"--id", "jump", "--captured-variables", "names"})
	if err != nil {
		t.Fatalf("names parse failed: %v", err)
	}
	if waitNames.capturedVariablesMode != pausePointCapturedVariablesModeNames {
		t.Fatalf("names captured-variables mode mismatch: %#v", waitNames)
	}

	if _, err := parseWaitForPausePointOptions([]string{"--id", "jump", "--captured-variables", "bogus"}); err == nil {
		t.Fatalf("expected error for invalid captured-variables value")
	}

	statusDefaults, err := parsePausePointStatusOptions([]string{"--id", "jump"})
	if err != nil {
		t.Fatalf("default status parse failed: %v", err)
	}
	if statusDefaults.capturedVariablesMode != pausePointCapturedVariablesModeFull {
		t.Fatalf("default status captured-variables mode mismatch: %#v", statusDefaults)
	}

	statusNames, err := parsePausePointStatusOptions([]string{"--id", "jump", "--captured-variables", "names"})
	if err != nil {
		t.Fatalf("names status parse failed: %v", err)
	}
	if statusNames.capturedVariablesMode != pausePointCapturedVariablesModeNames {
		t.Fatalf("names status captured-variables mode mismatch: %#v", statusNames)
	}

	if _, err := parsePausePointStatusOptions([]string{"--id", "jump", "--captured-variables", "bogus"}); err == nil {
		t.Fatalf("expected error for invalid status captured-variables value")
	}
}

// Verifies --expect is parsed repeatably and rejects a value with no "=".
func TestParseWaitForPausePointOptionsParsesExpectFlag(t *testing.T) {
	defaults, err := parseWaitForPausePointOptions([]string{"--id", "jump"})
	if err != nil {
		t.Fatalf("default parse failed: %v", err)
	}
	if defaults.expectations != nil {
		t.Fatalf("expected no expectations by default, got %#v", defaults.expectations)
	}

	options, err := parseWaitForPausePointOptions([]string{
		"--id", "jump", "--expect", "Health=100", "--expect", "Name=Enemy",
	})
	if err != nil {
		t.Fatalf("parse failed: %v", err)
	}
	if len(options.expectations) != 2 {
		t.Fatalf("expectations mismatch: %#v", options.expectations)
	}
	if options.expectations[0] != (pausePointExpectation{Name: "Health", Expected: "100"}) {
		t.Fatalf("expectation[0] mismatch: %#v", options.expectations[0])
	}
	if options.expectations[1] != (pausePointExpectation{Name: "Name", Expected: "Enemy"}) {
		t.Fatalf("expectation[1] mismatch: %#v", options.expectations[1])
	}

	if _, err := parseWaitForPausePointOptions([]string{"--id", "jump", "--expect", "NoEqualsSign"}); err == nil {
		t.Fatalf("expected error for --expect value without '='")
	}
}

// Verifies a hit response always embeds marker-matching logs.
func TestRunWaitForPausePointEmbedsMatchingLogsOnHit(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalFetch := fetchMatchingLogs
	defer func() {
		queryPausePointStatus = originalQuery
		fetchMatchingLogs = originalFetch
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:               id,
			Status:           pausePointStatusHit,
			IsHit:            true,
			HitCount:         1,
			Generation:       7,
			FirstHitAtUtc:    "2026-06-03T00:00:00.1250000Z",
			LastHitAtUtc:     "2026-06-03T00:00:00.1250000Z",
			FirstHitSequence: 3,
			LastHitSequence:  3,
			EditorState:      pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}

	fetchedSearchText := ""
	fetchedMaxCount := 0
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		fetchedSearchText = searchText
		fetchedMaxCount = maxCount
		return pausePointMatchingLogsResult{
			SearchText:        searchText,
			TotalCount:        2,
			DisplayedCount:    2,
			LogType:           "Error",
			MaxCount:          maxCount,
			IncludeStackTrace: true,
			Logs: []pausePointMatchingLog{
				{Type: "Error", Message: "[jump] velocity=4.2", StackTrace: "trace one"},
				{Type: "Error", Message: "[jump] grounded=false", StackTrace: "trace two"},
			},
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:                   "jump",
		timeoutSeconds:       1,
		timeout:              time.Second,
		matchingLogsMaxCount: 5,
	}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	if fetchedSearchText != "jump" || fetchedMaxCount != 5 {
		t.Fatalf("fetch arguments mismatch: %s %d", fetchedSearchText, fetchedMaxCount)
	}

	result := pausePointWaitResult{}
	if err := json.Unmarshal(stdout.Bytes(), &result); err != nil {
		t.Fatalf("stdout parse failed: %v from %s", err, stdout.String())
	}
	if len(result.MatchingLogs) != 2 || result.MatchingLogs[0].Message != "[jump] velocity=4.2" {
		t.Fatalf("matching logs mismatch: %#v", result.MatchingLogs)
	}
	if result.MatchingLogs[0].StackTrace != "trace one" {
		t.Fatalf("matching log stack trace mismatch: %#v", result.MatchingLogs)
	}
	if result.EditorState.CapturedAt != "PausePointHit" {
		t.Fatalf("editor state mismatch: %#v", result.EditorState)
	}
	if result.Generation != 7 || result.FirstHitSequence != 3 {
		t.Fatalf("pause point fields mismatch: %#v", result)
	}
	if !strings.Contains(result.Warning, "Multiple matching logs") {
		t.Fatalf("warning mismatch: %#v", result.Warning)
	}
}

// Verifies a successful fetch with zero matches yields an explicit empty MatchingLogs array,
// so agents can tell "no matching log appeared" apart from "log fetch failed" (field absent).
func TestRunWaitForPausePointEmbedsEmptyMatchingLogsWhenNoneMatch(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalFetch := fetchMatchingLogs
	defer func() {
		queryPausePointStatus = originalQuery
		fetchMatchingLogs = originalFetch
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{
			SearchText: searchText,
			MaxCount:   maxCount,
			Logs:       []pausePointMatchingLog{},
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:                   "jump",
		timeoutSeconds:       1,
		timeout:              time.Second,
		matchingLogsMaxCount: pausePointDefaultLogsMaxCount,
	}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	var result pausePointWaitResult
	if err := json.Unmarshal(stdout.Bytes(), &result); err != nil {
		t.Fatalf("stdout parse failed: %v from %s", err, stdout.String())
	}
	if result.MatchingLogs == nil || len(result.MatchingLogs) != 0 {
		t.Fatalf("MatchingLogs must be an explicit empty array: %#v", result.MatchingLogs)
	}
	if result.Warning != "" {
		t.Fatalf("warning should be empty when there are no matching logs: %#v", result.Warning)
	}
	if strings.Contains(stdout.String(), "Warning") {
		t.Fatalf("Warning must be omitted from JSON when empty: %s", stdout.String())
	}
}

// Verifies a log fetch failure never turns a successful hit into an error.
func TestRunWaitForPausePointIgnoresLogFetchFailure(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalFetch := fetchMatchingLogs
	defer func() {
		queryPausePointStatus = originalQuery
		fetchMatchingLogs = originalFetch
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{}, context.DeadlineExceeded
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:                   "jump",
		timeoutSeconds:       1,
		timeout:              time.Second,
		matchingLogsMaxCount: pausePointDefaultLogsMaxCount,
	}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("expected success despite log fetch failure, got %d with stderr %s", code, stderr.String())
	}
	if strings.Contains(stdout.String(), "MatchingLogs") {
		t.Fatalf("MatchingLogs must be omitted when the fetch fails: %s", stdout.String())
	}
	if strings.Contains(stdout.String(), "Warning") {
		t.Fatalf("Warning must be omitted when the fetch fails: %s", stdout.String())
	}
}

// Verifies timeout envelopes always embed marker-matching logs best-effort.
func TestRunWaitForPausePointEmbedsMatchingLogsOnTimeout(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalClear := clearPausePointStatus
	originalFetch := fetchMatchingLogs
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		clearPausePointStatus = originalClear
		fetchMatchingLogs = originalFetch
		pausePointStatusPoll = originalPoll
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusEnabled,
			IsEnabled:   true,
			EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		}, nil
	}
	clearPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Id: id, Status: pausePointStatusCleared}, nil
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{
			SearchText:     searchText,
			TotalCount:     3,
			DisplayedCount: 1,
			MaxCount:       maxCount,
			Logs:           []pausePointMatchingLog{{Type: "Log", Message: "[jump] never reached"}},
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:                   "jump",
		timeoutSeconds:       1,
		timeout:              5 * time.Millisecond,
		matchingLogsMaxCount: pausePointDefaultLogsMaxCount,
	}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected timeout failure, got %d with stdout %s", code, stdout.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	// The detail key mirrors the hit-response field name, so one spelling covers both surfaces.
	matchingLogs, ok := envelope.Error.Details["MatchingLogs"].([]any)
	if !ok || len(matchingLogs) != 1 {
		t.Fatalf("MatchingLogs detail mismatch: %#v", envelope.Error.Details)
	}
	warning, ok := envelope.Error.Details["Warning"].(string)
	if !ok || !strings.Contains(warning, "may be truncated") {
		t.Fatalf("Warning detail mismatch: %#v", envelope.Error.Details)
	}
}

// Verifies CapturedVariableHistory never repeats the latest hit: CapturedVariables already
// carries it, so the history must contain only strictly older frames. The note is set only
// when at least one latest-hit frame was dropped.
func TestFilterPausePointCapturedVariableHistoryExcludesLatestFrame(t *testing.T) {
	cases := []struct {
		name            string
		lastHitSequence int
		history         []pausePointCapturedHistoryFrame
		wantSequences   []int
		wantNote        string
	}{
		{
			name:            "single-shot leaves history empty",
			lastHitSequence: 1,
			history:         []pausePointCapturedHistoryFrame{{HitSequence: 1, FrameCount: 10}},
			wantSequences:   []int{},
			wantNote:        pausePointCapturedVariableHistoryNote,
		},
		{
			name:            "continuous with three hits keeps only older frames",
			lastHitSequence: 3,
			history: []pausePointCapturedHistoryFrame{
				{HitSequence: 1, FrameCount: 10},
				{HitSequence: 2, FrameCount: 20},
				{HitSequence: 3, FrameCount: 30},
			},
			wantSequences: []int{1, 2},
			wantNote:      pausePointCapturedVariableHistoryNote,
		},
		{
			name:            "no history stays empty",
			lastHitSequence: 0,
			history:         nil,
			wantSequences:   []int{},
			wantNote:        "",
		},
		{
			name:            "older frames only leave note empty",
			lastHitSequence: 5,
			history: []pausePointCapturedHistoryFrame{
				{HitSequence: 1, FrameCount: 10},
				{HitSequence: 2, FrameCount: 20},
			},
			wantSequences: []int{1, 2},
			wantNote:      "",
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			response := filterPausePointCapturedVariableHistory(pausePointStatusResponse{
				LastHitSequence:         testCase.lastHitSequence,
				CapturedVariableHistory: testCase.history,
			})

			gotSequences := make([]int, len(response.CapturedVariableHistory))
			for index, frame := range response.CapturedVariableHistory {
				gotSequences[index] = frame.HitSequence
			}
			if !reflect.DeepEqual(gotSequences, testCase.wantSequences) {
				t.Fatalf("filtered history mismatch: got %#v, want %#v", gotSequences, testCase.wantSequences)
			}
			if response.CapturedVariableHistory == nil {
				t.Fatalf("CapturedVariableHistory must never be nil so the JSON shape stays constant")
			}
			if response.CapturedVariableHistoryNote != testCase.wantNote {
				t.Fatalf("CapturedVariableHistoryNote mismatch: got %#v, want %#v",
					response.CapturedVariableHistoryNote, testCase.wantNote)
			}
		})
	}
}

// Verifies a set CapturedVariableHistoryNote survives json.Marshal under that exact key.
func TestPausePointStatusResponseIncludesCapturedVariableHistoryNote(t *testing.T) {
	marshaled, err := json.Marshal(pausePointStatusResponse{
		CapturedVariableHistoryNote: pausePointCapturedVariableHistoryNote,
	})
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	var decoded map[string]json.RawMessage
	if err := json.Unmarshal(marshaled, &decoded); err != nil {
		t.Fatalf("unmarshal envelope failed: %v", err)
	}

	rawNote, ok := decoded["CapturedVariableHistoryNote"]
	if !ok {
		t.Fatalf("CapturedVariableHistoryNote missing from JSON: %s", marshaled)
	}

	var note string
	if err := json.Unmarshal(rawNote, &note); err != nil {
		t.Fatalf("unmarshal note failed: %v", err)
	}
	wantNote := "CapturedVariableHistory lists hits before the latest one; the latest hit's variables are in CapturedVariables. HitSequence numbers come from a sequence shared by all pause points in the current Editor domain (it resets on domain reload); they order hits across markers and are not 1..HitCount for this marker."
	if note != wantNote {
		t.Fatalf("CapturedVariableHistoryNote mismatch: got %#v, want %#v",
			note, wantNote)
	}
}

// Verifies an empty CapturedVariableHistoryNote is omitted from JSON so 0-hit
// responses keep the historical shape.
func TestPausePointStatusResponseOmitsEmptyCapturedVariableHistoryNote(t *testing.T) {
	marshaled, err := json.Marshal(pausePointStatusResponse{})
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	if strings.Contains(string(marshaled), "CapturedVariableHistoryNote") {
		t.Fatalf("empty CapturedVariableHistoryNote must be omitted from JSON: %s", marshaled)
	}
}

// Verifies StatusNote is set for every Hit: trace keeps the no-pause wording,
// and non-trace modes get the frame-boundary wording. Non-Hit statuses stay empty
// so omitempty keeps the historical JSON shape.
func TestApplyPausePointHitStatusNote(t *testing.T) {
	const frameBoundaryNote = "Unity pauses at the next frame boundary; the rest of the hit frame already ran. Read at-line values from CapturedVariables; live reads via execute-dynamic-code reflect post-frame state."
	const traceNote = "Trace mode does not pause Play Mode; Status 'Hit' records that the marker fired while the game kept running."
	cases := []struct {
		name     string
		mode     string
		status   string
		wantNote string
	}{
		{
			name:     "trace hit sets note",
			mode:     pausePointModeTrace,
			status:   pausePointStatusHit,
			wantNote: traceNote,
		},
		{
			name:     "continuous hit sets frame-boundary note",
			mode:     pausePointModeContinuous,
			status:   pausePointStatusHit,
			wantNote: frameBoundaryNote,
		},
		{
			name:     "single-shot hit sets frame-boundary note",
			mode:     "single-shot",
			status:   pausePointStatusHit,
			wantNote: frameBoundaryNote,
		},
		{
			name:     "empty mode hit sets frame-boundary note",
			mode:     "",
			status:   pausePointStatusHit,
			wantNote: frameBoundaryNote,
		},
		{
			name:     "trace enabled omits note",
			mode:     pausePointModeTrace,
			status:   pausePointStatusEnabled,
			wantNote: "",
		},
		{
			name:     "trace expired omits note",
			mode:     pausePointModeTrace,
			status:   pausePointStatusExpired,
			wantNote: "",
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			response := applyPausePointHitStatusNote(pausePointStatusResponse{
				Mode:   testCase.mode,
				Status: testCase.status,
			})
			if response.StatusNote != testCase.wantNote {
				t.Fatalf("StatusNote mismatch: got %#v, want %#v",
					response.StatusNote, testCase.wantNote)
			}
		})
	}
}

// Verifies HitWhenNote reports skipped conditional hits only when the line ran
// without any matching capture.
func TestApplyPausePointHitWhenNote(t *testing.T) {
	cases := []struct {
		name     string
		response pausePointStatusResponse
		wantNote string
	}{
		{
			name: "skipped conditional hits set note",
			response: pausePointStatusResponse{
				HitWhen:             "speed > 5",
				HitCount:            0,
				HitWhenSkippedCount: 3,
			},
			wantNote: "The line executed but no hit matched --hit-when; 3 hit(s) were skipped.",
		},
		{
			name: "missing condition omits note",
			response: pausePointStatusResponse{
				HitCount:            0,
				HitWhenSkippedCount: 3,
			},
			wantNote: "",
		},
		{
			name: "matching hit omits note",
			response: pausePointStatusResponse{
				HitWhen:             "speed > 5",
				HitCount:            1,
				HitWhenSkippedCount: 3,
			},
			wantNote: "",
		},
		{
			name: "no skipped hits omits note",
			response: pausePointStatusResponse{
				HitWhen:             "speed > 5",
				HitCount:            0,
				HitWhenSkippedCount: 0,
			},
			wantNote: "",
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			response := applyPausePointHitWhenNote(testCase.response)
			if response.HitWhenNote != testCase.wantNote {
				t.Fatalf("HitWhenNote mismatch: got %#v, want %#v", response.HitWhenNote, testCase.wantNote)
			}
		})
	}
}

// Verifies a set StatusNote survives json.Marshal under that exact key.
func TestPausePointStatusResponseIncludesStatusNote(t *testing.T) {
	marshaled, err := json.Marshal(pausePointStatusResponse{
		StatusNote: pausePointTraceStatusNote,
	})
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	var decoded map[string]json.RawMessage
	if err := json.Unmarshal(marshaled, &decoded); err != nil {
		t.Fatalf("unmarshal envelope failed: %v", err)
	}

	rawNote, ok := decoded["StatusNote"]
	if !ok {
		t.Fatalf("StatusNote missing from JSON: %s", marshaled)
	}

	var note string
	if err := json.Unmarshal(rawNote, &note); err != nil {
		t.Fatalf("unmarshal note failed: %v", err)
	}
	if note != pausePointTraceStatusNote {
		t.Fatalf("StatusNote mismatch: got %#v, want %#v", note, pausePointTraceStatusNote)
	}
}

// Verifies an empty StatusNote is omitted from JSON so non-Hit responses keep
// the historical shape.
func TestPausePointStatusResponseOmitsEmptyStatusNote(t *testing.T) {
	marshaled, err := json.Marshal(pausePointStatusResponse{
		Mode:   pausePointModeTrace,
		Status: pausePointStatusHit,
	})
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	if strings.Contains(string(marshaled), "StatusNote") {
		t.Fatalf("empty StatusNote must be omitted from JSON: %s", marshaled)
	}
}

// Verifies timeout errors include a deterministic diagnosis hint for common stuck states.
func TestPausePointTimeoutErrorIncludesDiagnosisHint(t *testing.T) {
	cases := []struct {
		name     string
		response pausePointStatusResponse
		wantHint string
	}{
		{
			name:     "play mode not running",
			response: pausePointStatusResponse{Id: "jump", Status: pausePointStatusEnabled},
			wantHint: "PlayMode is not running. Start PlayMode (or trigger the marker code path in Edit Mode), then wait again.",
		},
		{
			name: "editor already paused",
			response: pausePointStatusResponse{
				Id:          "jump",
				Status:      pausePointStatusEnabled,
				EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "Current"},
			},
			wantHint: "Unity is already paused, so gameplay cannot reach the marker. Resume PlayMode before waiting again.",
		},
		{
			name: "marker never hit",
			response: pausePointStatusResponse{
				Id:          "jump",
				Status:      pausePointStatusEnabled,
				EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
				HitCount:    0,
			},
			wantHint: "Marker was enabled but never hit. Confirm the id matches UloopPausePoint.Pause(\"<id>\") and that the code path was executed. In fast-progressing games the state may have already moved past the marker (for example back to Ready or GameOver), so re-trigger the code path and wait again. " +
				"If the marker targets a Unity message method such as OnCollisionEnter2D/OnTriggerEnter2D, check whether `enable-pause-point`'s response carried a Warning about cached message dispatch: Unity can resolve a GameObject's message dispatch before the marker patch is installed, so a GameObject that already existed at enable time may never reach the marker even though the method body runs. Recreating the GameObject after enabling, or embedding UloopPausePoint.Pause(\"id\") directly in the method body, avoids this. " +
				"If the target line is inside a very small method, Mono's JIT may have inlined it into callers and the pause point never fires; move the pause point into the calling method. " +
				"If PlayMode kept progressing on its own while you were arranging state (timers, gravity, spawners), the scenario may have already been consumed before this marker could fire; next time, run `control-play-mode --action Pause` before setup and resume with `control-play-mode --action Play` only after `enable-pause-point` succeeds. " +
				"If the target line never hit despite the trigger firing, check the non-firing patterns: (1) the method is a physics/message callback or is called from one on a GameObject that existed before enable — recreate the GameObject or embed UloopPausePoint.Pause; (2) the method was already bound into a delegate/event before enable — the pre-bound invocation path bypasses the patch; (3) the method ran but exited on an earlier branch (for example a guard rejected the action because game state had already moved on) — arm a second marker on the early-return line to see which path ran. (4) the file has active hot-reload patches and the marker resolved against the last compiled source, so the armed line may sit in a different method than the editor shows — check ResolvedMethod, or run 'uloop compile' and re-enable. For patterns (1) and (2), hot-reloading a temporary log line into the method (`uloop hot-reload`) and re-triggering gives a one-way check: the log appearing proves the body ran even though the marker missed. The log staying absent proves nothing — the same cached dispatch can bypass the hot-reload patch too. Note: arming that temporary hot reload itself creates the pattern (4) condition for any later --line in the same file.",
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
				id:             "jump",
				timeoutSeconds: 1,
			}, testCase.response, pausePointWaitStateTimeout, false, false, nil)

			if cliErr.Details["Hint"] != testCase.wantHint {
				t.Fatalf("hint mismatch: %#v", cliErr.Details)
			}
		})
	}
}

// Verifies expired errors include a diagnosis hint, because a marker whose enable
// window ends before the wait deadline surfaces as PAUSE_POINT_EXPIRED, not a timeout.
func TestPausePointExpiredErrorIncludesDiagnosisHint(t *testing.T) {
	cases := []struct {
		name     string
		response pausePointStatusResponse
		wantHint string
	}{
		{
			name:     "play mode not running",
			response: pausePointStatusResponse{Id: "jump", Status: pausePointStatusExpired},
			wantHint: "PlayMode is not running. Start PlayMode (or trigger the marker code path in Edit Mode), then wait again.",
		},
		{
			name: "editor already paused",
			response: pausePointStatusResponse{
				Id:          "jump",
				Status:      pausePointStatusExpired,
				EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "Current"},
			},
			wantHint: "Unity is already paused, so gameplay cannot reach the marker. Resume PlayMode before waiting again.",
		},
		{
			name: "marker expired before hit",
			response: pausePointStatusResponse{
				Id:          "jump",
				Status:      pausePointStatusExpired,
				EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
				HitCount:    0,
			},
			wantHint: "Marker expired before it was hit: the enable-pause-point --timeout-seconds window (measured from enable, not from this wait) ran out. Re-enable the marker with a longer --timeout-seconds and trigger the code path again. " +
				"If the target line never hit despite the trigger firing, check the non-firing patterns: (1) the method is a physics/message callback or is called from one on a GameObject that existed before enable — recreate the GameObject or embed UloopPausePoint.Pause; (2) the method was already bound into a delegate/event before enable — the pre-bound invocation path bypasses the patch; (3) the method ran but exited on an earlier branch (for example a guard rejected the action because game state had already moved on) — arm a second marker on the early-return line to see which path ran. (4) the file has active hot-reload patches and the marker resolved against the last compiled source, so the armed line may sit in a different method than the editor shows — check ResolvedMethod, or run 'uloop compile' and re-enable. For patterns (1) and (2), hot-reloading a temporary log line into the method (`uloop hot-reload`) and re-triggering gives a one-way check: the log appearing proves the body ran even though the marker missed. The log staying absent proves nothing — the same cached dispatch can bypass the hot-reload patch too. Note: arming that temporary hot reload itself creates the pattern (4) condition for any later --line in the same file.",
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
				id:             "jump",
				timeoutSeconds: 1,
			}, testCase.response, pausePointWaitStateExpired, false, false, nil)

			if cliErr.Details["Hint"] != testCase.wantHint {
				t.Fatalf("hint mismatch: %#v", cliErr.Details)
			}
		})
	}
}

// Verifies the expired hint lists hot-reload line drift as candidate (4) and orders it after (3).
func TestPausePointExpiredHintIncludesHotReloadLineDriftCandidate(t *testing.T) {
	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, pausePointStatusResponse{
		Id:          "jump",
		Status:      pausePointStatusExpired,
		EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		HitCount:    0,
	}, pausePointWaitStateExpired, false, false, nil)

	hint, _ := cliErr.Details["Hint"].(string)
	const candidateFour = "(4) the file has active hot-reload patches and the marker resolved against the last compiled source, so the armed line may sit in a different method than the editor shows — check ResolvedMethod, or run 'uloop compile' and re-enable."
	threeIndex := strings.Index(hint, "(3) ")
	fourIndex := strings.Index(hint, candidateFour)
	if fourIndex < 0 {
		t.Fatalf("expired hint missing candidate (4): %q", hint)
	}
	if threeIndex < 0 || fourIndex < threeIndex {
		t.Fatalf("candidate (4) must follow (3): %q", hint)
	}
}

// Verifies hints stay scoped to diagnosable response states.
func TestPausePointHintIsOmittedOutsideDiagnosableStates(t *testing.T) {
	hitResponse := pausePointStatusResponse{
		Id:          "jump",
		Status:      pausePointStatusHit,
		EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "PausePointHit"},
		HitCount:    1,
	}
	timeoutErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, hitResponse, pausePointWaitStateTimeout, false, false, nil)
	if _, exists := timeoutErr.Details["Hint"]; exists {
		t.Fatalf("hint should be omitted when no diagnosis applies: %#v", timeoutErr.Details)
	}

	clearedResponse := pausePointStatusResponse{
		Id:          "jump",
		Status:      pausePointStatusCleared,
		EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
	}
	clearedErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, clearedResponse, pausePointWaitStateCleared, false, false, nil)
	if _, exists := clearedErr.Details["Hint"]; exists {
		t.Fatalf("hint should be omitted for cleared markers: %#v", clearedErr.Details)
	}
}

// Verifies expired markers report no remaining enabled lifetime.
func TestPausePointExpiredErrorReportsNoRemainingTime(t *testing.T) {
	response := pausePointStatusResponse{
		Id:                              "jump",
		Status:                          pausePointStatusExpired,
		TimeoutSeconds:                  1,
		ElapsedSinceEnabledMilliseconds: 1200,
		EditorState:                     pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		Message:                         "Pause point expired before it was hit.",
	}

	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, response, pausePointWaitStateExpired, false, false, nil)

	if cliErr.ErrorCode != clierrors.ErrorCodePausePointExpired {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Details["RemainingMilliseconds"] != int64(0) {
		t.Fatalf("remainingMilliseconds detail mismatch: %#v", cliErr.Details)
	}
}

// Verifies Expired with HitCount > 0 still copies Resolved* into Details but does not claim
// the armed line never ran.
func TestPausePointExpiredErrorOmitsResolvedNoteWhenHitCountPositive(t *testing.T) {
	response := pausePointStatusResponse{
		Id:               "jump",
		Status:           pausePointStatusExpired,
		Expired:          true,
		HitCount:         1,
		ResolvedLine:     42,
		ResolvedLineText: "    DoJump();",
		ResolvedMethod:   "Player.Update",
		SnapshotTiming:   "OnEnter",
		EditorState:      pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
	}

	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, response, pausePointWaitStateExpired, false, false, nil)

	if cliErr.Message != "Pause point expired before it was hit." {
		t.Fatalf("Message mismatch: %q", cliErr.Message)
	}
	if cliErr.Details["ResolvedLine"] != 42 {
		t.Fatalf("ResolvedLine mismatch: %#v", cliErr.Details["ResolvedLine"])
	}
	if cliErr.Details["ResolvedLineText"] != "    DoJump();" {
		t.Fatalf("ResolvedLineText mismatch: %#v", cliErr.Details["ResolvedLineText"])
	}
	if cliErr.Details["ResolvedMethod"] != "Player.Update" {
		t.Fatalf("ResolvedMethod mismatch: %#v", cliErr.Details["ResolvedMethod"])
	}
	if cliErr.Details["SnapshotTiming"] != "OnEnter" {
		t.Fatalf("SnapshotTiming mismatch: %#v", cliErr.Details["SnapshotTiming"])
	}
}

// Verifies Expired with a non-zero ResolvedLine and empty ResolvedLineText still adds the
// reading note and omits the empty text key from Details.
func TestPausePointExpiredErrorNotesResolvedLineWhenTextEmpty(t *testing.T) {
	response := pausePointStatusResponse{
		Id:               "jump",
		Status:           pausePointStatusExpired,
		Expired:          true,
		ResolvedLine:     55,
		ResolvedLineText: "",
		EditorState:      pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
	}

	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, response, pausePointWaitStateExpired, false, false, nil)

	if cliErr.Details["ResolvedLine"] != 55 {
		t.Fatalf("ResolvedLine mismatch: %#v", cliErr.Details["ResolvedLine"])
	}
	if _, present := cliErr.Details["ResolvedLineText"]; present {
		t.Fatalf("ResolvedLineText must be absent, got %#v", cliErr.Details["ResolvedLineText"])
	}
	wantMessage := "Pause point expired before it was hit. The marker stayed armed at the resolved line shown in Details; that line was never executed within the window."
	if cliErr.Message != wantMessage {
		t.Fatalf("Message mismatch:\nwant: %q\ngot:  %q", wantMessage, cliErr.Message)
	}
}

// Verifies a Timeout envelope stays byte-identical for Resolved* even when the status stub
// already carries ResolvedLine.
func TestPausePointTimeoutErrorOmitsResolvedFieldDetails(t *testing.T) {
	response := pausePointStatusResponse{
		Id:               "jump",
		Status:           pausePointStatusEnabled,
		IsEnabled:        true,
		ResolvedLine:     42,
		ResolvedLineText: "    DoJump();",
		ResolvedMethod:   "Player.Update",
		SnapshotTiming:   "OnEnter",
		EditorState:      pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		HitCount:         0,
	}

	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, response, pausePointWaitStateTimeout, false, false, nil)

	if cliErr.Message != "Pause point was not hit within 1s." {
		t.Fatalf("Message mismatch: %q", cliErr.Message)
	}
	for _, field := range []string{"ResolvedLine", "ResolvedLineText", "ResolvedMethod", "SnapshotTiming"} {
		if _, present := cliErr.Details[field]; present {
			t.Fatalf("%s must be absent from Timeout Details, got %#v", field, cliErr.Details[field])
		}
	}
}

// Verifies disabled native pause-point commands are rejected before Unity dispatch.
func TestRunProjectLocalWaitForPausePointRespectsToolSettings(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	writeToolSettings(t, projectRoot, `{"disabledTools":["pause-point"]}`)
	t.Chdir(filepath.Dir(projectRoot))

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunProjectLocal(
		context.Background(),
		[]string{"--project-path", projectRoot, clicore.PausePointAwaitCommandName, "--id", "jump"},
		&stdout,
		&stderr)

	if code != 1 {
		t.Fatalf("expected disabled command failure, got %d with stdout %s", code, stdout.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	if envelope.Error.ErrorCode != clierrors.ErrorCodeToolDisabled {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Command != clicore.PausePointAwaitCommandName {
		t.Fatalf("command mismatch: %#v", envelope.Error)
	}
}

// Verifies disabled pause-point-status is rejected before Unity dispatch.
func TestRunProjectLocalPausePointStatusRespectsToolSettings(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	writeToolSettings(t, projectRoot, `{"disabledTools":["pause-point"]}`)
	t.Chdir(filepath.Dir(projectRoot))

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunProjectLocal(
		context.Background(),
		[]string{"--project-path", projectRoot, clicore.PausePointStatusUserCommandName, "--id", "jump"},
		&stdout,
		&stderr)

	if code != 1 {
		t.Fatalf("expected disabled command failure, got %d with stdout %s", code, stdout.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	if envelope.Error.ErrorCode != clierrors.ErrorCodeToolDisabled {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Command != clicore.PausePointStatusUserCommandName {
		t.Fatalf("command mismatch: %#v", envelope.Error)
	}
}

// Verifies a flag that exists nowhere is reported as a plain unknown option: no stale-runner hint,
// since the flag being absent from every command means the docs cannot be ahead of this build.
func TestParseUnknownOptionErrorsOmitStaleRunnerHint(t *testing.T) {
	wantMessage := `Unknown option "--bogus-flag" for await-pause-point.`

	_, err := parseWaitForPausePointOptions([]string{"--id", "jump", "--bogus-flag", "value"})
	if err == nil {
		t.Fatal("expected error for unknown flag")
	}
	if err.Error() != wantMessage {
		t.Fatalf("await-pause-point message mismatch: %v", err)
	}

	wantStatusMessage := `Unknown option "--bogus-flag" for pause-point-status.`

	_, statusErr := parsePausePointStatusOptions([]string{"--id", "jump", "--bogus-flag", "value"})
	if statusErr == nil {
		t.Fatal("expected error for unknown flag")
	}
	if statusErr.Error() != wantStatusMessage {
		t.Fatalf("pause-point-status message mismatch: %v", statusErr)
	}
}

// Verifies await-pause-point requires either a marker id or a complete file:line target.
func TestParseWaitForPausePointOptionsRequiresMarkerTarget(t *testing.T) {
	_, err := parseWaitForPausePointOptions([]string{"--timeout-seconds", "1"})

	if err == nil {
		t.Fatal("expected missing marker target error")
	}
	if err.Error() != "Missing required option: --id" {
		t.Fatalf("error mismatch: %v", err)
	}
}

// Verifies the missing-target NextAction explains both id and file:line forms for await.
func TestParseWaitForPausePointOptionsMissingTargetNextActionMentionsFileLineFlags(t *testing.T) {
	_, err := parseWaitForPausePointOptions([]string{"--timeout-seconds", "1"})
	requireNextActions(t, err, []string{
		"Pass --id <marker-id> matching UloopPausePoint.Pause(\"<marker-id>\"), or the Id returned by enable-pause-point (file:line markers use <project-relative path>:<line>, e.g. Assets/Scripts/Foo.cs:42). Alternatively, query a file:line marker with --file <project-relative path> --line <line>.",
	})
}

// Verifies pause-point-status reports the current marker state without waiting for a hit.
func TestRunPausePointStatusReturnsCurrentStatus(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:                    id,
			Status:                pausePointStatusEnabled,
			IsEnabled:             true,
			EnabledAtUtc:          "2026-06-03T00:00:00.0000000Z",
			RemainingMilliseconds: 30000,
			Generation:            3,
			EditorState:           pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
			RecommendedNextAction: "Re-enable the marker with a longer --timeout-seconds and trigger the code path again; clearing the expired marker first is not required.",
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	var response pausePointStatusResponse
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout.String())
	}
	if response.Status != pausePointStatusEnabled {
		t.Fatalf("status mismatch: %#v", response)
	}
	if response.EnabledAtUtc != "2026-06-03T00:00:00.0000000Z" {
		t.Fatalf("enabledAtUtc mismatch: %#v", response)
	}
	if response.RemainingMilliseconds != 30000 {
		t.Fatalf("remaining milliseconds mismatch: %#v", response)
	}
	if response.Generation != 3 {
		t.Fatalf("generation mismatch: %#v", response)
	}
	if response.EditorState.CapturedAt != "Current" || !response.EditorState.IsPlaying {
		t.Fatalf("editor state mismatch: %#v", response)
	}
	if response.RecommendedNextAction != "Re-enable the marker with a longer --timeout-seconds and trigger the code path again; clearing the expired marker first is not required." {
		t.Fatalf("recommendedNextAction mismatch: %#v", response)
	}
}

// Verifies pause-point-status stdout includes CapturedVariableHistoryNote when the
// command path filters the latest-hit frame out of history.
func TestRunPausePointStatusIncludesCapturedVariableHistoryNoteWhenLatestHitIsFiltered(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:              id,
			Status:          pausePointStatusHit,
			IsEnabled:       true,
			IsHit:           true,
			HitCount:        1,
			LastHitSequence: 1,
			CapturedVariableHistory: []pausePointCapturedHistoryFrame{
				{HitSequence: 1, FrameCount: 10},
			},
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}

	var decoded map[string]json.RawMessage
	if err := json.Unmarshal(stdout.Bytes(), &decoded); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout.String())
	}

	rawNote, ok := decoded["CapturedVariableHistoryNote"]
	if !ok {
		t.Fatalf("CapturedVariableHistoryNote missing from status JSON: %s", stdout.String())
	}

	var note string
	if err := json.Unmarshal(rawNote, &note); err != nil {
		t.Fatalf("unmarshal note failed: %v", err)
	}
	wantNote := "CapturedVariableHistory lists hits before the latest one; the latest hit's variables are in CapturedVariables. HitSequence numbers come from a sequence shared by all pause points in the current Editor domain (it resets on domain reload); they order hits across markers and are not 1..HitCount for this marker."
	if note != wantNote {
		t.Fatalf("CapturedVariableHistoryNote mismatch: got %#v, want %#v",
			note, wantNote)
	}
}

// Verifies pause-point-status stdout omits CapturedVariableHistoryNote on a 0-hit response.
func TestRunPausePointStatusOmitsCapturedVariableHistoryNoteOnZeroHit(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:              id,
			Status:          pausePointStatusEnabled,
			IsEnabled:       true,
			HitCount:        0,
			LastHitSequence: 0,
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}

	if strings.Contains(stdout.String(), "CapturedVariableHistoryNote") {
		t.Fatalf("0-hit status JSON must omit CapturedVariableHistoryNote: %s", stdout.String())
	}
}

// Verifies pause-point-status stdout includes StatusNote when Unity reports a
// trace-mode Hit. Removing applyPausePointHitStatusNote from the status
// command path makes this test Red.
func TestRunPausePointStatusIncludesStatusNoteOnTraceHit(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:        id,
			Status:    pausePointStatusHit,
			Mode:      pausePointModeTrace,
			IsEnabled: true,
			IsHit:     true,
			HitCount:  1,
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}

	assertStdoutHasPausePointTraceStatusNote(t, stdout.Bytes())
}

// Verifies pause-point-status exposes the hit-when diagnostic after conditional
// captures were skipped, so omitting the status-command wiring makes this test Red.
func TestRunPausePointStatusIncludesHitWhenNoteForSkippedHits(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:                  id,
			Status:              pausePointStatusEnabled,
			IsEnabled:           true,
			HitWhen:             "speed > 5",
			HitWhenSkippedCount: 3,
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}

	assertStdoutHasPausePointHitWhenNote(t, stdout.Bytes(),
		"The line executed but no hit matched --hit-when; 3 hit(s) were skipped.")
}

// Verifies pause-point-status stdout includes the frame-boundary StatusNote on a
// non-trace Hit. Removing applyPausePointHitStatusNote from the status command
// path makes this test Red.
func TestRunPausePointStatusIncludesStatusNoteOnSingleShotHit(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:        id,
			Status:    pausePointStatusHit,
			Mode:      "single-shot",
			IsEnabled: true,
			IsHit:     true,
			HitCount:  1,
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}

	assertStdoutHasPausePointStatusNote(t, stdout.Bytes(),
		"Unity pauses at the next frame boundary; the rest of the hit frame already ran. Read at-line values from CapturedVariables; live reads via execute-dynamic-code reflect post-frame state.")
}

// Verifies await-pause-point stdout includes StatusNote on a trace-mode Hit.
// Removing applyPausePointHitStatusNote from the wait hit path makes this test Red.
func TestRunWaitForPausePointCommandIncludesStatusNoteOnTraceHit(t *testing.T) {
	originalExtend := extendPausePointExpiry
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	t.Cleanup(func() {
		extendPausePointExpiry = originalExtend
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
	})

	extendPausePointExpiry = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
		minimumRemainingSeconds int,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Id: id, Status: pausePointStatusEnabled}, nil
	}

	statusResponses := []pausePointStatusResponse{
		{Id: "jump", Status: pausePointStatusEnabled, IsEnabled: true},
		{
			Id:        "jump",
			Status:    pausePointStatusHit,
			Mode:      pausePointModeTrace,
			IsEnabled: true,
			IsHit:     true,
			HitCount:  1,
		},
	}
	statusCallCount := 0
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		response := statusResponses[statusCallCount]
		statusCallCount++
		return response, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePointCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump", "--timeout-seconds", "1"},
		"",
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}

	assertStdoutHasPausePointTraceStatusNote(t, stdout.Bytes())
}

// Verifies await-pause-point stdout includes the frame-boundary StatusNote on a
// non-trace Hit. Removing applyPausePointHitStatusNote from the wait hit path
// makes this test Red.
func TestRunWaitForPausePointCommandIncludesStatusNoteOnSingleShotHit(t *testing.T) {
	originalExtend := extendPausePointExpiry
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	t.Cleanup(func() {
		extendPausePointExpiry = originalExtend
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
	})

	extendPausePointExpiry = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
		minimumRemainingSeconds int,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Id: id, Status: pausePointStatusEnabled}, nil
	}

	statusResponses := []pausePointStatusResponse{
		{Id: "jump", Status: pausePointStatusEnabled, IsEnabled: true},
		{
			Id:        "jump",
			Status:    pausePointStatusHit,
			Mode:      "single-shot",
			IsEnabled: true,
			IsHit:     true,
			HitCount:  1,
		},
	}
	statusCallCount := 0
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		response := statusResponses[statusCallCount]
		statusCallCount++
		return response, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePointCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump", "--timeout-seconds", "1"},
		"",
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}

	assertStdoutHasPausePointStatusNote(t, stdout.Bytes(),
		"Unity pauses at the next frame boundary; the rest of the hit frame already ran. Read at-line values from CapturedVariables; live reads via execute-dynamic-code reflect post-frame state.")
}

func assertStdoutHasPausePointTraceStatusNote(t *testing.T, stdout []byte) {
	t.Helper()
	assertStdoutHasPausePointStatusNote(t, stdout,
		"Trace mode does not pause Play Mode; Status 'Hit' records that the marker fired while the game kept running.")
}

func assertStdoutHasPausePointStatusNote(t *testing.T, stdout []byte, wantNote string) {
	t.Helper()

	var decoded map[string]json.RawMessage
	if err := json.Unmarshal(stdout, &decoded); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout)
	}

	rawNote, ok := decoded["StatusNote"]
	if !ok {
		t.Fatalf("StatusNote missing from JSON: %s", stdout)
	}

	var note string
	if err := json.Unmarshal(rawNote, &note); err != nil {
		t.Fatalf("unmarshal note failed: %v", err)
	}
	if note != wantNote {
		t.Fatalf("StatusNote mismatch: got %#v, want %#v",
			note, wantNote)
	}
}

func assertStdoutHasPausePointHitWhenNote(t *testing.T, stdout []byte, wantNote string) {
	t.Helper()

	var decoded map[string]json.RawMessage
	if err := json.Unmarshal(stdout, &decoded); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout)
	}

	rawNote, ok := decoded["HitWhenNote"]
	if !ok {
		t.Fatalf("HitWhenNote missing from JSON: %s", stdout)
	}

	var note string
	if err := json.Unmarshal(rawNote, &note); err != nil {
		t.Fatalf("unmarshal note failed: %v", err)
	}
	if note != wantNote {
		t.Fatalf("HitWhenNote mismatch: got %#v, want %#v", note, wantNote)
	}
}

// Verifies pause-point-status passes captured variables and the truncated flag through to stdout.
func TestRunPausePointStatusReturnsCapturedVariables(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:        id,
			Status:    pausePointStatusHit,
			IsEnabled: true,
			IsHit:     true,
			CapturedVariables: []pausePointCapturedVariable{
				{
					Name:     "speed",
					Scope:    "Local",
					TypeName: "System.Int32",
					Value:    pausePointVariableValue("5"),
				},
				{
					Name:                  "enemy",
					Scope:                 "InstanceField",
					TypeName:              "UnityEngine.GameObject",
					Value:                 pausePointVariableValue("Enemy"),
					UnityObjectKind:       "SceneObject",
					UnityObjectPath:       "MainScene:/Root/Enemy",
					UnityObjectInstanceId: -1234,
				},
			},
			CapturedVariablesTruncated: true,
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	var response pausePointStatusResponse
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout.String())
	}
	if !response.CapturedVariablesTruncated {
		t.Fatalf("expected CapturedVariablesTruncated to be true: %#v", response)
	}
	if len(response.CapturedVariables) != 2 {
		t.Fatalf("expected 2 captured variables, got %#v", response.CapturedVariables)
	}
	first := response.CapturedVariables[0]
	if first.Name != "speed" || first.Value == nil || *first.Value != "5" {
		t.Fatalf("first captured variable mismatch: %#v", first)
	}
	second := response.CapturedVariables[1]
	if second.Name != "enemy" || second.UnityObjectKind != "SceneObject" ||
		second.UnityObjectPath != "MainScene:/Root/Enemy" || second.UnityObjectInstanceId != -1234 {
		t.Fatalf("second captured variable mismatch: %#v", second)
	}
}

// Verifies --captured-variables names strips Value from every captured variable (including
// history frames) while keeping Name/Scope/TypeName, for pause-point-status output.
func TestRunPausePointStatusCapturedVariablesNamesOmitsValues(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
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
			Mode:            "continuous",
			LastHitSequence: 2,
			CapturedVariables: []pausePointCapturedVariable{
				{Name: "speed", Scope: "Local", TypeName: "System.Int32", Value: pausePointVariableValue("5")},
			},
			CapturedVariableHistory: []pausePointCapturedHistoryFrame{
				{
					HitSequence: 1,
					CapturedVariables: []pausePointCapturedVariable{
						{Name: "speed", Scope: "Local", TypeName: "System.Int32", Value: pausePointVariableValue("3")},
					},
				},
			},
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump", "--captured-variables", "names"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	if strings.Contains(stdout.String(), `"Value"`) {
		t.Fatalf("Value must be omitted in names mode: %s", stdout.String())
	}

	var response pausePointStatusResponse
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout.String())
	}
	if len(response.CapturedVariables) != 1 || response.CapturedVariables[0].Name != "speed" ||
		response.CapturedVariables[0].TypeName != "System.Int32" {
		t.Fatalf("CapturedVariables mismatch: %#v", response.CapturedVariables)
	}
	if len(response.CapturedVariableHistory) != 1 ||
		len(response.CapturedVariableHistory[0].CapturedVariables) != 1 ||
		response.CapturedVariableHistory[0].CapturedVariables[0].Name != "speed" {
		t.Fatalf("CapturedVariableHistory mismatch: %#v", response.CapturedVariableHistory)
	}
}

// Verifies pausePointCapturedVariable round-trips through JSON without losing or reordering fields.
func TestPausePointStatusResponseCapturedVariablesJSONRoundTrip(t *testing.T) {
	original := pausePointStatusResponse{
		Id:     "jump",
		Status: pausePointStatusHit,
		CapturedVariables: []pausePointCapturedVariable{
			{
				Name:                  "speed",
				Scope:                 "Local",
				TypeName:              "System.Int32",
				Value:                 pausePointVariableValue("5"),
				UnityObjectKind:       "SceneObject",
				UnityObjectPath:       "MainScene:/Root/Enemy",
				UnityObjectInstanceId: -1234,
			},
		},
		CapturedVariablesTruncated: true,
	}

	marshaled, err := json.Marshal(original)
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	var roundTripped pausePointStatusResponse
	if err := json.Unmarshal(marshaled, &roundTripped); err != nil {
		t.Fatalf("unmarshal failed: %v", err)
	}

	if !reflect.DeepEqual(original.CapturedVariables, roundTripped.CapturedVariables) {
		t.Fatalf("captured variables mismatch after round trip: got %#v, want %#v",
			roundTripped.CapturedVariables, original.CapturedVariables)
	}
	if original.CapturedVariablesTruncated != roundTripped.CapturedVariablesTruncated {
		t.Fatalf("capturedVariablesTruncated mismatch after round trip: got %v, want %v",
			roundTripped.CapturedVariablesTruncated, original.CapturedVariablesTruncated)
	}
}

// Verifies a non-Unity-object variable omits all three UnityObject* fields from its JSON,
// since Unity always sets them to their zero value ("", "", 0) for such variables.
func TestPausePointCapturedVariableOmitsUnityObjectFieldsWhenNotAUnityObject(t *testing.T) {
	variable := pausePointCapturedVariable{
		Name:     "speed",
		Scope:    "Local",
		TypeName: "System.Int32",
		Value:    pausePointVariableValue("5"),
	}

	marshaled, err := json.Marshal(variable)
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	for _, field := range []string{"UnityObjectKind", "UnityObjectPath", "UnityObjectInstanceId"} {
		if strings.Contains(string(marshaled), field) {
			t.Fatalf("%s must be omitted for a non-Unity-object variable: %s", field, marshaled)
		}
	}
}

// Verifies UnityObjectKind is the discriminator for "is this a Unity object variable": even
// when UnityObjectInstanceId happens to be its zero value, UnityObjectKind (and Path, if set)
// still appear in the JSON because they are non-zero. This locks in the contract that consumers
// must check UnityObjectKind's presence, not UnityObjectInstanceId's value, to detect a Unity
// object variable.
func TestPausePointCapturedVariableKeepsUnityObjectKindWhenInstanceIdIsZero(t *testing.T) {
	variable := pausePointCapturedVariable{
		Name:                  "destroyedEnemy",
		Scope:                 "Local",
		TypeName:              "UnityEngine.GameObject",
		Value:                 pausePointVariableValue("(destroyed)"),
		UnityObjectKind:       "Destroyed",
		UnityObjectInstanceId: 0,
	}

	marshaled, err := json.Marshal(variable)
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	if !strings.Contains(string(marshaled), `"UnityObjectKind":"Destroyed"`) {
		t.Fatalf("UnityObjectKind must be present when non-empty: %s", marshaled)
	}
	if strings.Contains(string(marshaled), "UnityObjectInstanceId") {
		t.Fatalf("UnityObjectInstanceId must still be omitted when zero, even alongside a non-empty Kind: %s", marshaled)
	}
}

// Verifies a genuinely empty string Value (e.g. a captured `string s = ""`) still serializes as
// "Value":"" in full mode, distinguishable from names mode omitting Value entirely via a nil pointer.
func TestPausePointCapturedVariableKeepsGenuinelyEmptyValueInFullMode(t *testing.T) {
	variable := pausePointCapturedVariable{
		Name:     "label",
		Scope:    "Local",
		TypeName: "System.String",
		Value:    pausePointVariableValue(""),
	}

	marshaled, err := json.Marshal(variable)
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	if !strings.Contains(string(marshaled), `"Value":""`) {
		t.Fatalf("a genuinely empty Value must still be serialized in full mode: %s", marshaled)
	}

	stripped := stripPausePointCapturedVariableValues([]pausePointCapturedVariable{variable})
	strippedMarshaled, err := json.Marshal(stripped[0])
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}
	if strings.Contains(string(strippedMarshaled), `"Value"`) {
		t.Fatalf("names mode must omit Value entirely, even when the original value was empty: %s", strippedMarshaled)
	}
}

// Verifies pause-point-status derives Expired from Status when older Unity packages omit the bool field.
func TestRunPausePointStatusDerivesExpiredFromStatus(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:     id,
			Status: pausePointStatusExpired,
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	var response pausePointStatusResponse
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout.String())
	}
	if !response.Expired {
		t.Fatalf("expired mismatch: %#v", response)
	}
}

// Verifies pause-point-status derives remaining time when older Unity packages omit the field.
func TestRunPausePointStatusDerivesRemainingTimeFromOlderResponse(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:                              id,
			Status:                          pausePointStatusEnabled,
			IsEnabled:                       true,
			TimeoutSeconds:                  30,
			ElapsedSinceEnabledMilliseconds: 1200,
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	var response pausePointStatusResponse
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout.String())
	}
	if response.RemainingMilliseconds != 28800 {
		t.Fatalf("remaining milliseconds mismatch: %#v", response)
	}
}

// Verifies pause-point-status without an id or file:line target selects list mode.
func TestParsePausePointStatusOptionsWithoutTargetUsesListMode(t *testing.T) {
	options, err := parsePausePointStatusOptions([]string{})
	if err != nil {
		t.Fatalf("expected list mode, got %v", err)
	}
	if options.id != "" || options.idProvided || options.queryTarget.hasFile || options.queryTarget.hasLine {
		t.Fatalf("expected empty list target, got %#v", options)
	}
}

// Verifies both query commands compose a source marker id from --file and decimal --line values.
func TestParsePausePointQueryOptionsComposeFileLineID(t *testing.T) {
	awaitOptions, awaitErr := parseWaitForPausePointOptions([]string{
		"--file", `Assets\Scripts\Marker.cs`, "--line", "0042",
	})
	if awaitErr != nil {
		t.Fatalf("await file:line parse failed: %v", awaitErr)
	}
	if awaitOptions.id != "Assets/Scripts/Marker.cs:42" {
		t.Fatalf("await id = %q", awaitOptions.id)
	}

	statusOptions, statusErr := parsePausePointStatusOptions([]string{
		"--file", "/source/Assets/Scripts/Marker.cs", "--line", "7",
	})
	if statusErr != nil {
		t.Fatalf("status file:line parse failed: %v", statusErr)
	}
	if statusOptions.id != "/source/Assets/Scripts/Marker.cs:7" {
		t.Fatalf("status id = %q", statusOptions.id)
	}
}

// Verifies file and line must be supplied together for both pause-point query commands.
func TestParsePausePointQueryOptionsRequireCompleteFileLinePair(t *testing.T) {
	_, awaitFileErr := parseWaitForPausePointOptions([]string{"--file", "Assets/Scripts/Marker.cs"})
	if awaitFileErr == nil || awaitFileErr.Error() != "--file requires --line." {
		t.Fatalf("await file-only error = %v", awaitFileErr)
	}
	_, awaitLineErr := parseWaitForPausePointOptions([]string{"--line", "42"})
	if awaitLineErr == nil || awaitLineErr.Error() != "--line requires --file." {
		t.Fatalf("await line-only error = %v", awaitLineErr)
	}

	_, statusFileErr := parsePausePointStatusOptions([]string{"--file", "Assets/Scripts/Marker.cs"})
	if statusFileErr == nil || statusFileErr.Error() != "--file requires --line." {
		t.Fatalf("status file-only error = %v", statusFileErr)
	}
	_, statusLineErr := parsePausePointStatusOptions([]string{"--line", "42"})
	if statusLineErr == nil || statusLineErr.Error() != "--line requires --file." {
		t.Fatalf("status line-only error = %v", statusLineErr)
	}
}

// Verifies id and file:line target forms are mutually exclusive for both query commands.
func TestParsePausePointQueryOptionsRejectCombinedIDAndFileLineTarget(t *testing.T) {
	tests := []struct {
		name string
		args []string
	}{
		{
			name: "complete file line target",
			args: []string{"--id", "marker", "--file", "Assets/Scripts/Marker.cs", "--line", "42"},
		},
		{
			name: "file only target",
			args: []string{"--id", "marker", "--file", "Assets/Scripts/Marker.cs"},
		},
		{
			name: "line only target",
			args: []string{"--id", "marker", "--line", "42"},
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			_, awaitErr := parseWaitForPausePointOptions(test.args)
			if awaitErr == nil || awaitErr.Error() != "--id cannot be combined with --file or --line." {
				t.Fatalf("await combined-target error = %v", awaitErr)
			}

			_, statusErr := parsePausePointStatusOptions(test.args)
			if statusErr == nil || statusErr.Error() != "--id cannot be combined with --file or --line." {
				t.Fatalf("status combined-target error = %v", statusErr)
			}
		})
	}
}

// Verifies both query commands reject non-positive and non-numeric --line values before id composition.
func TestParsePausePointQueryOptionsRejectInvalidLine(t *testing.T) {
	tests := []struct {
		name      string
		line      string
		wantError string
	}{
		{
			name:      "non numeric",
			line:      "forty-two",
			wantError: "Invalid positive integer value for --line: forty-two",
		},
		{
			name:      "zero",
			line:      "0",
			wantError: "Invalid positive integer value for --line: 0",
		},
		{
			name:      "negative",
			line:      "-42",
			wantError: "Invalid positive integer value for --line: -42",
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			args := []string{"--file", "Assets/Scripts/Marker.cs", "--line", test.line}
			_, awaitErr := parseWaitForPausePointOptions(args)
			if awaitErr == nil || awaitErr.Error() != test.wantError {
				t.Fatalf("await line error = %v", awaitErr)
			}

			_, statusErr := parsePausePointStatusOptions(args)
			if statusErr == nil || statusErr.Error() != test.wantError {
				t.Fatalf("status line error = %v", statusErr)
			}
		})
	}
}

func parsePausePointErrorEnvelope(t *testing.T, payload []byte) clierrors.CLIErrorEnvelope {
	t.Helper()

	var envelope clierrors.CLIErrorEnvelope
	if err := json.Unmarshal(payload, &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, string(payload))
	}
	return envelope
}

func parsePausePointErrorEnvelopeAfterAnnounce(t *testing.T, payload []byte, announceLine string) clierrors.CLIErrorEnvelope {
	t.Helper()
	prefix := announceLine + "\n"
	if !bytes.HasPrefix(payload, []byte(prefix)) {
		t.Fatalf("stderr missing announce prefix %q\nstderr:\n%s", announceLine, payload)
	}
	return parsePausePointErrorEnvelope(t, payload[len(prefix):])
}

func assertStdoutIsSingleJSONObject(t *testing.T, stdout []byte) {
	t.Helper()
	if !json.Valid(bytes.TrimSpace(stdout)) {
		t.Fatalf("stdout is not a single JSON value:\n%s", stdout)
	}
}

// createLaunchTestProject and writeToolSettings are duplicated from
// internal/cli's test helpers of the same name: test helpers cannot be
// shared across packages, and both packages need a minimal Unity project
// fixture with tool settings for their command-gating tests.
func createLaunchTestProject(t *testing.T) string {
	t.Helper()

	projectRoot := t.TempDir()
	for _, directory := range []string{"Assets", "ProjectSettings"} {
		if err := os.MkdirAll(filepath.Join(projectRoot, directory), 0o755); err != nil {
			t.Fatalf("failed to create %s: %v", directory, err)
		}
	}
	return projectRoot
}

func writeToolSettings(t *testing.T, projectRoot string, content string) {
	t.Helper()
	settingsDir := filepath.Join(projectRoot, ".uloop")
	if err := os.MkdirAll(settingsDir, 0o755); err != nil {
		t.Fatalf("failed to create settings dir: %v", err)
	}
	if err := os.WriteFile(filepath.Join(settingsDir, "settings.tools.json"), []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write tool settings: %v", err)
	}
}
