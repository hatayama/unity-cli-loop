package dispatchercontract

import (
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clitest"
)

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
	clitest.RequireContractFieldMissing(t, fields, "dispatcherContractVersion")
	clitest.RequireContractFieldMissing(t, fields, "schemaVersion")
}
