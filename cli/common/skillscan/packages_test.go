package skillscan

import (
	"os"
	"path/filepath"
	"reflect"
	"testing"
)

// Tests that source roots are enumerated through package identity search results in precedence order.
func TestEnumerateSourceRootsUsesPackageSearchResults(t *testing.T) {
	projectRoot := t.TempDir()
	writeTestPackageJSON(t, filepath.Join(projectRoot, "Packages", "src"), packageName)
	if err := os.MkdirAll(filepath.Join(projectRoot, "Packages", "src", "Editor", "FirstPartyTools"), 0o755); err != nil {
		t.Fatalf("failed to create package marker: %v", err)
	}
	projectPackageRoot := filepath.Join(projectRoot, "Packages", "local-package")
	if err := os.MkdirAll(projectPackageRoot, 0o755); err != nil {
		t.Fatalf("failed to create project package: %v", err)
	}
	manifestPackageRoot := filepath.Join(t.TempDir(), "manifest-local-package")
	if err := os.MkdirAll(manifestPackageRoot, 0o755); err != nil {
		t.Fatalf("failed to create manifest package: %v", err)
	}
	writeManifest(
		t,
		projectRoot,
		`{"dependencies":{"com.example.manifest-local":"file:`+filepath.ToSlash(manifestPackageRoot)+`","com.example.cached":"1.0.0"}}`)
	cachedPackageRoot := filepath.Join(projectRoot, "Library", "PackageCache", "com.example.cached@1.0.0")
	if err := os.MkdirAll(cachedPackageRoot, 0o755); err != nil {
		t.Fatalf("failed to create cached package: %v", err)
	}

	sourceRoots := EnumerateSourceRoots(projectRoot)

	actualPaths := sourceRootPaths(sourceRoots)
	expectedPaths := []string{
		filepath.Join(projectRoot, "Packages", "src", "Editor", "CliOnlyTools~"),
		filepath.Join(projectRoot, "Assets"),
		projectPackageRoot,
		filepath.Join(projectRoot, "Packages", "src"),
		manifestPackageRoot,
		cachedPackageRoot,
	}
	if !reflect.DeepEqual(actualPaths, cleanPaths(expectedPaths)) {
		t.Fatalf("source roots mismatch:\nactual:   %#v\nexpected: %#v", actualPaths, cleanPaths(expectedPaths))
	}
}

// Tests that local manifest dependencies exclude stale roots with the same identity from PackageCache.
func TestEnumeratePackageSearchResultsExcludesStaleCacheForLocalDependencies(t *testing.T) {
	for _, dependencyPrefix := range []string{"file:", "path:"} {
		t.Run(dependencyPrefix, func(t *testing.T) {
			projectRoot := t.TempDir()
			localPackageRoot := filepath.Join(t.TempDir(), "local-package")
			if err := os.MkdirAll(localPackageRoot, 0o755); err != nil {
				t.Fatalf("failed to create local package: %v", err)
			}
			staleCacheRoot := filepath.Join(
				projectRoot,
				"Library",
				"PackageCache",
				"com.example.local@1.0.0")
			if err := os.MkdirAll(staleCacheRoot, 0o755); err != nil {
				t.Fatalf("failed to create stale cache package: %v", err)
			}
			writeManifest(
				t,
				projectRoot,
				`{"dependencies":{"com.example.local":"`+dependencyPrefix+
					filepath.ToSlash(localPackageRoot)+`"}}`)

			results := EnumeratePackageSearchResults(projectRoot)

			actual := packageResultSummaries(results)
			expected := []string{"com.example.local|" + filepath.Clean(localPackageRoot)}
			if !reflect.DeepEqual(actual, expected) {
				t.Fatalf("package results mismatch:\nactual:   %#v\nexpected: %#v", actual, expected)
			}
		})
	}
}

