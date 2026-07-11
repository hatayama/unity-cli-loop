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

func readPausePointStatusResponseContract(t *testing.T) []byte {
	t.Helper()

	path := findRepoRelativeFile(t, pausePointStatusResponseContractPath)
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read shared pause point status response contract: %v", err)
	}
	return data
}
