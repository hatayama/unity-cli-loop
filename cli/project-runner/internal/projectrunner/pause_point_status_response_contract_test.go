package projectrunner

import (
	"encoding/json"
	"os"
	"reflect"
	"testing"
)

const pausePointStatusResponseContractPath = "tests/contracts/pause_point_status_response_contract.json"

// Verifies the Go pausePointStatusResponse DTO preserves every field in the shared Unity
// PausePointStatusResponse contract, including the CapturedVariables/CapturedVariablesTruncated
// fields introduced for source file:line pause points.
func TestPausePointStatusResponseMatchesSharedContract(t *testing.T) {
	fixture := readPausePointStatusResponseContract(t)

	var response pausePointStatusResponse
	if err := json.Unmarshal(fixture, &response); err != nil {
		t.Fatalf("failed to unmarshal shared pause point status response contract: %v", err)
	}

	roundTripped, err := json.Marshal(response)
	if err != nil {
		t.Fatalf("failed to marshal Go pause point status response: %v", err)
	}

	expected := readJSONFieldShape(t, fixture)
	actual := readJSONFieldShape(t, roundTripped)
	if !reflect.DeepEqual(actual, expected) {
		t.Fatalf("pause point status response field shape drifted\nexpected: %#v\nactual:   %#v", expected, actual)
	}
}

// Verifies empty caller-frame lists stay visible as [] in the re-marshaled CLI payload
// (matching the always-present C# serialization) instead of being dropped.
func TestPausePointStatusResponseKeepsEmptyCallerFramesVisible(t *testing.T) {
	response := pausePointStatusResponse{
		CallerFrames: []pausePointCallerFrame{},
		CapturedVariableHistory: []pausePointCapturedHistoryFrame{
			{CallerFrames: []pausePointCallerFrame{}},
		},
	}

	data, err := json.Marshal(response)
	if err != nil {
		t.Fatalf("failed to marshal pause point status response: %v", err)
	}

	var payload map[string]any
	if err := json.Unmarshal(data, &payload); err != nil {
		t.Fatalf("failed to unmarshal round-tripped payload: %v", err)
	}

	topLevel, ok := payload["CallerFrames"].([]any)
	if !ok || len(topLevel) != 0 {
		t.Fatalf("expected top-level CallerFrames to be an empty array, got %#v", payload["CallerFrames"])
	}

	history, ok := payload["CapturedVariableHistory"].([]any)
	if !ok || len(history) != 1 {
		t.Fatalf("expected one history frame, got %#v", payload["CapturedVariableHistory"])
	}

	historyFrame, ok := history[0].(map[string]any)
	if !ok {
		t.Fatalf("expected history frame object, got %#v", history[0])
	}

	historyFrames, ok := historyFrame["CallerFrames"].([]any)
	if !ok || len(historyFrames) != 0 {
		t.Fatalf("expected history CallerFrames to be an empty array, got %#v", historyFrame["CallerFrames"])
	}
}

// Verifies a payload from a package generation without CallerFrames re-marshals with []
// (the Unity contract shape) instead of null after normalization.
func TestPausePointStatusResponseNormalizesOmittedCallerFrames(t *testing.T) {
	source := []byte(`{"CapturedVariableHistory":[{"HitSequence":1}]}`)

	response := pausePointStatusResponse{}
	if err := json.Unmarshal(source, &response); err != nil {
		t.Fatalf("failed to unmarshal payload without CallerFrames: %v", err)
	}
	normalizePausePointCallerFrames(&response)

	data, err := json.Marshal(response)
	if err != nil {
		t.Fatalf("failed to marshal normalized payload: %v", err)
	}

	var payload map[string]any
	if err := json.Unmarshal(data, &payload); err != nil {
		t.Fatalf("failed to unmarshal round-tripped payload: %v", err)
	}

	if _, ok := payload["CallerFrames"].([]any); !ok {
		t.Fatalf("expected top-level CallerFrames to be an array, got %#v", payload["CallerFrames"])
	}

	history, ok := payload["CapturedVariableHistory"].([]any)
	if !ok || len(history) != 1 {
		t.Fatalf("expected one history frame, got %#v", payload["CapturedVariableHistory"])
	}
	historyFrame, ok := history[0].(map[string]any)
	if !ok {
		t.Fatalf("expected history frame object, got %#v", history[0])
	}
	if _, ok := historyFrame["CallerFrames"].([]any); !ok {
		t.Fatalf("expected history CallerFrames to be an array, got %#v", historyFrame["CallerFrames"])
	}
}

func readPausePointStatusResponseContract(t *testing.T) []byte {
	t.Helper()

	path := findRepoRelativeFile(t, pausePointStatusResponseContractPath)
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read shared pause point status response contract: %v", err)
	}
	return data
}
