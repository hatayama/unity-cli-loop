package projectrunner

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"net"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies --await is extracted and the remaining args are left untouched for schema parsing.
func TestExtractPausePointEnableAwaitFlagsExtractsAwait(t *testing.T) {
	remaining, await, mode, names, expectations, _, _, _, err := extractPausePointEnableAwaitFlags([]string{"--id", "jump", "--await"})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if !await {
		t.Fatalf("expected await to be true")
	}
	if mode != pausePointCapturedVariablesModeFull {
		t.Fatalf("mode mismatch: %s", mode)
	}
	if names != nil {
		t.Fatalf("expected no captured variable names, got %#v", names)
	}
	if expectations != nil {
		t.Fatalf("expected no expectations, got %#v", expectations)
	}
	if len(remaining) != 2 || remaining[0] != "--id" || remaining[1] != "jump" {
		t.Fatalf("remaining args mismatch: %#v", remaining)
	}
}

// Verifies --captured-variables/--captured-variable-names are extracted alongside --await.
func TestExtractPausePointEnableAwaitFlagsExtractsCapturedVariableOptions(t *testing.T) {
	remaining, await, mode, names, _, _, _, _, err := extractPausePointEnableAwaitFlags([]string{
		"--id", "jump", "--await", "--captured-variables", "names", "--captured-variable-names", "a,b",
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if !await {
		t.Fatalf("expected await to be true")
	}
	if mode != pausePointCapturedVariablesModeNames {
		t.Fatalf("mode mismatch: %s", mode)
	}
	if len(names) != 2 || names[0] != "a" || names[1] != "b" {
		t.Fatalf("names mismatch: %#v", names)
	}
	if len(remaining) != 2 || remaining[0] != "--id" || remaining[1] != "jump" {
		t.Fatalf("remaining args mismatch: %#v", remaining)
	}
}

// Verifies --expect is extracted (repeatably) alongside --await, and unrelated args are untouched.
func TestExtractPausePointEnableAwaitFlagsExtractsExpect(t *testing.T) {
	remaining, await, _, _, expectations, _, _, _, err := extractPausePointEnableAwaitFlags([]string{
		"--id", "jump", "--await", "--expect", "Health=100", "--expect", "Name=Enemy",
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if !await {
		t.Fatalf("expected await to be true")
	}
	if len(expectations) != 2 {
		t.Fatalf("expectations mismatch: %#v", expectations)
	}
	if expectations[0] != (pausePointExpectation{Name: "Health", Expected: "100"}) {
		t.Fatalf("expectation[0] mismatch: %#v", expectations[0])
	}
	if expectations[1] != (pausePointExpectation{Name: "Name", Expected: "Enemy"}) {
		t.Fatalf("expectation[1] mismatch: %#v", expectations[1])
	}
	if len(remaining) != 2 || remaining[0] != "--id" || remaining[1] != "jump" {
		t.Fatalf("remaining args mismatch: %#v", remaining)
	}
}

// Verifies --captured-variables without --await is rejected, since it has no effect otherwise.
func TestExtractPausePointEnableAwaitFlagsRequiresAwaitForCapturedVariables(t *testing.T) {
	_, _, _, _, _, _, _, _, err := extractPausePointEnableAwaitFlags([]string{"--id", "jump", "--captured-variables", "names"})
	if err == nil {
		t.Fatalf("expected an error")
	}
	if !strings.Contains(err.Error(), "require --await") {
		t.Fatalf("error message mismatch: %v", err)
	}
}

// Verifies --expect without --await is rejected, since it has no effect otherwise.
func TestExtractPausePointEnableAwaitFlagsRequiresAwaitForExpect(t *testing.T) {
	_, _, _, _, _, _, _, _, err := extractPausePointEnableAwaitFlags([]string{"--id", "jump", "--expect", "Health=100"})
	if err == nil {
		t.Fatalf("expected an error")
	}
	if !strings.Contains(err.Error(), "require --await") {
		t.Fatalf("error message mismatch: %v", err)
	}
}

// Verifies enable-pause-point without --await leaves File/Line/Id/Mode args untouched.
func TestExtractPausePointEnableAwaitFlagsWithoutAwaitLeavesArgsUnchanged(t *testing.T) {
	remaining, await, _, _, _, _, _, _, err := extractPausePointEnableAwaitFlags([]string{"--file", "Assets/Foo.cs", "--line", "10"})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if await {
		t.Fatalf("expected await to be false")
	}
	if len(remaining) != 4 {
		t.Fatalf("remaining args mismatch: %#v", remaining)
	}
}

// Verifies enable-pause-point --await enables the marker then waits, returning a single merged
// hit response without a second enable-pause-point IPC call.
func TestRunEnablePausePointCommandAwaitsAfterSuccessfulEnable(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	originalFetch := fetchMatchingLogs
	pausePointStatusPoll = time.Millisecond
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
		fetchMatchingLogs = originalFetch
	})

	statusResponses := []pausePointStatusResponse{
		{Id: "jump", Status: pausePointStatusEnabled, IsEnabled: true},
		{Id: "jump", Status: pausePointStatusHit, IsHit: true, HitCount: 1},
	}
	statusCallCount := 0
	queryPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		if id != "jump" {
			t.Fatalf("id mismatch: %s", id)
		}
		response := statusResponses[statusCallCount]
		statusCallCount++
		return response, nil
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{SearchText: searchText, Logs: []pausePointMatchingLog{}}, nil
	}

	listener := newLoopbackIpcListener(t)
	enableRequests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(
		listener,
		pausePointEnableCommandName,
		enableRequests,
		serverErr,
		`{"Success":true,"Id":"jump","Status":"Enabled","IsEnabled":true,"TimeoutSeconds":30,"Warning":"cached message dispatch warning"}`,
	)

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runEnablePausePointCommand(
		context.Background(),
		connection,
		[]string{"--id", "jump", "--await"},
		t.TempDir(),
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}

	request := readIPCRequest(t, enableRequests)
	if request["Id"] != "jump" {
		t.Fatalf("enable request Id mismatch: %#v", request)
	}
	if _, hasAwait := request["Await"]; hasAwait {
		t.Fatalf("--await must not leak into the Unity-side request: %#v", request)
	}

	var response pausePointWaitResult
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("failed to decode stdout: %v\n%s", err, stdout.String())
	}
	if response.Status != pausePointStatusHit || response.HitCount != 1 {
		t.Fatalf("response mismatch: %#v", response)
	}
	if !strings.Contains(response.EnableTimeWarning, "cached message dispatch warning") {
		t.Fatalf("expected enable-time warning on EnableTimeWarning, got: %q", response.EnableTimeWarning)
	}
	if strings.Contains(response.Warning, "cached message dispatch warning") {
		t.Fatalf("enable-time warning must not be folded into Warning: %q", response.Warning)
	}
	if statusCallCount != 2 {
		t.Fatalf("status call count mismatch: %d", statusCallCount)
	}
}

