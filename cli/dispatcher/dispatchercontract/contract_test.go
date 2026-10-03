package dispatchercontract

import (
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clitest"
)

func TestDispatcherContractProvidesRuntimeVersion(t *testing.T) {
	// Verifies that the dispatcher owns a release version independent from project-local CLI releases.
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

func TestRequireStringPanicsOnEmptyField(t *testing.T) {
	// Verifies an empty contract field stops loading with a panic that names the field.
	defer func() {
		recovered := recover()
		if recovered != "contract field dispatcherVersion must not be empty" {
			t.Fatalf("recovered = %#v", recovered)
		}
	}()

	requireString("", "dispatcherVersion")
}
