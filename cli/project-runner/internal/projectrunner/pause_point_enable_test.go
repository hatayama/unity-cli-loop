package projectrunner

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
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
	if !strings.Contains(response.Warning, pausePointEnableTimeWarningPrefix+"cached message dispatch warning") {
		t.Fatalf("expected the prefixed enable-time warning in Warning, got: %q", response.Warning)
	}
	if len(response.Warnings) == 0 {
		t.Fatalf("Warning must never be non-empty while Warnings is empty: %q", response.Warning)
	}
	if statusCallCount != 2 {
		t.Fatalf("status call count mismatch: %d", statusCallCount)
	}
}

// Verifies enable-pause-point --await prints the armed-wait line to stderr as soon as
// the marker is armed (before the first status poll), and keeps stdout a single JSON object.
func TestRunEnablePausePointCommandAnnouncesArmedWaitOnStderr(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	originalFetch := fetchMatchingLogs
	pausePointStatusPoll = time.Millisecond
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
		fetchMatchingLogs = originalFetch
	})

	const wantAnnounce = "Pause point armed (Id: jump). Waiting up to 45s for a hit; the JSON response prints only when the wait ends. If this output gets cut off before then, read the outcome with: uloop pause-point-status --id \"jump\"\n"
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	statusResponses := []pausePointStatusResponse{
		{Id: "jump", Status: pausePointStatusEnabled, IsEnabled: true},
		{Id: "jump", Status: pausePointStatusHit, IsHit: true, HitCount: 1},
	}
	statusCallCount := 0
	queryPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		if statusCallCount == 0 && stderr.String() != wantAnnounce {
			t.Fatalf("announce must appear before the first status poll:\nwant %q\ngot  %q", wantAnnounce, stderr.String())
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
		`{"Success":true,"Id":"jump","Status":"Enabled","IsEnabled":true,"TimeoutSeconds":45}`,
	)

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

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
	_ = readIPCRequest(t, enableRequests)
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

// Verifies enable-pause-point --await stdout includes StatusNote on a trace-mode Hit.
// Removing applyPausePointHitStatusNote from the enable-await hit path makes this test Red.
func TestRunEnablePausePointCommandIncludesStatusNoteOnTraceHit(t *testing.T) {
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

	assertStdoutHasPausePointTraceAwaitStatusNote(t, stdout.Bytes())
}

