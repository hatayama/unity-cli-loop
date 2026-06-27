package clicontract

import (
	"testing"

	"github.com/hatayama/unity-cli-loop/cli/internal/version"
)

func TestCliContractProvidesRuntimeVersion(t *testing.T) {
	// Verifies that the native CLI owns its runtime version from the single CLI module.
	requireValidContractVersion(t, "cliVersion", Current.CliVersion)
}

func TestCliContractProvidesProtocolVersion(t *testing.T) {
	// Verifies that the contract declares which C#-side IPC protocol the binary speaks.
	if Current.ProtocolVersion < 1 {
		t.Fatalf("protocolVersion must be at least 1, got %d", Current.ProtocolVersion)
	}
}

func TestCliContractProvidesDispatcherContractVersion(t *testing.T) {
	// Verifies that the contract declares which dispatcher capability generation the binary provides.
	if DispatcherCurrent.DispatcherContractVersion < 1 {
		t.Fatalf("dispatcherContractVersion must be at least 1, got %d", DispatcherCurrent.DispatcherContractVersion)
	}
}

func TestDispatcherContractProvidesRuntimeVersion(t *testing.T) {
	// Verifies that the launcher owns a release version independent from project-local CLI releases.
	requireValidContractVersion(t, "dispatcherVersion", DispatcherCurrent.DispatcherVersion)
}

func TestCliContractDoesNotOwnDispatcherReleaseVersion(t *testing.T) {
	// Verifies release-please CLI version stamping cannot accidentally move dispatcher release metadata.
	if Current.CliVersion == DispatcherCurrent.DispatcherVersion {
		t.Fatalf("cliVersion and dispatcherVersion should be independently owned: %q", Current.CliVersion)
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
