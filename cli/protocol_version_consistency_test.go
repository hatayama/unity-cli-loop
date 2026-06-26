package clicontract

import (
	"encoding/json"
	"os"
	"path/filepath"
	"regexp"
	"strconv"
	"testing"
)

// unityProtocolConstantPath is the C# source that declares the protocol generation the
// Unity package accepts. It is relative to this package directory (the cli/ module root).
const (
	unityProtocolConstantPath = "../Packages/src/Editor/Domain/CliConstants.cs"
	unityPackageManifestPath  = "../Packages/src/package.json"
	unityPackageCliPinPath    = "../Packages/src/cli-pin.json"
)

var unityRequiredProtocolVersionPattern = regexp.MustCompile(`REQUIRED_CLI_PROTOCOL_VERSION\s*=\s*(\d+)`)

type unityPackageManifest struct {
	Name    string `json:"name"`
	Version string `json:"version"`
}

type unityPackageCliPin struct {
	SchemaVersion            int    `json:"schemaVersion"`
	PackageName              string `json:"packageName"`
	PackageVersion           string `json:"packageVersion"`
	CLIVersion               string `json:"cliVersion"`
	RequiredProtocolVersion  int    `json:"requiredProtocolVersion"`
	MinimumDispatcherVersion string `json:"minimumDispatcherVersion"`
}

// TestProtocolVersionMatchesUnityPackage is the keystone compatibility invariant: the
// protocol generation the CLI advertises (cli/contract.json) and the generation the Unity
// package requires (CliConstants.REQUIRED_CLI_PROTOCOL_VERSION) must be identical on every
// commit. They are released together, so a breaking IPC change bumps both in one PR; if the
// two ever diverge, a CLI built from this commit would be wrongly rejected or accepted by
// its own package. This check is deterministic and needs no git diff or release numbering.
func TestProtocolVersionMatchesUnityPackage(t *testing.T) {
	unityProtocolVersion := readUnityRequiredProtocolVersion(t)
	if Current.ProtocolVersion != unityProtocolVersion {
		t.Fatalf(
			"protocol version mismatch: cli/contract.json declares %d but %s requires %d; "+
				"bump both together on a breaking IPC change",
			Current.ProtocolVersion, unityProtocolConstantPath, unityProtocolVersion)
	}
}

// TestUnityPackageCliPinMatchesReleaseContracts verifies the dispatcher pin copied into
// projects points at the package release, CLI release, and protocol generation from their
// canonical declarations.
func TestUnityPackageCliPinMatchesReleaseContracts(t *testing.T) {
	manifest := readJSONFile[unityPackageManifest](t, unityPackageManifestPath)
	pin := readJSONFile[unityPackageCliPin](t, unityPackageCliPinPath)

	if pin.SchemaVersion != 1 {
		t.Fatalf("expected %s schemaVersion to be 1, got %d", unityPackageCliPinPath, pin.SchemaVersion)
	}
	if pin.PackageName != manifest.Name {
		t.Fatalf("expected %s packageName to match %s name: %q != %q", unityPackageCliPinPath, unityPackageManifestPath, pin.PackageName, manifest.Name)
	}
	if pin.PackageVersion != manifest.Version {
		t.Fatalf("expected %s packageVersion to match %s version: %q != %q", unityPackageCliPinPath, unityPackageManifestPath, pin.PackageVersion, manifest.Version)
	}
	if pin.CLIVersion != Current.CliVersion {
		t.Fatalf("expected %s cliVersion to match cli/contract.json cliVersion: %q != %q", unityPackageCliPinPath, pin.CLIVersion, Current.CliVersion)
	}
	if pin.RequiredProtocolVersion != Current.ProtocolVersion {
		t.Fatalf("expected %s requiredProtocolVersion to match cli/contract.json protocolVersion: %d != %d", unityPackageCliPinPath, pin.RequiredProtocolVersion, Current.ProtocolVersion)
	}
	if pin.RequiredProtocolVersion != readUnityRequiredProtocolVersion(t) {
		t.Fatalf("expected %s requiredProtocolVersion to match %s", unityPackageCliPinPath, unityProtocolConstantPath)
	}
	if pin.MinimumDispatcherVersion == "" {
		t.Fatalf("expected %s minimumDispatcherVersion to be set", unityPackageCliPinPath)
	}
}

func readUnityRequiredProtocolVersion(t *testing.T) int {
	t.Helper()

	content, err := os.ReadFile(filepath.Clean(unityProtocolConstantPath))
	if err != nil {
		t.Fatalf("failed to read Unity protocol constant from %s: %v", unityProtocolConstantPath, err)
	}

	matches := unityRequiredProtocolVersionPattern.FindStringSubmatch(string(content))
	if len(matches) != 2 {
		t.Fatalf("%s does not define REQUIRED_CLI_PROTOCOL_VERSION", unityProtocolConstantPath)
	}

	unityProtocolVersion, err := strconv.Atoi(matches[1])
	if err != nil {
		t.Fatalf("REQUIRED_CLI_PROTOCOL_VERSION is not an integer: %v", err)
	}

	return unityProtocolVersion
}

func readJSONFile[T any](t *testing.T, path string) T {
	t.Helper()

	content, err := os.ReadFile(filepath.Clean(path))
	if err != nil {
		t.Fatalf("failed to read %s: %v", path, err)
	}

	var value T
	if err := json.Unmarshal(content, &value); err != nil {
		t.Fatalf("failed to parse %s: %v", path, err)
	}
	return value
}
