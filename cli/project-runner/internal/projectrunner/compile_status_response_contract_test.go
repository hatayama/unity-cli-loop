package projectrunner

import (
	"encoding/json"
	"os"
	"reflect"
	"testing"
)

const compileStatusResponseContractPath = "tests/contracts/compile_status_response_contract.json"

// Verifies the Go compileStatusResponse DTO preserves every field in the shared Unity
// get-compile-status response contract, including nested Result.Success used by compileResultStatus.
func TestCompileStatusResponseMatchesSharedContract(t *testing.T) {
	fixturePath := findRepoRelativeFile(t, compileStatusResponseContractPath)
	fixture, err := os.ReadFile(fixturePath)
	if err != nil {
		t.Fatalf("failed to read shared compile status response contract: %v", err)
	}

	var response compileStatusResponse
	if err := json.Unmarshal(fixture, &response); err != nil {
		t.Fatalf("failed to unmarshal shared compile status response contract: %v", err)
	}

	assertCompileStatusResponseFieldsPopulated(t, response)

	var resultStatus compileResultStatus
	if err := json.Unmarshal(response.Result, &resultStatus); err != nil {
		t.Fatalf("failed to unmarshal Result into compileResultStatus: %v", err)
	}
	if resultStatus.Success == nil {
		t.Fatal("compileResultStatus.Success must be non-nil after unmarshaling the shared contract")
	}

	roundTripped, err := json.Marshal(response)
	if err != nil {
		t.Fatalf("failed to marshal Go compile status response: %v", err)
	}

	expected := readJSONFieldShape(t, fixture)
	actual := readJSONFieldShape(t, roundTripped)
	if !reflect.DeepEqual(actual, expected) {
		t.Fatalf("compile status response field shape drifted\nexpected: %#v\nactual:   %#v", expected, actual)
	}
}

func assertCompileStatusResponseFieldsPopulated(t *testing.T, response compileStatusResponse) {
	t.Helper()

	if !response.Ready {
		t.Fatal("Ready must be true in the shared contract fixture")
	}
	if !response.HasResult {
		t.Fatal("HasResult must be true in the shared contract fixture")
	}
	if !response.IsCompiling {
		t.Fatal("IsCompiling must be true in the shared contract fixture")
	}
	if !response.IsUpdating {
		t.Fatal("IsUpdating must be true in the shared contract fixture")
	}
	if !response.IsDomainReloadInProgress {
		t.Fatal("IsDomainReloadInProgress must be true in the shared contract fixture")
	}
	if len(response.Result) == 0 {
		t.Fatal("Result must be non-empty in the shared contract fixture")
	}
	if response.Message == "" {
		t.Fatal("Message must be non-empty in the shared contract fixture")
	}
}
