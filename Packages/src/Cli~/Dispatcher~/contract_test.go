package dispatchercontract

import (
	"testing"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/version"
)

func TestDispatcherContractProvidesRuntimeVersion(t *testing.T) {
	// Verifies that the dispatcher binary owns its runtime version outside shared packages.
	_, ok := version.Compare(Current.DispatcherVersion, Current.DispatcherVersion)
	if !ok {
		t.Fatalf("dispatcher version must be valid semver: %s", Current.DispatcherVersion)
	}
}

func TestDispatcherContractProvidesVersionEnvironment(t *testing.T) {
	// Verifies that dispatcher owns the environment key it forwards to project-local core.
	if Current.DispatcherVersionEnv != "ULOOP_DISPATCHER_VERSION" {
		t.Fatalf("dispatcher version env mismatch: %s", Current.DispatcherVersionEnv)
	}
}
