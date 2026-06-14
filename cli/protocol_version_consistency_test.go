package clicontract

import (
	"os"
	"path/filepath"
	"regexp"
	"strconv"
	"testing"
)

// unityProtocolConstantPath is the C# source that declares the protocol generation the
// Unity package accepts. It is relative to this package directory (the cli/ module root).
const unityProtocolConstantPath = "../Packages/src/Editor/Domain/CliConstants.cs"

var unityRequiredProtocolVersionPattern = regexp.MustCompile(`REQUIRED_CLI_PROTOCOL_VERSION\s*=\s*(\d+)`)

// TestProtocolVersionMatchesUnityPackage is the keystone compatibility invariant: the
// protocol generation the CLI advertises (cli/contract.json) and the generation the Unity
// package requires (CliConstants.REQUIRED_CLI_PROTOCOL_VERSION) must be identical on every
// commit. They are released together, so a breaking IPC change bumps both in one PR; if the
// two ever diverge, a CLI built from this commit would be wrongly rejected or accepted by
// its own package. This check is deterministic and needs no git diff or release numbering.
func TestProtocolVersionMatchesUnityPackage(t *testing.T) {
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

	if Current.ProtocolVersion != unityProtocolVersion {
		t.Fatalf(
			"protocol version mismatch: cli/contract.json declares %d but %s requires %d; "+
				"bump both together on a breaking IPC change",
			Current.ProtocolVersion, unityProtocolConstantPath, unityProtocolVersion)
	}
}
