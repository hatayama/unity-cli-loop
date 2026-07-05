package clicontract

import (
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
