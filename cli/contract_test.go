package clicontract

import (
	"testing"

	"github.com/hatayama/unity-cli-loop/cli/internal/version"
)

func TestCliContractProvidesRuntimeVersion(t *testing.T) {
	// Verifies that the native CLI owns its runtime version from the single CLI module.
	requireValidContractVersion(t, "cliVersion", Current.CliVersion)
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
