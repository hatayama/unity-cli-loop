package clicontract

import (
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clitest"
)

func TestCliContractProvidesRuntimeVersion(t *testing.T) {
	// Verifies that the project runner owns its runtime version from the single CLI module.
	clitest.RequireValidContractVersion(t, "projectRunnerVersion", Current.ProjectRunnerVersion)
}

func TestCliContractProvidesProtocolVersion(t *testing.T) {
	// Verifies that the contract declares which C#-side IPC protocol the binary speaks.
	if Current.ProtocolVersion < 1 {
		t.Fatalf("protocolVersion must be at least 1, got %d", Current.ProtocolVersion)
	}
}

func TestCliContractDoesNotDeclareDispatcherReleaseFields(t *testing.T) {
	// Verifies release-please CLI version stamping cannot accidentally move dispatcher release metadata.
	fields := clitest.RequireContractFieldMap(t, contractFiles, contractFileName)
	clitest.RequireContractFieldMissing(t, fields, "dispatcherVersion")
	clitest.RequireContractFieldMissing(t, fields, "dispatcherContractVersion")
}
