package projectrunner

import (
	"context"
	"encoding/json"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies respect+PlayMode waits, while EditMode, respect-off, and unset stay on the fast path.
func TestShouldWaitForRunTestsDomainReload(t *testing.T) {
	cases := []struct {
		name   string
		params map[string]any
		want   bool
	}{
		{
			name: "respect and PlayMode",
			params: map[string]any{
				runTestsRespectEnterPlayModeSettingsParam: true,
				runTestsTestModeParam:                     "PlayMode",
			},
			want: true,
		},
		{
			name: "respect and EditMode",
			params: map[string]any{
				runTestsRespectEnterPlayModeSettingsParam: true,
				runTestsTestModeParam:                     "EditMode",
			},
			want: false,
		},
		{
			name: "respect off and PlayMode",
			params: map[string]any{
				runTestsRespectEnterPlayModeSettingsParam: false,
				runTestsTestModeParam:                     "PlayMode",
			},
			want: false,
		},
		{
			name:   "unset",
			params: map[string]any{},
			want:   false,
		},
	}

	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			got := shouldWaitForRunTestsDomainReload(tc.params)
			if got != tc.want {
				t.Fatalf("shouldWaitForRunTestsDomainReload mismatch: got %v want %v params=%#v", got, tc.want, tc.params)
			}
		})
	}
}

// Verifies an existing safe request id is kept and a missing id is generated as run_tests_*.
func TestPrepareRunTestsWaitParams(t *testing.T) {
	existing := map[string]any{runTestsRequestIDParam: "run_tests_existing-1"}
	kept, err := prepareRunTestsWaitParams(existing)
	if err != nil {
		t.Fatalf("prepareRunTestsWaitParams failed: %v", err)
	}
	if kept != "run_tests_existing-1" {
		t.Fatalf("existing request id was not kept: %s", kept)
	}
	if existing[runTestsRequestIDParam] != "run_tests_existing-1" {
		t.Fatalf("params request id mismatch: %#v", existing[runTestsRequestIDParam])
	}

	generatedParams := map[string]any{}
	generated, err := prepareRunTestsWaitParams(generatedParams)
	if err != nil {
		t.Fatalf("prepareRunTestsWaitParams failed: %v", err)
	}
	if !strings.HasPrefix(generated, "run_tests_") {
		t.Fatalf("generated request id prefix mismatch: %s", generated)
	}
	if !isSafeCompileRequestID(generated) {
		t.Fatalf("generated request id is unsafe: %s", generated)
	}
	if generatedParams[runTestsRequestIDParam] != generated {
		t.Fatalf("generated request id was not written to params: %#v", generatedParams[runTestsRequestIDParam])
	}
}

// Verifies the wait deadline is TimeoutSeconds (default 600) plus the 60s margin.
func TestRunTestsWaitTimeoutFromParams(t *testing.T) {
	defaultTimeout := runTestsWaitTimeoutFromParams(map[string]any{})
	if defaultTimeout != (runTestsWaitDefaultTimeoutSeconds*time.Second)+runTestsWaitTimeoutMargin {
		t.Fatalf("default timeout mismatch: %s", defaultTimeout)
	}

	configured := runTestsWaitTimeoutFromParams(map[string]any{runTestsTimeoutParam: 30})
	if configured != 90*time.Second {
		t.Fatalf("configured timeout mismatch: %s", configured)
	}
}

// Verifies polling returns the stored result on the third HasResult response.
func TestWaitForRunTestsResultReturnsStoredResult(t *testing.T) {
	connection := compileWaitTestConnection(t)
	callCount := 0
	query := func(context.Context, unityipc.Connection, string) (runTestsStatusResponse, error) {
		callCount++
		if callCount < 3 {
			return runTestsStatusResponse{Ready: true, HasResult: false}, nil
		}
		return runTestsStatusResponse{
			HasResult: true,
			Result:    json.RawMessage(`{"Success":true,"TestCount":1}`),
		}, nil
	}

	result, completed, err := waitForRunTestsResult(
		context.Background(),
		connection,
		"run_tests_poll",
		time.Second,
		5*time.Millisecond,
		query,
	)
	if err != nil {
		t.Fatalf("waitForRunTestsResult failed: %v", err)
	}
	if !completed {
		t.Fatal("waitForRunTestsResult did not complete")
	}
	if string(result) != `{"Success":true,"TestCount":1}` {
		t.Fatalf("result mismatch: %s", result)
	}
	if callCount != 3 {
		t.Fatalf("query call count mismatch: got %d want 3", callCount)
	}
}

// Verifies a wait with no stored result times out as (nil, false, nil).
func TestWaitForRunTestsResultTimesOutWithoutResult(t *testing.T) {
	connection := compileWaitTestConnection(t)
	query := func(context.Context, unityipc.Connection, string) (runTestsStatusResponse, error) {
		return runTestsStatusResponse{Ready: true, HasResult: false}, nil
	}

	result, completed, err := waitForRunTestsResult(
		context.Background(),
		connection,
		"run_tests_timeout",
		20*time.Millisecond,
		5*time.Millisecond,
		query,
	)
	if err != nil {
		t.Fatalf("waitForRunTestsResult failed: %v", err)
	}
	if completed {
		t.Fatal("waitForRunTestsResult should time out without a stored result")
	}
	if result != nil {
		t.Fatalf("timed-out result should be nil: %s", result)
	}
}

// Verifies cancellation returns the context error instead of a timeout triple.
func TestWaitForRunTestsResultReturnsContextError(t *testing.T) {
	connection := compileWaitTestConnection(t)
	ctx, cancel := context.WithCancel(context.Background())
	query := func(context.Context, unityipc.Connection, string) (runTestsStatusResponse, error) {
		cancel()
		return runTestsStatusResponse{HasResult: false}, nil
	}

	result, completed, err := waitForRunTestsResult(
		ctx,
		connection,
		"run_tests_cancel",
		time.Second,
		time.Second,
		query,
	)
	if err == nil {
		t.Fatal("waitForRunTestsResult should return the cancellation error")
	}
	if completed {
		t.Fatal("cancelled wait should not complete")
	}
	if result != nil {
		t.Fatalf("cancelled result should be nil: %s", result)
	}
}