// Verifies enable-pause-point --await stdout includes the frame-boundary StatusNote
// on a non-trace Hit. Removing applyPausePointHitStatusNote from the enable-await
// hit path makes this test Red.
func TestRunEnablePausePointCommandIncludesStatusNoteOnSingleShotHit(t *testing.T) {
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

	assertStdoutHasPausePointStatusNote(t, stdout.Bytes(),
		"Unity pauses at the next frame boundary; the rest of the hit frame already ran. Read at-line values from CapturedVariables; live reads via execute-dynamic-code reflect post-frame state.")
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
		`{"Success":true,"Id":"Assets/Foo.cs:42","Status":"Enabled","IsEnabled":true,"TimeoutSeconds":30,"ResolvedLine":42,"ResolvedLineText":"    DoJump();","ResolvedMethod":"Player.Update","SnapshotTiming":"OnEnter","LineBasis":"EditedFile"}`,
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
	var raw map[string]any
	if err := json.Unmarshal(stdout.Bytes(), &raw); err != nil {
		t.Fatalf("failed to decode LineBasis: %v\n%s", err, stdout.String())
	}
	if raw["LineBasis"] != "EditedFile" {
		t.Fatalf("LineBasis mismatch: %#v", raw["LineBasis"])
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

// Verifies enable --await Expired copies enable-time ResolvedLine / ResolvedLineText /
// ResolvedMethod / SnapshotTiming into the error Details and explains how to read them.
func TestRunPausePointWaitAfterEnablePropagatesResolvedFieldsOnExpired(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
	})

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
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointWaitAfterEnable(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		waitForPausePointOptions{
			id:                   "Assets/Foo.cs:42",
			timeoutSeconds:       1,
			timeout:              time.Second,
			matchingLogsMaxCount: 5,
			markerJustEnabled:    true,
		},
		enablePausePointPropagatedFields{
			ResolvedLine:     42,
			ResolvedLineText: "    DoJump();",
			ResolvedMethod:   "Player.Update",
			SnapshotTiming:   "OnEnter",
		},
		&stdout,
		&stderr,
	)
	if code != 1 {
		t.Fatalf("expected expired failure, got %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}

	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	if envelope.Error.ErrorCode != "PAUSE_POINT_EXPIRED" {
		t.Fatalf("error code mismatch: %s", envelope.Error.ErrorCode)
	}
	if envelope.Error.Details["ResolvedLine"] != float64(42) {
		t.Fatalf("ResolvedLine mismatch: %#v", envelope.Error.Details["ResolvedLine"])
	}
	if envelope.Error.Details["ResolvedLineText"] != "    DoJump();" {
		t.Fatalf("ResolvedLineText mismatch: %#v", envelope.Error.Details["ResolvedLineText"])
	}
	if envelope.Error.Details["ResolvedMethod"] != "Player.Update" {
		t.Fatalf("ResolvedMethod mismatch: %#v", envelope.Error.Details["ResolvedMethod"])
	}
	if envelope.Error.Details["SnapshotTiming"] != "OnEnter" {
		t.Fatalf("SnapshotTiming mismatch: %#v", envelope.Error.Details["SnapshotTiming"])
	}
	wantMessage := "Pause point expired before it was hit. The marker stayed armed at the resolved line shown in Details; that line was never executed within the window."
	if envelope.Error.Message != wantMessage {
		t.Fatalf("Message mismatch:\nwant: %q\ngot:  %q", wantMessage, envelope.Error.Message)
	}
}

// Verifies enable-pause-point --await copies enable-time LineBasis into Expired error Details,
// because Expired does not marshal the normal wait response.
func TestRunEnablePausePointCommandAwaitPropagatesLineBasisOnExpired(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
	})

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
		}, nil
	}

	listener := newLoopbackIpcListener(t)
	enableRequests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(
		listener,
		pausePointEnableCommandName,
		enableRequests,
		serverErr,
		`{"Success":true,"Id":"Assets/Foo.cs:42","Status":"Enabled","IsEnabled":true,"TimeoutSeconds":30,"LineBasis":"LastCompiledSource"}`,
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
	if code != 1 {
		t.Fatalf("expected expired failure, got %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}

	firstJSONByte := bytes.IndexByte(stderr.Bytes(), '{')
	if firstJSONByte < 0 {
		t.Fatalf("expected error JSON, got %s", stderr.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes()[firstJSONByte:])
	if envelope.Error.Details["LineBasis"] != "LastCompiledSource" {
		t.Fatalf("LineBasis mismatch: %#v", envelope.Error.Details["LineBasis"])
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
	if stderr.Len() != 0 {
		t.Fatalf("failed enable must not announce a wait start, got stderr %q", stderr.String())
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

	cleared := false
	queryPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		if cleared {
			return pausePointStatusResponse{
				Id:                id,
				Status:            pausePointStatusCleared,
				ClearedReason:     pausePointAwaitTimeoutAutoClearReason,
				StatusBeforeClear: pausePointStatusEnabled,
				HitCount:          0,
				EditorState:       pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
			}, nil
		}
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusEnabled,
			IsEnabled:   true,
			HitCount:    0,
			EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		}, nil
	}
	clearPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		cleared = true
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
	envelope := parsePausePointErrorEnvelopeAfterAnnounce(t, stderr.Bytes(),
		"Pause point armed (Id: jump). Waiting up to 1s for a hit; the JSON response prints only when the wait ends. If this output gets cut off before then, read the outcome with: uloop pause-point-status --id \"jump\"")
	if envelope.Error.Details["Status"] != pausePointStatusCleared {
		t.Fatalf("status detail mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["MarkerClearedByThisCommand"] != true {
		t.Fatalf("MarkerClearedByThisCommand mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["ClearedReason"] != pausePointAwaitTimeoutAutoClearReason {
		t.Fatalf("ClearedReason mismatch: %#v", envelope.Error.Details)
	}
	wantHint := pausePointHintTimeoutAutoCleared + pausePointNonFiringPatternsHint
	if envelope.Error.Details["Hint"] != wantHint {
		t.Fatalf("hint mismatch: %#v", envelope.Error.Details["Hint"])
	}
}

// Verifies the Unity clear IPC payload carries Id and Reason=AwaitTimeoutAutoClear, so a key
// rename or a stub-only test cannot hide a broken timeout auto-clear contract.
func TestClearPausePointStatusFromUnitySendsAwaitTimeoutAutoClearReason(t *testing.T) {
	listener := newLoopbackIpcListener(t)
	requests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(
		listener,
		pausePointClearStatusCommandName,
		requests,
		serverErr,
		`{"Success":true,"Id":"jump","Status":"Cleared","ClearedReason":"AwaitTimeoutAutoClear"}`,
	)

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	response, err := clearPausePointStatusFromUnity(context.Background(), connection, "jump")
	if err != nil {
		t.Fatalf("clearPausePointStatusFromUnity failed: %v", err)
	}
	if response.Id != "jump" || response.Status != pausePointStatusCleared {
		t.Fatalf("response mismatch: %#v", response)
	}

	params := readIPCRequest(t, requests)
	if params["Id"] != "jump" {
		t.Fatalf("Id mismatch: %#v", params)
	}
	if params["Reason"] != pausePointAwaitTimeoutAutoClearReason {
		t.Fatalf("Reason mismatch: %#v", params)
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

// Verifies invalid values for the CLI-only enable flags are rejected while parsing, before any request.
func TestExtractPausePointEnableAwaitFlagsRejectsInvalidValues(t *testing.T) {
	cases := map[string]struct {
		args        []string
		wantMessage string
	}{
		"resume-play with non-true value": {args: []string{"--await", "--resume-play=yes"}, wantMessage: "Invalid boolean flag (pass with no value, or =true) value for --resume-play: yes"},
		"trigger without value":           {args: []string{"--await", "--trigger"}, wantMessage: "--trigger requires a value"},
		"trigger with blank command":      {args: []string{"--await", "--trigger", "   "}, wantMessage: "--trigger requires a value"},
		"captured-variables unknown mode": {args: []string{"--await", "--captured-variables", "everything"}, wantMessage: "Invalid full or names value for --captured-variables: everything"},
		"expect without name":             {args: []string{"--await", "--expect", "=5"}, wantMessage: "Invalid --expect value: =5"},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			remaining, _, _, _, _, _, _, _, err := extractPausePointEnableAwaitFlags(testCase.args)
			if argumentError := requireArgumentError(t, err); argumentError.Message != testCase.wantMessage {
				t.Fatalf("Message = %q, want %q", argumentError.Message, testCase.wantMessage)
			}
			if remaining != nil {
				t.Fatalf("remaining args must be nil on error: %#v", remaining)
			}
		})
	}
}

// Verifies --captured-variable-names alone without --await is named as the offending option.
func TestExtractPausePointEnableAwaitFlagsNamesCapturedVariableNamesWithoutAwait(t *testing.T) {
	_, _, _, _, _, _, _, _, err := extractPausePointEnableAwaitFlags([]string{"--captured-variable-names", "speed"})

	if argumentError := requireArgumentError(t, err); argumentError.Option != "--captured-variable-names" {
		t.Fatalf("Option = %q, want --captured-variable-names", argumentError.Option)
	}
}

// Verifies enable-pause-point argument and catalog failures on the --await path exit 1 without
// sending the enable request.
func TestRunEnablePausePointCommandRejectsBeforeSending(t *testing.T) {
	cacheWithoutEnable := t.TempDir()
	if err := os.MkdirAll(filepath.Join(cacheWithoutEnable, ".uloop"), 0o755); err != nil {
		t.Fatalf("mkdir failed: %v", err)
	}
	writeTestFile(t, filepath.Join(cacheWithoutEnable, ".uloop", "tools.json"), `{"tools":[]}`)
	otherProject := writeFakeUnityProject(t)

	cases := []struct {
		name        string
		projectRoot string
		args        []string
		wantStderr  string
	}{
		{name: "invalid CLI-only flag", projectRoot: t.TempDir(), args: []string{"--await", "--resume-play=no"}, wantStderr: "boolean flag (pass with no value, or =true)"},
		{name: "tool missing from project cache", projectRoot: cacheWithoutEnable, args: []string{"--await", "--id", "jump"}, wantStderr: `"ErrorCode": "UNKNOWN_COMMAND"`},
		{name: "unknown schema option", projectRoot: t.TempDir(), args: []string{"--await", "--bogus-flag"}, wantStderr: "Unknown option for enable-pause-point: --bogus-flag"},
		{name: "nested project path for another project", projectRoot: writeFakeUnityProject(t), args: []string{"--await", "--project-path", otherProject}, wantStderr: "--project-path must target the same Unity project"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			var stdout, stderr bytes.Buffer

			code := runEnablePausePointCommand(
				context.Background(),
				unreachableConnection(testCase.projectRoot),
				testCase.args,
				t.TempDir(),
				&stdout,
				&stderr,
			)

			if code != 1 || stdout.Len() != 0 {
				t.Fatalf("code=%d stdout=%q stderr=%s", code, stdout.String(), stderr.String())
			}
			if !strings.Contains(stderr.String(), testCase.wantStderr) {
				t.Fatalf("stderr must mention %q:\n%s", testCase.wantStderr, stderr.String())
			}
		})
	}
}

// stubEnablePausePointSends makes each enable send return the next scripted outcome or error.
func stubEnablePausePointSends(t *testing.T, results []string, errs []error) *int {
	t.Helper()
	original := sendEnablePausePointIPC
	t.Cleanup(func() { sendEnablePausePointIPC = original })
	sends := 0
	sendEnablePausePointIPC = func(context.Context, unityipc.Connection, map[string]any, io.Writer) (unityipc.UnitySendOutcome, error) {
		sends++
		if errs[sends-1] != nil {
			return unityipc.UnitySendOutcome{}, errs[sends-1]
		}
		return unityipc.UnitySendOutcome{Result: json.RawMessage(results[sends-1])}, nil
	}
	return &sends
}

// Verifies each failure on the --await enable path (send failure, failed Release recovery, failed
// resend after recovery) exits 1 without starting the wait.
func TestRunEnablePausePointAndAwaitStopsOnEnableFailures(t *testing.T) {
	sendFailure := errors.New("enable send failed")

	t.Run("send failure", func(t *testing.T) {
		sends := stubEnablePausePointSends(t, []string{""}, []error{sendFailure})
		code, stderr := runEnablePausePointAndAwaitForTest(t)
		if code != 1 || *sends != 1 || !strings.Contains(stderr, "enable send failed") {
			t.Fatalf("code=%d sends=%d stderr=%s", code, *sends, stderr)
		}
	})
	t.Run("Release recovery fails", func(t *testing.T) {
		sends := stubEnablePausePointSends(t, []string{releaseCodeOptimizationEnableFailureJSON}, []error{nil})
		originalSwitch := sendSetCodeOptimizationDebug
		t.Cleanup(func() { sendSetCodeOptimizationDebug = originalSwitch })
		sendSetCodeOptimizationDebug = func(context.Context, unityipc.Connection) error {
			return errors.New("switch refused")
		}
		code, stderr := runEnablePausePointAndAwaitForTest(t)
		if code != 1 || *sends != 1 || !strings.Contains(stderr, "switch refused") {
			t.Fatalf("code=%d sends=%d stderr=%s", code, *sends, stderr)
		}
	})
	t.Run("resend after recovery fails", func(t *testing.T) {
		stubPausePointRecoverySwitchAndCompile(t)
		sends := stubEnablePausePointSends(t, []string{releaseCodeOptimizationEnableFailureJSON, ""}, []error{nil, sendFailure})
		code, stderr := runEnablePausePointAndAwaitForTest(t)
		if code != 1 || *sends != 2 || !strings.Contains(stderr, "enable send failed") {
			t.Fatalf("code=%d sends=%d stderr=%s", code, *sends, stderr)
		}
	})
}

func runEnablePausePointAndAwaitForTest(t *testing.T) (int, string) {
	t.Helper()
	original := queryPausePointStatus
	t.Cleanup(func() { queryPausePointStatus = original })
	queryPausePointStatus = func(context.Context, unityipc.Connection, string) (pausePointStatusResponse, error) {
		t.Fatal("the wait must not start after a failed enable")
		return pausePointStatusResponse{}, nil
	}
	var stdout, stderr bytes.Buffer
	code := runEnablePausePointAndAwait(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		map[string]any{"Id": "jump"},
		pausePointCapturedVariablesModeFull,
		nil, nil, "", nil, false,
		t.TempDir(),
		&stdout,
		&stderr,
	)
	if stdout.Len() != 0 {
		t.Fatalf("stdout must stay empty: %s", stdout.String())
	}
	return code, stderr.String()
}

// stubPausePointWaitAlwaysEnabled keeps the marker armed and unhit for every status poll, with
// a 1ms poll so a short wait times out quickly.
func stubPausePointWaitAlwaysEnabled(t *testing.T) {
	t.Helper()
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	originalClear := clearPausePointStatus
	originalResume := resumePlayModeForPausePoint
	originalLogs := fetchMatchingLogs
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
		clearPausePointStatus = originalClear
		resumePlayModeForPausePoint = originalResume
		fetchMatchingLogs = originalLogs
	})
	pausePointStatusPoll = time.Millisecond
	queryPausePointStatus = func(_ context.Context, _ unityipc.Connection, id string) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Success: true, Id: id, Status: pausePointStatusEnabled, IsEnabled: true, Mode: "continuous"}, nil
	}
	clearPausePointStatus = func(_ context.Context, _ unityipc.Connection, id string) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Success: true, Id: id, Status: pausePointStatusCleared}, nil
	}
	resumePlayModeForPausePoint = func(context.Context, unityipc.Connection) pausePointResumePlayResult {
		return pausePointResumePlayResult{WasPaused: true, Resumed: true}
	}
}

