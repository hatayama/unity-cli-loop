package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies a valid "Name=value" splits into Name/Expected, and a value containing "=" is
// split on the first "=" only.
func TestParsePausePointExpectFlagValue(t *testing.T) {
	expectation, err := parsePausePointExpectFlagValue("Health=100")
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if expectation != (pausePointExpectation{Name: "Health", Expected: "100"}) {
		t.Fatalf("expectation mismatch: %#v", expectation)
	}

	nested, err := parsePausePointExpectFlagValue("ConnectionString=Server=localhost")
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if nested != (pausePointExpectation{Name: "ConnectionString", Expected: "Server=localhost"}) {
		t.Fatalf("nested expectation mismatch: %#v", nested)
	}
}

// Verifies a value with no "=" is rejected.
func TestParsePausePointExpectFlagValueRejectsMissingEquals(t *testing.T) {
	if _, err := parsePausePointExpectFlagValue("NoEqualsSign"); err == nil {
		t.Fatalf("expected an error")
	}
}

// Verifies evaluatePausePointExpectations covers match, mismatch, and variable-not-found cases,
// searching Local/Parameter/InstanceField/This alike by Name.
func TestEvaluatePausePointExpectations(t *testing.T) {
	variables := []pausePointCapturedVariable{
		{Name: "health", Scope: "Local", Value: pausePointVariableValue("100")},
		{Name: "speed", Scope: "Parameter", Value: pausePointVariableValue("4.2")},
		{Name: "isGrounded", Scope: "InstanceField", Value: pausePointVariableValue("false")},
	}
	expectations := []pausePointExpectation{
		{Name: "health", Expected: "100"},
		{Name: "speed", Expected: "9.9"},
		{Name: "missing", Expected: "whatever"},
	}

	results := evaluatePausePointExpectations(variables, expectations)
	if len(results) != 3 {
		t.Fatalf("results count mismatch: %#v", results)
	}

	if !results[0].Found || !results[0].Passed || results[0].Actual != "100" {
		t.Fatalf("match case mismatch: %#v", results[0])
	}
	if !results[1].Found || results[1].Passed || results[1].Actual != "4.2" {
		t.Fatalf("mismatch case mismatch: %#v", results[1])
	}
	if results[2].Found || results[2].Passed || results[2].Actual != "" {
		t.Fatalf("not-found case mismatch: %#v", results[2])
	}

	if allPausePointExpectationsPassed(results) {
		t.Fatalf("expected overall failure since one expectation failed")
	}
	if !allPausePointExpectationsPassed(results[:1]) {
		t.Fatalf("expected overall success for only the passing expectation")
	}
}

// Verifies no --expect given yields neither Expectations nor AllExpectationsPassed in the JSON.
func TestRunWaitForPausePointOmitsExpectationsWhenNoneRequested(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalFetch := fetchMatchingLogs
	defer func() {
		queryPausePointStatus = originalQuery
		fetchMatchingLogs = originalFetch
	}()

	queryPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Id: id, Status: pausePointStatusHit, IsHit: true, HitCount: 1}, nil
	}
	fetchMatchingLogs = func(ctx context.Context, connection unityipc.Connection, searchText string, maxCount int) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{SearchText: searchText, Logs: []pausePointMatchingLog{}}, nil
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
	if bytes.Contains(stdout.Bytes(), []byte("Expectations")) || bytes.Contains(stdout.Bytes(), []byte("AllExpectationsPassed")) {
		t.Fatalf("Expectations/AllExpectationsPassed must be omitted when --expect was not given: %s", stdout.String())
	}
}

