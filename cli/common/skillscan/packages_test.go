package skillscan

import (
	"os"
	"path/filepath"
	"testing"
)

// Tests that local manifest packages from other dependencies cannot replace the Unity CLI Loop package root.
func TestResolvePackageRootIgnoresOtherManifestFilePackages(t *testing.T) {
	projectRoot := t.TempDir()
	otherPackageRoot := filepath.Join(t.TempDir(), "AOtherPackage")
	packageRoot := filepath.Join(t.TempDir(), "ZUnityCliLoopPackage")
	for _, candidateRoot := range []string{otherPackageRoot, packageRoot} {
		markerPath := filepath.Join(candidateRoot, "Editor", "FirstPartyTools")
		if err := os.MkdirAll(markerPath, 0o755); err != nil {
			t.Fatalf("failed to create marker path: %v", err)
		}
	}
	writeManifest(
		t,
		projectRoot,
		`{"dependencies":{"com.example.other":"file:`+filepath.ToSlash(otherPackageRoot)+`","io.github.hatayama.uloopmcp":"file:`+filepath.ToSlash(packageRoot)+`"}}`)

	actualRoot := filepath.Clean(ResolvePackageRoot(projectRoot))
	expectedRoot := filepath.Clean(packageRoot)
	if actualRoot != expectedRoot {
		t.Fatalf("package root mismatch: actual=%s expected=%s", actualRoot, expectedRoot)
	}
}

// Tests that package root probing uses the current first-party tool marker.
func TestResolvePackageRootCandidateUsesFirstPartyToolsMarker(t *testing.T) {
	projectRoot := t.TempDir()
	markerPath := filepath.Join(projectRoot, "Packages", "src", "Editor", "FirstPartyTools")
	if err := os.MkdirAll(markerPath, 0o755); err != nil {
		t.Fatalf("failed to create marker path: %v", err)
	}

	actualRoot := resolvePackageRootCandidate(projectRoot)
	expectedRoot := filepath.Join(projectRoot, "Packages", "src")
	if actualRoot != expectedRoot {
		t.Fatalf("package root mismatch: actual=%s expected=%s", actualRoot, expectedRoot)
	}
}

// writeManifest is duplicated from internal/cli's test helper of the same
// name: test helpers cannot be shared across packages, and both packages
// exercise package-root resolution from a project's manifest.json.
func writeManifest(t *testing.T, projectRoot string, content string) {
	t.Helper()
	manifestDir := filepath.Join(projectRoot, "Packages")
	if err := os.MkdirAll(manifestDir, 0o755); err != nil {
		t.Fatalf("failed to create manifest dir: %v", err)
	}
	if err := os.WriteFile(filepath.Join(manifestDir, "manifest.json"), []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write manifest: %v", err)
	}
}