// Verifies file:line enable --await copies ResolvedLine / ResolvedLineText / ResolvedMethod /
// SnapshotTiming from the enable response into the await hit payload.
func TestRunEnablePausePointCommandAwaitPropagatesFileLineResolvedFields(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	originalFetch := fetchMatchingLogs
	pausePointStatusPoll = time.Millisecond
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
		fetchMatchingLogs = originalFetch
	})

	statusResponses := []pausePointStatusResponse{
		{Id: "Assets/Foo.cs:42", Status: pausePointStatusEnabled, IsEnabled: true},
		{Id: "Assets/Foo.cs:42", Status: pausePointStatusHit, IsHit: true, HitCount: 1},
	}
	statusCallCount := 0
	queryPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		response := statusResponses[statusCallCount]
		statusCallCount++
		return response, nil
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{SearchText: searchText, Logs: []pausePointMatchingLog{}}, nil
	}

	listener := newLoopbackIpcListener(t)
	enableRequests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(
		listener,
		pausePointEnableCommandName,
		enableRequests,
		serverErr,
		`{"Success":true,"Id":"Assets/Foo.cs:42","Status":"Enabled","IsEnabled":true,"TimeoutSeconds":30,"ResolvedLine":42,"ResolvedLineText":"    DoJump();","ResolvedMethod":"Player.Update","SnapshotTiming":"OnEnter"}`,
	)

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runEnablePausePointCommand(
		context.Background(),
		connection,
		[]string{"--file", "Assets/Foo.cs", "--line", "42", "--await"},
		t.TempDir(),
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}

	var response pausePointWaitResult
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("failed to decode stdout: %v\n%s", err, stdout.String())
	}
	if response.ResolvedLine != 42 {
		t.Fatalf("ResolvedLine mismatch: %#v", response)
	}
	if response.ResolvedLineText != "    DoJump();" {
		t.Fatalf("ResolvedLineText mismatch: %#v", response)
	}
	if response.ResolvedMethod != "Player.Update" {
		t.Fatalf("ResolvedMethod mismatch: %#v", response)
	}
	if response.SnapshotTiming != "OnEnter" {
		t.Fatalf("SnapshotTiming mismatch: %#v", response)
	}
}

