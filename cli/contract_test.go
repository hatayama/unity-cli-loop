package clicontract

import (
	"encoding/json"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/version"
)

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

func TestDispatcherContractDoesNotDeclareCliReleaseFields(t *testing.T) {
	// Verifies dispatcher releases stay independent from project-local CLI release metadata.
	fields := requireContractFieldMap(t, dispatcherContractFileName)
	requireContractFieldMissing(t, fields, "projectRunnerVersion")
	requireContractFieldMissing(t, fields, "cliVersion")
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