// Tests that package search results expose stable package identities for direct, manifest, and cached roots.
func TestEnumeratePackageSearchResultsCapturesPackageIdentities(t *testing.T) {
	projectRoot := t.TempDir()
	writeTestPackageJSON(t, filepath.Join(projectRoot, "Packages", "src"), packageName)
	manifestPackageRoot := filepath.Join(t.TempDir(), "manifest-local-package")
	if err := os.MkdirAll(manifestPackageRoot, 0o755); err != nil {
		t.Fatalf("failed to create manifest package: %v", err)
	}
	writeManifest(
		t,
		projectRoot,
		`{"dependencies":{"com.example.manifest-local":"file:`+filepath.ToSlash(manifestPackageRoot)+`","com.example.cached":"1.0.0"}}`)
	cachedPackageRoot := filepath.Join(projectRoot, "Library", "PackageCache", "com.example.cached@1.0.0")
	if err := os.MkdirAll(cachedPackageRoot, 0o755); err != nil {
		t.Fatalf("failed to create cached package: %v", err)
	}

	results := EnumeratePackageSearchResults(projectRoot)

	actual := packageResultSummaries(results)
	expected := []string{
		packageName + "|" + filepath.Join(projectRoot, "Packages", "src"),
		"com.example.manifest-local|" + filepath.Clean(manifestPackageRoot),
		"com.example.cached|" + filepath.Clean(cachedPackageRoot),
	}
	if !reflect.DeepEqual(actual, cleanSummaries(expected)) {
		t.Fatalf("package results mismatch:\nactual:   %#v\nexpected: %#v", actual, cleanSummaries(expected))
	}
}

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

// Tests that marker-only project packages are not treated as the Unity CLI Loop package unless they are Packages/src.
func TestResolvePackageRootIgnoresMarkerOnlyUnrelatedProjectPackage(t *testing.T) {
	projectRoot := t.TempDir()
	unrelatedPackageRoot := filepath.Join(projectRoot, "Packages", "unrelated-package")
	markerPath := filepath.Join(unrelatedPackageRoot, "Editor", "FirstPartyTools")
	if err := os.MkdirAll(markerPath, 0o755); err != nil {
		t.Fatalf("failed to create marker path: %v", err)
	}

	actualRoot := ResolvePackageRoot(projectRoot)
	if actualRoot != "" {
		t.Fatalf("unrelated marker-only package should not resolve as package root: %s", actualRoot)
	}
}

// Tests that the historical Packages/src package root takes precedence over named package folders.
func TestResolvePackageRootPrefersPackagesSrc(t *testing.T) {
	projectRoot := t.TempDir()
	srcRoot := filepath.Join(projectRoot, "Packages", "src")
	namedRoot := filepath.Join(projectRoot, "Packages", packageName)
	for _, candidateRoot := range []string{srcRoot, namedRoot} {
		markerPath := filepath.Join(candidateRoot, "Editor", "FirstPartyTools")
		if err := os.MkdirAll(markerPath, 0o755); err != nil {
			t.Fatalf("failed to create marker path: %v", err)
		}
	}

	actualRoot := filepath.Clean(ResolvePackageRoot(projectRoot))
	expectedRoot := filepath.Clean(srcRoot)
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

func sourceRootPaths(sourceRoots []SkillSourceRoot) []string {
	paths := []string{}
	for _, sourceRoot := range sourceRoots {
		paths = append(paths, filepath.Clean(sourceRoot.Path))
	}
	return paths
}

func packageResultSummaries(results []PackageSearchResult) []string {
	summaries := []string{}
	for _, result := range results {
		summaries = append(summaries, result.Identity.Name+"|"+filepath.Clean(result.Root))
	}
	return summaries
}

func cleanPaths(paths []string) []string {
	cleaned := []string{}
	for _, path := range paths {
		cleaned = append(cleaned, filepath.Clean(path))
	}
	return cleaned
}

func cleanSummaries(summaries []string) []string {
	cleaned := []string{}
	for _, summary := range summaries {
		cleaned = append(cleaned, filepath.Clean(summary))
	}
	return cleaned
}

func writeTestPackageJSON(t *testing.T, packageRoot string, name string) {
	t.Helper()
	if err := os.MkdirAll(packageRoot, 0o755); err != nil {
		t.Fatalf("failed to create package root: %v", err)
	}
	content := `{"name":"` + name + `"}`
	if err := os.WriteFile(filepath.Join(packageRoot, "package.json"), []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write package.json: %v", err)
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
