package projectrunner

import (
	"encoding/json"
	"os"
	"reflect"
	"testing"
)

const watchResponseContractPath = "tests/contracts/watch_response_contract.json"

// Verifies the Go watchResponse DTO preserves every field in the shared Unity watch response contract.
func TestWatchResponseMatchesSharedContract(t *testing.T) {
	fixturePath := findRepoRelativeFile(t, watchResponseContractPath)
	fixture, err := os.ReadFile(fixturePath)
	if err != nil {
		t.Fatalf("failed to read shared watch response contract: %v", err)
	}

	var response watchResponse
	if err := json.Unmarshal(fixture, &response); err != nil {
		t.Fatalf("failed to unmarshal shared watch response contract: %v", err)
	}

	roundTripped, err := json.Marshal(response)
	if err != nil {
		t.Fatalf("failed to marshal Go watch response: %v", err)
	}

	expected := readJSONFieldShape(t, fixture)
	actual := readJSONFieldShape(t, roundTripped)
	if !reflect.DeepEqual(actual, expected) {
		t.Fatalf("watch response field shape drifted\nexpected: %#v\nactual:   %#v", expected, actual)
	}
}