// Verifies --await prefers status ResolvedLine / ResolvedLineText when a later status poll
// carries retarget-updated values that differ from the enable-time fields.
func TestRunEnablePausePointCommandAwaitPrefersStatusResolvedFieldsOverEnable(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	originalFetch := fetchMatchingLogs
	pausePointStatusPoll = time.Millisecond
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
		fetchMatchingLogs = originalFetch
	})

	statusResponses := []pausePointStatusResponse{
		{Id: "Assets/Foo.cs:42", Status: pausePointStatusEnabled, IsEnabled: true},
		{
			Id:               "Assets/Foo.cs:42",
			Status:           pausePointStatusHit,
			IsHit:            true,
			HitCount:         1,
			ResolvedLine:     55,
			ResolvedLineText: "    DoJumpRetargeted();",
		},
	}
	statusCallCount := 0
	queryPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		response := statusResponses[statusCallCount]
		statusCallCount++
		return response, nil
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{SearchText: searchText, Logs: []pausePointMatchingLog{}}, nil
	}

	listener := newLoopbackIpcListener(t)
	enableRequests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(
		listener,
		pausePointEnableCommandName,
		enableRequests,
		serverErr,
		`{"Success":true,"Id":"Assets/Foo.cs:42","Status":"Enabled","IsEnabled":true,"TimeoutSeconds":30,"ResolvedLine":42,"ResolvedLineText":"    DoJump();","ResolvedMethod":"Player.Update","SnapshotTiming":"OnEnter"}`,
	)

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runEnablePausePointCommand(
		context.Background(),
		connection,
		[]string{"--file", "Assets/Foo.cs", "--line", "42", "--await"},
		t.TempDir(),
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}

	var response pausePointWaitResult
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("failed to decode stdout: %v\n%s", err, stdout.String())
	}
	if response.ResolvedLine != 55 {
		t.Fatalf("ResolvedLine should prefer status: %#v", response)
	}
	if response.ResolvedLineText != "    DoJumpRetargeted();" {
		t.Fatalf("ResolvedLineText should prefer status: %#v", response)
	}
	if response.ResolvedMethod != "Player.Update" {
		t.Fatalf("ResolvedMethod mismatch: %#v", response)
	}
	if response.SnapshotTiming != "OnEnter" {
		t.Fatalf("SnapshotTiming mismatch: %#v", response)
	}
}

// Verifies --await merges ResolvedLine/Text as a pair: a non-zero status line keeps status
// text even when empty, instead of filling enable-time text onto a status line number.
func TestRunEnablePausePointCommandAwaitKeepsStatusResolvedPairWhenTextEmpty(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	originalFetch := fetchMatchingLogs
	pausePointStatusPoll = time.Millisecond
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
		fetchMatchingLogs = originalFetch
	})

	statusResponses := []pausePointStatusResponse{
		{Id: "Assets/Foo.cs:42", Status: pausePointStatusEnabled, IsEnabled: true},
		{
			Id:               "Assets/Foo.cs:42",
			Status:           pausePointStatusHit,
			IsHit:            true,
			HitCount:         1,
			ResolvedLine:     55,
			ResolvedLineText: "",
		},
	}
	statusCallCount := 0
	queryPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		response := statusResponses[statusCallCount]
		statusCallCount++
		return response, nil
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{SearchText: searchText, Logs: []pausePointMatchingLog{}}, nil
	}

	listener := newLoopbackIpcListener(t)
	enableRequests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(
		listener,
		pausePointEnableCommandName,
		enableRequests,
		serverErr,
		`{"Success":true,"Id":"Assets/Foo.cs:42","Status":"Enabled","IsEnabled":true,"TimeoutSeconds":30,"ResolvedLine":42,"ResolvedLineText":"    DoJump();","ResolvedMethod":"Player.Update","SnapshotTiming":"OnEnter"}`,
	)

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runEnablePausePointCommand(
		context.Background(),
		connection,
		[]string{"--file", "Assets/Foo.cs", "--line", "42", "--await"},
		t.TempDir(),
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}

	var response pausePointWaitResult
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("failed to decode stdout: %v\n%s", err, stdout.String())
	}
	if response.ResolvedLine != 55 {
		t.Fatalf("ResolvedLine should keep status pair: %#v", response)
	}
	if response.ResolvedLineText != "" {
		t.Fatalf("ResolvedLineText must not fall back to enable text when status line is set: %#v", response)
	}
}