// Verifies an --await timeout after --resume-play reports the resume result and the matching
// logs with their single-fire warning in the error details.
func TestRunPausePointWaitAfterEnableTimeoutReportsResumeAndMatchingLogs(t *testing.T) {
	stubPausePointWaitAlwaysEnabled(t)
	fetchMatchingLogs = func(context.Context, unityipc.Connection, string, int) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{TotalCount: 2, Logs: []pausePointMatchingLog{{Message: "a"}, {Message: "b"}}}, nil
	}
	var stdout, stderr bytes.Buffer

	code := runPausePointWaitAfterEnable(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		waitForPausePointOptions{id: "jump", timeoutSeconds: 1, timeout: 20 * time.Millisecond, resumePlay: true, markerJustEnabled: true},
		enablePausePointPropagatedFields{},
		&stdout,
		&stderr,
	)

	if code != 1 || stdout.Len() != 0 {
		t.Fatalf("code=%d stdout=%q stderr=%s", code, stdout.String(), stderr.String())
	}
	envelope := clierrors.CLIErrorEnvelope{}
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not an error envelope: %v\n%s", err, stderr.String())
	}
	details := envelope.Error.Details
	if resume, _ := details["ResumePlayResult"].(map[string]any); resume["Resumed"] != true {
		t.Fatalf("ResumePlayResult missing: %#v", details)
	}
	if logs, _ := details["MatchingLogs"].([]any); len(logs) != 2 {
		t.Fatalf("MatchingLogs missing: %#v", details)
	}
	if warning, _ := details["Warning"].(string); !strings.Contains(warning, "Multiple matching logs") {
		t.Fatalf("Warning missing: %#v", details)
	}
}

// Verifies a wait that ends with an error (here a cancelled context) is reported on stderr with exit 1.
func TestRunPausePointWaitAfterEnableReportsWaitError(t *testing.T) {
	stubPausePointWaitAlwaysEnabled(t)
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	var stdout, stderr bytes.Buffer

	code := runPausePointWaitAfterEnable(
		ctx,
		unityipc.Connection{ProjectRoot: t.TempDir()},
		waitForPausePointOptions{id: "jump", timeoutSeconds: 1, timeout: time.Second, markerJustEnabled: true},
		enablePausePointPropagatedFields{},
		&stdout,
		&stderr,
	)

	if code != 1 || stdout.Len() != 0 {
		t.Fatalf("code=%d stdout=%q", code, stdout.String())
	}
	if !strings.Contains(stderr.String(), "canceled") {
		t.Fatalf("stderr must report the cancellation:\n%s", stderr.String())
	}
}
