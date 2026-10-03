package clicontract

import (
	"errors"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clitest"
)

func TestCliContractProvidesRuntimeVersion(t *testing.T) {
	// Verifies that the project runner owns its runtime version from the single CLI module.
	clitest.RequireValidContractVersion(t, "projectRunnerVersion", ProjectRunnerVersion())
}

func TestCliContractProvidesProtocolVersion(t *testing.T) {
	// Verifies that the contract declares which C#-side IPC protocol the binary speaks.
	if ProtocolVersion() < 1 {
		t.Fatalf("protocolVersion must be at least 1, got %d", ProtocolVersion())
	}
}

func TestLoadReturnsEmbeddedContract(t *testing.T) {
	// Verifies callers can explicitly load the embedded CLI contract.
	contract, err := Load()
	if err != nil {
		t.Fatalf("Load failed: %v", err)
	}
	clitest.RequireValidContractVersion(t, "projectRunnerVersion", contract.ProjectRunnerVersion)
	if contract.ProtocolVersion < 1 {
		t.Fatalf("protocolVersion must be at least 1, got %d", contract.ProtocolVersion)
	}
}

func TestParseContractReturnsErrorForInvalidJSON(t *testing.T) {
	// Verifies malformed contract data is reported as an error instead of panicking during package init.
	_, err := parseContract([]byte("{"))
	if err == nil {
		t.Fatal("expected invalid JSON error")
	}
}

func TestCliContractDoesNotDeclareDispatcherReleaseFields(t *testing.T) {
	// Verifies release-please CLI version stamping cannot accidentally move dispatcher release metadata.
	fields := clitest.RequireContractFieldMap(t, contractFiles, contractFileName)
	clitest.RequireContractFieldMissing(t, fields, "dispatcherVersion")
	clitest.RequireContractFieldMissing(t, fields, "dispatcherContractVersion")
	clitest.RequireContractFieldMissing(t, fields, "schemaVersion")
}

func TestParseContractRejectsInvalidContracts(t *testing.T) {
	// Verifies malformed JSON, a missing or empty projectRunnerVersion, and a protocolVersion below 1 are each rejected.
	cases := []struct {
		name            string
		content         string
		expectedMessage string
	}{
		{name: "invalid JSON", content: "{", expectedMessage: "CLI contract is invalid JSON"},
		{name: "missing runner version", content: `{"protocolVersion":1}`, expectedMessage: "contract field projectRunnerVersion must not be empty"},
		{name: "empty runner version", content: `{"protocolVersion":1,"projectRunnerVersion":""}`, expectedMessage: "contract field projectRunnerVersion must not be empty"},
		{name: "missing protocol version", content: `{"projectRunnerVersion":"1.2.3"}`, expectedMessage: "protocolVersion must be at least 1, got 0"},
		{name: "negative protocol version", content: `{"protocolVersion":-1,"projectRunnerVersion":"1.2.3"}`, expectedMessage: "protocolVersion must be at least 1, got -1"},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			contract, err := parseContract([]byte(testCase.content))
			if err == nil {
				t.Fatalf("expected error, got contract %#v", contract)
			}
			if !strings.Contains(err.Error(), testCase.expectedMessage) {
				t.Fatalf("error %q should contain %q", err.Error(), testCase.expectedMessage)
			}
			if contract != (Contract{}) {
				t.Fatalf("rejected contract should be zero, got %#v", contract)
			}
		})
	}
}

func TestParseContractReturnsDeclaredFields(t *testing.T) {
	// Verifies a valid contract is returned with its declared protocol and runner versions.
	contract, err := parseContract([]byte(`{"protocolVersion":2,"projectRunnerVersion":"1.2.3"}`))
	if err != nil {
		t.Fatalf("parseContract failed: %v", err)
	}
	if contract.ProtocolVersion != 2 || contract.ProjectRunnerVersion != "1.2.3" {
		t.Fatalf("contract mismatch: %#v", contract)
	}
}

func TestMustLoadContractPanicsWithLoadError(t *testing.T) {
	// Verifies version accessors fail fast with the load error when the embedded contract could not be loaded.
	if _, err := Load(); err != nil {
		t.Fatalf("Load failed: %v", err)
	}
	originalErr := loadContractErr
	loadErr := errors.New("contract load failed")
	loadContractErr = loadErr
	t.Cleanup(func() {
		loadContractErr = originalErr
	})

	defer func() {
		recovered := recover()
		recoveredErr, ok := recovered.(error)
		if !ok || !errors.Is(recoveredErr, loadErr) {
			t.Fatalf("expected panic with load error, got %#v", recovered)
		}
	}()
	ProtocolVersion()
	t.Fatal("ProtocolVersion should panic when the contract failed to load")
}
