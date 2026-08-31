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
// Unity package accepts. It is relative to this package directory (cli/common/clicontract).
const (
	unityProtocolConstantPath = "../../../Packages/src/Editor/Domain/CliConstants.cs"
	unityPackageCliPinPath    = "../../../Packages/src/project-runner-pin.json"
	unityProjectCliPinPath    = "../../../.uloop/project-runner-pin.json"
)

var unityRequiredProtocolVersionPattern = regexp.MustCompile(`REQUIRED_CLI_PROTOCOL_VERSION\s*=\s*(\d+)`)

type unityPackageCliPin struct {
	ProjectRunnerVersion     string `json:"projectRunnerVersion"`
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
	cliProtocolVersion := ProtocolVersion()
	if cliProtocolVersion != unityProtocolVersion {
		t.Fatalf(
			"protocol version mismatch: cli/contract.json declares %d but %s requires %d; "+
				"bump both together on a breaking IPC change",
			cliProtocolVersion, unityProtocolConstantPath, unityProtocolVersion)
	}
}

// TestUnityPackageCliPinMatchesReleaseContracts verifies the pin JSON shipped with the package
// advertises the same project runner release as cli/contract.json and defines a minimum
// dispatcher version.
func TestUnityPackageCliPinMatchesReleaseContracts(t *testing.T) {
	pin := readJSONFile[unityPackageCliPin](t, unityPackageCliPinPath)

	if pin.ProjectRunnerVersion != ProjectRunnerVersion() {
		t.Fatalf("expected %s projectRunnerVersion to match cli/contract.json projectRunnerVersion: %q != %q", unityPackageCliPinPath, pin.ProjectRunnerVersion, ProjectRunnerVersion())
	}
	if pin.MinimumDispatcherVersion == "" {
		t.Fatalf("expected %s minimumDispatcherVersion to be set", unityPackageCliPinPath)
	}
}

// TestUnityProjectCliPinMatchesPackageCliPin verifies the committed project pin does not
// shadow the package pin with stale dispatcher metadata.
func TestUnityProjectCliPinMatchesPackageCliPin(t *testing.T) {
	packagePin := readJSONFile[unityPackageCliPin](t, unityPackageCliPinPath)
	projectPin := readJSONFile[unityPackageCliPin](t, unityProjectCliPinPath)

	if projectPin != packagePin {
		t.Fatalf("expected %s to match %s", unityProjectCliPinPath, unityPackageCliPinPath)
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