// Verifies method-name enable --await omits ResolvedLine / ResolvedLineText / ResolvedMethod /
// SnapshotTiming when the enable response did not set them.
func TestRunEnablePausePointCommandAwaitOmitsResolvedFieldsForMethodArm(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	originalFetch := fetchMatchingLogs
	pausePointStatusPoll = time.Millisecond
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
		fetchMatchingLogs = originalFetch
	})

	statusResponses := []pausePointStatusResponse{
		{Id: "jump", Status: pausePointStatusEnabled, IsEnabled: true},
		{Id: "jump", Status: pausePointStatusHit, IsHit: true, HitCount: 1},
	}
	statusCallCount := 0
	queryPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		response := statusResponses[statusCallCount]
		statusCallCount++
		return response, nil
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{SearchText: searchText, Logs: []pausePointMatchingLog{}}, nil
	}

	listener := newLoopbackIpcListener(t)
	enableRequests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(
		listener,
		pausePointEnableCommandName,
		enableRequests,
		serverErr,
		`{"Success":true,"Id":"jump","Status":"Enabled","IsEnabled":true,"TimeoutSeconds":30}`,
	)

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runEnablePausePointCommand(
		context.Background(),
		connection,
		[]string{"--id", "jump", "--await"},
		t.TempDir(),
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}

	var raw map[string]any
	if err := json.Unmarshal(stdout.Bytes(), &raw); err != nil {
		t.Fatalf("failed to decode stdout: %v\n%s", err, stdout.String())
	}
	for _, field := range []string{"ResolvedLine", "ResolvedLineText", "ResolvedMethod", "SnapshotTiming"} {
		if _, present := raw[field]; present {
			t.Fatalf("method-name await hit must omit %s, got %#v", field, raw)
		}
	}
}

// Verifies a log fetch failure after --await never turns a successful hit into an error, and
// omits MatchingLogs entirely (like the plain await-pause-point path) rather than emitting an
// empty array, so "empty array" keeps meaning "fetch succeeded with no matches" for both commands.
func TestRunEnablePausePointCommandOmitsMatchingLogsOnFetchFailureWhenExpectationsGiven(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	originalFetch := fetchMatchingLogs
	pausePointStatusPoll = time.Millisecond
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
		fetchMatchingLogs = originalFetch
	})

	velocityValue := "4.2"
	statusResponses := []pausePointStatusResponse{
		{Id: "jump", Status: pausePointStatusEnabled, IsEnabled: true},
		{
			Id:       "jump",
			Status:   pausePointStatusHit,
			IsHit:    true,
			HitCount: 1,
			CapturedVariables: []pausePointCapturedVariable{
				{Name: "velocity", Value: &velocityValue},
			},
		},
	}
	statusCallCount := 0
	queryPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		response := statusResponses[statusCallCount]
		statusCallCount++
		return response, nil
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{}, context.DeadlineExceeded
	}

	listener := newLoopbackIpcListener(t)
	enableRequests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(
		listener,
		pausePointEnableCommandName,
		enableRequests,
		serverErr,
		`{"Success":true,"Id":"jump","Status":"Enabled","IsEnabled":true,"TimeoutSeconds":30}`,
	)

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runEnablePausePointCommand(
		context.Background(),
		connection,
		[]string{"--id", "jump", "--await", "--expect", "velocity=4.2"},
		t.TempDir(),
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success despite log fetch failure, got %d with stderr %s", code, stderr.String())
	}
	if strings.Contains(stdout.String(), "MatchingLogs") {
		t.Fatalf("MatchingLogs must be omitted when the fetch fails: %s", stdout.String())
	}
	var result struct {
		AllExpectationsPassed *bool `json:"AllExpectationsPassed"`
	}
	if err := json.Unmarshal(stdout.Bytes(), &result); err != nil {
		t.Fatalf("failed to decode stdout: %v\n%s", err, stdout.String())
	}
	if result.AllExpectationsPassed == nil || !*result.AllExpectationsPassed {
		t.Fatalf("expected expectations to survive the log fetch failure, got: %s", stdout.String())
	}
}

