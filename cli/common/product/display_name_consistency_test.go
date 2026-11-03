package product

import (
	"encoding/json"
	"os"
	"regexp"
	"testing"
)

// These paths are relative to this package directory (cli/common/product).
const (
	unityPackageManifestPath = "../../../Packages/src/package.json"
	unityConstantsPath       = "../../../Packages/src/Editor/ToolContracts/UnityCliLoopConstants.cs"
)

var unityDisplayNamePattern = regexp.MustCompile(`DISPLAY_NAME\s*=\s*"([^"]*)"`)

// TestDisplayNameMatchesUnityPackageManifest verifies the CLI names the product the same way
// as the Package Manager listing, which shows displayName from package.json.
func TestDisplayNameMatchesUnityPackageManifest(t *testing.T) {
	data, err := os.ReadFile(unityPackageManifestPath)
	if err != nil {
		t.Fatalf("read %s: %v", unityPackageManifestPath, err)
	}
	var manifest struct {
		DisplayName string `json:"displayName"`
	}
	if err := json.Unmarshal(data, &manifest); err != nil {
		t.Fatalf("parse %s: %v", unityPackageManifestPath, err)
	}

	if manifest.DisplayName != DisplayName {
		t.Fatalf("product name mismatch: %s displayName is %q but product.DisplayName is %q; rename both together",
			unityPackageManifestPath, manifest.DisplayName, DisplayName)
	}
}

// TestDisplayNameMatchesUnityEditorConstant verifies the CLI names the product the same way as
// the Editor menus, window titles, and messages, which read UnityCliLoopConstants.DISPLAY_NAME.
func TestDisplayNameMatchesUnityEditorConstant(t *testing.T) {
	data, err := os.ReadFile(unityConstantsPath)
	if err != nil {
		t.Fatalf("read %s: %v", unityConstantsPath, err)
	}
	match := unityDisplayNamePattern.FindSubmatch(data)
	if match == nil {
		t.Fatalf("%s does not declare DISPLAY_NAME", unityConstantsPath)
	}

	if string(match[1]) != DisplayName {
		t.Fatalf("product name mismatch: %s DISPLAY_NAME is %q but product.DisplayName is %q; rename both together",
			unityConstantsPath, match[1], DisplayName)
	}
}
