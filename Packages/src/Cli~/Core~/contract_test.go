package corecontract

import (
	"testing"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/version"
)

func TestCoreContractProvidesRuntimeVersion(t *testing.T) {
	// Verifies that the core binary owns its runtime version outside shared packages.
	requireValidContractVersion(t, "coreVersion", Current.CoreVersion)
}

func TestCoreContractProvidesDispatcherRequirement(t *testing.T) {
	// Verifies that core owns the minimum dispatcher version needed to execute this binary.
	requireValidContractVersion(t, "minimumRequiredDispatcherVersion", Current.MinimumRequiredDispatcherVersion)
}

func TestCoreContractProvidesRequiredDispatcherVersionFlag(t *testing.T) {
	// Verifies that Unity and Go can share the flag used to query dispatcher compatibility.
	if Current.RequiredDispatcherVersionFlag != "--required-dispatcher-version" {
		t.Fatalf("required dispatcher version flag mismatch: %s", Current.RequiredDispatcherVersionFlag)
	}
}

func TestCoreContractProvidesDispatcherVersionEnvironment(t *testing.T) {
	// Verifies that core declares the environment key it accepts from the dispatcher.
	if Current.DispatcherVersionEnv != "ULOOP_DISPATCHER_VERSION" {
		t.Fatalf("dispatcher version env mismatch: %s", Current.DispatcherVersionEnv)
	}
}

func requireValidContractVersion(t *testing.T, label string, value string) {
	t.Helper()

	if value == "" {
		t.Fatalf("%s must not be empty", label)
	}
	_, ok := version.Compare(value, value)
	if !ok {
		t.Fatalf("%s must be valid semver: %s", label, value)
	}
}
