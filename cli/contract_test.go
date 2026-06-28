package clicontract

import (
	"encoding/json"
	"testing"

	"github.com/hatayama/unity-cli-loop/cli/internal/version"
)

func TestCliContractProvidesRuntimeVersion(t *testing.T) {
	// Verifies that the project runner owns its runtime version from the single CLI module.
	requireValidContractVersion(t, "projectRunnerVersion", Current.ProjectRunnerVersion)
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

func TestCliContractDoesNotDeclareDispatcherReleaseFields(t *testing.T) {
	// Verifies release-please CLI version stamping cannot accidentally move dispatcher release metadata.
	fields := requireContractFieldMap(t, contractFileName)
	requireContractFieldMissing(t, fields, "dispatcherVersion")
	requireContractFieldMissing(t, fields, "dispatcherContractVersion")
}

func TestDispatcherContractDoesNotDeclareCliReleaseFields(t *testing.T) {
	// Verifies dispatcher releases stay independent from project-local CLI release metadata.
	fields := requireContractFieldMap(t, dispatcherContractFileName)
	requireContractFieldMissing(t, fields, "projectRunnerVersion")
	requireContractFieldMissing(t, fields, "protocolVersion")
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

func requireContractFieldMap(t *testing.T, fileName string) map[string]any {
	t.Helper()

	content, err := contractFiles.ReadFile(fileName)
	if err != nil {
		t.Fatalf("failed to read %s: %v", fileName, err)
	}
	fields := map[string]any{}
	if err := json.Unmarshal(content, &fields); err != nil {
		t.Fatalf("%s is invalid JSON: %v", fileName, err)
	}
	return fields
}

func requireContractFieldMissing(t *testing.T, fields map[string]any, fieldName string) {
	t.Helper()

	if _, ok := fields[fieldName]; ok {
		t.Fatalf("contract must not declare %s", fieldName)
	}
}