// Verifies --expect assertions are evaluated against the raw CapturedVariables and surfaced in
// the hit response as Expectations + AllExpectationsPassed, covering match and mismatch.
func TestRunWaitForPausePointEvaluatesExpectations(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalFetch := fetchMatchingLogs
	defer func() {
		queryPausePointStatus = originalQuery
		fetchMatchingLogs = originalFetch
	}()

	queryPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:       id,
			Status:   pausePointStatusHit,
			IsHit:    true,
			HitCount: 1,
			CapturedVariables: []pausePointCapturedVariable{
				{Name: "health", Scope: "Local", Value: pausePointVariableValue("100")},
			},
		}, nil
	}
	fetchMatchingLogs = func(ctx context.Context, connection unityipc.Connection, searchText string, maxCount int) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{SearchText: searchText, Logs: []pausePointMatchingLog{}}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:                   "jump",
		timeoutSeconds:       1,
		timeout:              time.Second,
		matchingLogsMaxCount: pausePointDefaultLogsMaxCount,
		expectations: []pausePointExpectation{
			{Name: "health", Expected: "100"},
			{Name: "missing", Expected: "whatever"},
		},
	}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}

	var result pausePointWaitResult
	if err := json.Unmarshal(stdout.Bytes(), &result); err != nil {
		t.Fatalf("stdout parse failed: %v from %s", err, stdout.String())
	}
	if len(result.Expectations) != 2 {
		t.Fatalf("expectations mismatch: %#v", result.Expectations)
	}
	if !result.Expectations[0].Passed || !result.Expectations[0].Found || result.Expectations[0].Actual != "100" {
		t.Fatalf("expectation[0] mismatch: %#v", result.Expectations[0])
	}
	if result.Expectations[1].Passed || result.Expectations[1].Found {
		t.Fatalf("expectation[1] mismatch: %#v", result.Expectations[1])
	}
	if result.AllExpectationsPassed == nil || *result.AllExpectationsPassed {
		t.Fatalf("expected AllExpectationsPassed to be false, got %#v", result.AllExpectationsPassed)
	}
}

// Verifies Found=false expectations produce the not-found Warning on a hit.
func TestRunWaitForPausePointWarnsWhenExpectedVariableNotFound(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalFetch := fetchMatchingLogs
	defer func() {
		queryPausePointStatus = originalQuery
		fetchMatchingLogs = originalFetch
	}()

	queryPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:       id,
			Status:   pausePointStatusHit,
			IsHit:    true,
			HitCount: 1,
			CapturedVariables: []pausePointCapturedVariable{
				{Name: "health", Scope: "Local", Value: pausePointVariableValue("100")},
			},
		}, nil
	}
	fetchMatchingLogs = func(ctx context.Context, connection unityipc.Connection, searchText string, maxCount int) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{SearchText: searchText, Logs: []pausePointMatchingLog{}}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:                   "jump",
		timeoutSeconds:       1,
		timeout:              time.Second,
		matchingLogsMaxCount: pausePointDefaultLogsMaxCount,
		expectations: []pausePointExpectation{
			{Name: "cells", Expected: "whatever"},
			{Name: "total", Expected: "3"},
		},
	}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}

	var result pausePointWaitResult
	if err := json.Unmarshal(stdout.Bytes(), &result); err != nil {
		t.Fatalf("stdout parse failed: %v from %s", err, stdout.String())
	}
	const wantWarning = "Expected variable(s) not present in CapturedVariables: cells, total. This is a not-found result, not a value mismatch — check the variable name, and note that locals can be missing from hot-reload patched bodies compiled before this fix."
	if result.Warning != wantWarning {
		t.Fatalf("Warning mismatch:\n got: %q\nwant: %q", result.Warning, wantWarning)
	}
}

// Verifies Found=true expectations do not emit the not-found Warning, even on value mismatch.
func TestRunWaitForPausePointOmitsNotFoundWarningWhenEveryExpectationIsFound(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalFetch := fetchMatchingLogs
	defer func() {
		queryPausePointStatus = originalQuery
		fetchMatchingLogs = originalFetch
	}()

	queryPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:       id,
			Status:   pausePointStatusHit,
			IsHit:    true,
			HitCount: 1,
			CapturedVariables: []pausePointCapturedVariable{
				{Name: "health", Scope: "Local", Value: pausePointVariableValue("100")},
				{Name: "speed", Scope: "Local", Value: pausePointVariableValue("4.2")},
			},
		}, nil
	}
	fetchMatchingLogs = func(ctx context.Context, connection unityipc.Connection, searchText string, maxCount int) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{SearchText: searchText, Logs: []pausePointMatchingLog{}}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:                   "jump",
		timeoutSeconds:       1,
		timeout:              time.Second,
		matchingLogsMaxCount: pausePointDefaultLogsMaxCount,
		expectations: []pausePointExpectation{
			{Name: "health", Expected: "100"},
			{Name: "speed", Expected: "9.9"},
		},
	}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}

	var result pausePointWaitResult
	if err := json.Unmarshal(stdout.Bytes(), &result); err != nil {
		t.Fatalf("stdout parse failed: %v from %s", err, stdout.String())
	}
	if strings.Contains(result.Warning, "not present in CapturedVariables") {
		t.Fatalf("not-found warning must be absent when every expectation was Found: %q", result.Warning)
	}
}
