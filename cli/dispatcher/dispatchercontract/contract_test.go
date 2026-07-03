package dispatchercontract

import (
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clitest"
)

func TestCliContractProvidesDispatcherContractVersion(t *testing.T) {
	// Verifies that the contract declares which dispatcher capability generation the binary provides.
	if DispatcherCurrent.DispatcherContractVersion < 1 {
		t.Fatalf("dispatcherContractVersion must be at least 1, got %d", DispatcherCurrent.DispatcherContractVersion)
	}
}

func TestDispatcherContractProvidesRuntimeVersion(t *testing.T) {
	// Verifies that the launcher owns a release version independent from project-local CLI releases.
	clitest.RequireValidContractVersion(t, "dispatcherVersion", DispatcherCurrent.DispatcherVersion)
}

func TestDispatcherContractDoesNotDeclareCliReleaseFields(t *testing.T) {
	// Verifies dispatcher releases stay independent from project-local CLI release metadata.
	fields := clitest.RequireContractFieldMap(t, contractFiles, dispatcherContractFileName)
	clitest.RequireContractFieldMissing(t, fields, "projectRunnerVersion")
	clitest.RequireContractFieldMissing(t, fields, "cliVersion")
	clitest.RequireContractFieldMissing(t, fields, "protocolVersion")
}
