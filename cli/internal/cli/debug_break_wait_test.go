package cli

import (
	"bytes"
	"context"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

// Verifies wait-for-debug-break polls until Unity reports the marker hit.
func TestWaitForDebugBreakReturnsHitAfterArmedStatus(t *testing.T) {
	originalQuery := queryDebugBreakStatus
	originalPoll := debugBreakStatusPoll
	debugBreakStatusPoll = time.Millisecond
	defer func() {
		queryDebugBreakStatus = originalQuery
		debugBreakStatusPoll = originalPoll
	}()

	responses := []debugBreakStatusResponse{
		{Id: "jump", Status: debugBreakStatusArmed, IsArmed: true},
		{Id: "jump", Status: debugBreakStatusHit, IsHit: true, IsPaused: true, HitCount: 1},
	}
	requestCount := 0
	queryDebugBreakStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (debugBreakStatusResponse, error) {
		if id != "jump" {
			t.Fatalf("id mismatch: %s", id)
		}
		response := responses[requestCount]
		requestCount++
		return response, nil
	}

	response, state, err := waitForDebugBreak(context.Background(), unityipc.Connection{}, waitForDebugBreakOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
	})
	if err != nil {
		t.Fatalf("waitForDebugBreak failed: %v", err)
	}
	if state != debugBreakWaitStateHit {
		t.Fatalf("state mismatch: %s", state)
	}
	if response.Status != debugBreakStatusHit || response.HitCount != 1 {
		t.Fatalf("response mismatch: %#v", response)
	}
	if requestCount != 2 {
		t.Fatalf("request count mismatch: %d", requestCount)
	}
}

// Verifies wait-for-debug-break clears the arm after its own timeout.
func TestRunWaitForDebugBreakClearsArmAfterTimeout(t *testing.T) {
	originalQuery := queryDebugBreakStatus
	originalClear := clearDebugBreakStatus
	originalPoll := debugBreakStatusPoll
	debugBreakStatusPoll = time.Millisecond
	defer func() {
		queryDebugBreakStatus = originalQuery
		clearDebugBreakStatus = originalClear
		debugBreakStatusPoll = originalPoll
	}()

	queryDebugBreakStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (debugBreakStatusResponse, error) {
		return debugBreakStatusResponse{Id: id, Status: debugBreakStatusArmed, IsArmed: true}, nil
	}

	clearedID := ""
	clearDebugBreakStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (debugBreakStatusResponse, error) {
		clearedID = id
		return debugBreakStatusResponse{Id: id, Status: debugBreakStatusCleared}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForDebugBreak(context.Background(), unityipc.Connection{}, waitForDebugBreakOptions{
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
	if !strings.Contains(stderr.String(), errorCodeDebugBreakWaitTimeout) {
		t.Fatalf("timeout error missing from stderr: %s", stderr.String())
	}
}

// Verifies wait-for-debug-break rejects calls before the marker is armed.
func TestWaitForDebugBreakReturnsNotArmedStateImmediately(t *testing.T) {
	originalQuery := queryDebugBreakStatus
	defer func() {
		queryDebugBreakStatus = originalQuery
	}()

	queryDebugBreakStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (debugBreakStatusResponse, error) {
		return debugBreakStatusResponse{Id: id, Status: debugBreakStatusNotArmed}, nil
	}

	response, state, err := waitForDebugBreak(context.Background(), unityipc.Connection{}, waitForDebugBreakOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
	})
	if err != nil {
		t.Fatalf("waitForDebugBreak failed: %v", err)
	}
	if state != debugBreakWaitStateNotArmed {
		t.Fatalf("state mismatch: %s", state)
	}
	if response.Status != debugBreakStatusNotArmed {
		t.Fatalf("response mismatch: %#v", response)
	}
}

// Verifies wait-for-debug-break requires a marker id.
func TestParseWaitForDebugBreakOptionsRequiresID(t *testing.T) {
	_, err := parseWaitForDebugBreakOptions([]string{"--timeout-seconds", "1"})

	if err == nil {
		t.Fatal("expected missing id error")
	}
	if !strings.Contains(err.Error(), "Missing required option") {
		t.Fatalf("error mismatch: %v", err)
	}
}