// Verifies a failed enable-pause-point call returns the enable failure directly instead of
// proceeding to wait, since there is no marker to wait on.
func TestRunEnablePausePointCommandDoesNotAwaitAfterFailedEnable(t *testing.T) {
	originalQuery := queryPausePointStatus
	statusCalled := false
	queryPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		statusCalled = true
		return pausePointStatusResponse{}, nil
	}
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
	})

	listener := newLoopbackIpcListener(t)
	enableRequests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(
		listener,
		pausePointEnableCommandName,
		enableRequests,
		serverErr,
		`{"Success":false,"Message":"Id must not be null or empty."}`,
	)

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runEnablePausePointCommand(
		context.Background(),
		connection,
		[]string{"--id", "jump", "--await"},
		t.TempDir(),
		&stdout,
		&stderr)

	if code != 1 {
		t.Fatalf("expected failure, got %d with stdout %s", code, stdout.String())
	}
	if statusCalled {
		t.Fatalf("await must not poll status after a failed enable")
	}
	if !strings.Contains(stdout.String(), "Id must not be null or empty.") {
		t.Fatalf("expected enable failure message in stdout: %s", stdout.String())
	}
}

// Verifies enable-pause-point --await's composite wait path mirrors await-pause-point's
// non-firing-pattern diagnosis hint on a HitCount=0 timeout (Round4 regression: a fix applied
// only to await-pause-point was missed in this composite path).
func TestRunEnablePausePointCommandAwaitTimeoutIncludesNonFiringHint(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	originalClear := clearPausePointStatus
	pausePointStatusPoll = time.Millisecond
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
		clearPausePointStatus = originalClear
	})

	queryPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusEnabled,
			IsEnabled:   true,
			HitCount:    0,
			EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		}, nil
	}
	clearPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Id: id, Status: pausePointStatusCleared}, nil
	}

	listener := newLoopbackIpcListener(t)
	enableRequests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(
		listener,
		pausePointEnableCommandName,
		enableRequests,
		serverErr,
		`{"Success":true,"Id":"jump","Status":"Enabled","IsEnabled":true,"TimeoutSeconds":1}`,
	)

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runEnablePausePointCommand(
		context.Background(),
		connection,
		[]string{"--id", "jump", "--await"},
		t.TempDir(),
		&stdout,
		&stderr)

	if code != 1 {
		t.Fatalf("expected timeout failure, got %d with stdout %s", code, stdout.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	hint, _ := envelope.Error.Details["Hint"].(string)
	if !strings.Contains(hint, "non-firing patterns") {
		t.Fatalf("expected non-firing pattern hint in composite await path, got: %q", hint)
	}
}

func serveSingleIPCResponse(
	listener net.Listener,
	expectedMethod string,
	requests chan<- map[string]any,
	serverErr chan<- error,
	result string,
) {
	conn, err := listener.Accept()
	if err != nil {
		serverErr <- err
		return
	}
	defer func() { _ = conn.Close() }()

	payload, err := unityipc.Read(bufio.NewReader(conn))
	if err != nil {
		serverErr <- err
		return
	}

	request := struct {
		Method string         `json:"method"`
		Params map[string]any `json:"params"`
	}{}
	if err := json.Unmarshal(payload, &request); err != nil {
		serverErr <- err
		return
	}
	if request.Method != expectedMethod {
		serverErr <- fmt.Errorf("method mismatch: %s", request.Method)
		return
	}
	requests <- request.Params

	response := []byte(fmt.Sprintf(`{"jsonrpc":"2.0","result":%s,"id":1}`, result))
	if err := unityipc.Write(conn, response); err != nil {
		serverErr <- err
		return
	}
}

func readIPCRequest(t *testing.T, requests <-chan map[string]any) map[string]any {
	t.Helper()
	select {
	case request := <-requests:
		return request
	case <-time.After(time.Second):
		t.Fatal("timed out waiting for request")
		return nil
	}
}
