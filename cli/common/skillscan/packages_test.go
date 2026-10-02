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

// Tests that a relative file: dependency resolves against the Packages folder, the way Unity reads it.
func TestResolveLocalDependencyPathResolvesRelativePathsAgainstPackagesFolder(t *testing.T) {
	projectRoot := filepath.Join(t.TempDir(), "workspace", "project")

	resolved := resolveLocalDependencyPath("file:../../sibling/Packages/src", projectRoot)

	expected := filepath.Clean(filepath.Join(projectRoot, "Packages", "..", "..", "sibling", "Packages", "src"))
	if resolved != expected {
		t.Fatalf("resolved path mismatch:\nactual:   %q\nexpected: %q", resolved, expected)
	}
}

// Tests that a relative path: dependency uses the same Packages-relative base as file:.
func TestResolveLocalDependencyPathResolvesRelativePathPrefixAgainstPackagesFolder(t *testing.T) {
	projectRoot := filepath.Join(t.TempDir(), "workspace", "project")

	resolved := resolveLocalDependencyPath("path:../sibling-package", projectRoot)

	expected := filepath.Clean(filepath.Join(projectRoot, "Packages", "..", "sibling-package"))
	if resolved != expected {
		t.Fatalf("resolved path mismatch:\nactual:   %q\nexpected: %q", resolved, expected)
	}
}

// Tests that an absolute dependency path is used as written, with no project directory prepended.
func TestResolveLocalDependencyPathKeepsAbsolutePaths(t *testing.T) {
	projectRoot := t.TempDir()
	// Manifests spell paths with forward slashes on every platform, and an absolute path is
	// meant to pass through untouched, so the written form is what must come back.
	dependencyPath := filepath.ToSlash(filepath.Join(t.TempDir(), "absolute-package"))

	resolved := resolveLocalDependencyPath("file:"+dependencyPath, projectRoot)

	if resolved != dependencyPath {
		t.Fatalf("resolved path mismatch:\nactual:   %q\nexpected: %q", resolved, dependencyPath)
	}
}

// Tests that the file:// form keeps resolving to the absolute path that follows the slashes.
func TestResolveLocalDependencyPathKeepsFileSchemeAbsolutePaths(t *testing.T) {
	projectRoot := t.TempDir()
	dependencyPath := filepath.ToSlash(filepath.Join(t.TempDir(), "scheme-package"))

	resolved := resolveLocalDependencyPath("file://"+dependencyPath, projectRoot)

	if resolved != dependencyPath {
		t.Fatalf("resolved path mismatch:\nactual:   %q\nexpected: %q", resolved, dependencyPath)
	}
}

// Tests that a package reached through a relative manifest entry is enumerated at its real location.
func TestEnumeratePackageSearchResultsResolvesRelativeManifestDependency(t *testing.T) {
	workspaceRoot := t.TempDir()
	projectRoot := filepath.Join(workspaceRoot, "project")
	siblingPackageRoot := filepath.Join(workspaceRoot, "sibling", "Packages", "src")
	if err := os.MkdirAll(siblingPackageRoot, 0o755); err != nil {
		t.Fatalf("failed to create sibling package: %v", err)
	}
	writeManifest(
		t,
		projectRoot,
		`{"dependencies":{"com.example.sibling":"file:../../sibling/Packages/src"}}`)

	results := EnumeratePackageSearchResults(projectRoot)

	actual := packageResultSummaries(results)
	expected := []string{"com.example.sibling|" + filepath.Clean(siblingPackageRoot)}
	if !reflect.DeepEqual(actual, expected) {
		t.Fatalf("package results mismatch:\nactual:   %#v\nexpected: %#v", actual, expected)
	}
}

func mkdirAll(t *testing.T, path string) {
	t.Helper()
	if err := os.MkdirAll(path, 0o755); err != nil {
		t.Fatalf("failed to create %s: %v", path, err)
	}
}

// Tests that Editor folders are found down to the depth limit, sorted, and never inside excluded or Editor folders.
func TestFindEditorFoldersHonorsDepthAndExclusions(t *testing.T) {
	basePath := t.TempDir()
	mkdirAll(t, filepath.Join(basePath, "Editor", "Editor"))
	mkdirAll(t, filepath.Join(basePath, "B", "Editor"))
	mkdirAll(t, filepath.Join(basePath, "A", "One", "Two", "Editor"))
	mkdirAll(t, filepath.Join(basePath, "A", "One", "Two", "Three", "Editor"))
	// A-B sorts before A/ ('-' < '/'), so a depth-first walk and the sorted result disagree.
	mkdirAll(t, filepath.Join(basePath, "A-B", "Editor"))
	mkdirAll(t, filepath.Join(basePath, "node_modules", "Editor"))
	mkdirAll(t, filepath.Join(basePath, "C"))
	if err := os.WriteFile(filepath.Join(basePath, "C", "Editor"), []byte("not a folder"), 0o644); err != nil {
		t.Fatalf("failed to write file: %v", err)
	}

	actual := FindEditorFolders(basePath, SkillSearchMaxDepth)

	expected := []string{
		filepath.Join(basePath, "A-B", "Editor"),
		filepath.Join(basePath, "A", "One", "Two", "Editor"),
		filepath.Join(basePath, "B", "Editor"),
		filepath.Join(basePath, "Editor"),
	}
	if !reflect.DeepEqual(actual, expected) {
		t.Fatalf("editor folders mismatch:\nactual:   %#v\nexpected: %#v", actual, expected)
	}
}

// Tests that a missing base path yields an empty, non-nil folder list.
func TestFindEditorFoldersReturnsEmptyForMissingBase(t *testing.T) {
	actual := FindEditorFolders(filepath.Join(t.TempDir(), "missing"), SkillSearchMaxDepth)

	if actual == nil || len(actual) != 0 {
		t.Fatalf("expected an empty folder list, got %#v", actual)
	}
}

// Tests that the package is found in PackageCache by directory name alone when no manifest lists it,
// and that a nested Packages/src inside the cached directory is used as its root.
func TestFindUnityCliLoopPackageFallsBackToPackageCacheDirectoryName(t *testing.T) {
	projectRoot := t.TempDir()
	cacheDir := filepath.Join(projectRoot, "Library", "PackageCache")
	cachedRoot := filepath.Join(cacheDir, packageNameAlias+"@1.0.0", "Packages", "src")
	mkdirAll(t, filepath.Join(cachedRoot, "Editor", "FirstPartyTools"))
	mkdirAll(t, filepath.Join(cacheDir, "com.example.unrelated@1.0.0"))
	cachedFile := filepath.Join(cacheDir, packageName+"@file")
	if err := os.WriteFile(cachedFile, []byte("not a folder"), 0o644); err != nil {
		t.Fatalf("failed to write file: %v", err)
	}
	for _, searchResult := range EnumeratePackageSearchResults(projectRoot) {
		if filepath.Clean(searchResult.Root) == filepath.Clean(cachedFile) {
			t.Fatalf("a file in PackageCache must not be listed as a package: %#v", searchResult)
		}
	}

	result, ok := FindUnityCliLoopPackage(projectRoot)

	if !ok {
		t.Fatal("cached package should be found")
	}
	if result.Identity.Name != packageNameAlias || filepath.Clean(result.Root) != filepath.Clean(cachedRoot) {
		t.Fatalf("unexpected package result: %#v", result)
	}
}

// Tests that two package candidates with the same priority resolve to the lexicographically smaller
// root, even when the larger root is enumerated first.
func TestFindUnityCliLoopPackageBreaksPriorityTiesByRoot(t *testing.T) {
	baseRoot := t.TempDir()
	projectRoot := filepath.Join(baseRoot, "z-project")
	directRoot := filepath.Join(projectRoot, "Packages", "custom")
	writeTestPackageJSON(t, directRoot, packageName)
	mkdirAll(t, filepath.Join(directRoot, "Editor", "FirstPartyTools"))
	externalRoot := filepath.Join(baseRoot, "a-external")
	mkdirAll(t, filepath.Join(externalRoot, "Editor", "FirstPartyTools"))
	writeManifest(t, projectRoot, `{"dependencies":{"`+packageName+`":"file:`+filepath.ToSlash(externalRoot)+`"}}`)

	result, ok := FindUnityCliLoopPackage(projectRoot)

	if !ok || filepath.Clean(result.Root) != filepath.Clean(externalRoot) {
		t.Fatalf("expected %s, got %#v (ok=%v)", externalRoot, result, ok)
	}
}

// Tests that a manifest entry with no name is dropped and that a package reached both directly and
// through the manifest under the same identity is listed once.
func TestEnumeratePackageSearchResultsDropsNamelessAndDuplicateEntries(t *testing.T) {
	projectRoot := t.TempDir()
	localRoot := filepath.Join(projectRoot, "Packages", "local")
	writeTestPackageJSON(t, localRoot, "com.example.local")
	writeManifest(t, projectRoot, `{"dependencies":{"":"file:local","com.example.local":"file:local"}}`)

	actual := packageResultSummaries(EnumeratePackageSearchResults(projectRoot))

	expected := []string{"com.example.local|" + filepath.Clean(localRoot)}
	if !reflect.DeepEqual(actual, expected) {
		t.Fatalf("package results mismatch:\nactual:   %#v\nexpected: %#v", actual, expected)
	}
}

// Tests that manifest entries sharing one local root are ordered by identity name.
func TestEnumeratePackageSearchResultsOrdersSharedRootsByIdentity(t *testing.T) {
	projectRoot := t.TempDir()
	sharedRoot := filepath.Join(t.TempDir(), "shared")
	mkdirAll(t, sharedRoot)
	sharedValue := "file:" + filepath.ToSlash(sharedRoot)
	writeManifest(t, projectRoot, `{"dependencies":{"com.example.zeta":"`+sharedValue+`","com.example.alpha":"`+sharedValue+`"}}`)

	actual := packageResultSummaries(EnumeratePackageSearchResults(projectRoot))

	expected := []string{
		"com.example.alpha|" + filepath.Clean(sharedRoot),
		"com.example.zeta|" + filepath.Clean(sharedRoot),
	}
	if !reflect.DeepEqual(actual, expected) {
		t.Fatalf("package results mismatch:\nactual:   %#v\nexpected: %#v", actual, expected)
	}
}

// Tests that an unparsable or mistyped manifest, or one without dependencies, yields no dependencies
// instead of a partially decoded set.
func TestReadManifestDependenciesReturnsEmptyForUnusableManifests(t *testing.T) {
	for name, content := range map[string]string{
		"invalid json":      `{"dependencies":`,
		"null dependencies": `{"dependencies":null}`,
		"mistyped entry":    `{"dependencies":{"com.example.local":"file:local","bad":1}}`,
	} {
		t.Run(name, func(t *testing.T) {
			projectRoot := t.TempDir()
			writeManifest(t, projectRoot, content)

			dependencies := readManifestDependencies(projectRoot)

			if dependencies == nil || len(dependencies) != 0 {
				t.Fatalf("expected empty dependencies, got %#v", dependencies)
			}
		})
	}
}

// Tests that local dependency values that are not file:/path: references or are blank resolve to nothing.
func TestResolveLocalDependencyPathRejectsNonLocalValues(t *testing.T) {
	for _, value := range []string{"1.0.0", "file:", "path:   "} {
		if resolved := resolveLocalDependencyPath(value, t.TempDir()); resolved != "" {
			t.Errorf("resolveLocalDependencyPath(%q) = %q, want empty", value, resolved)
		}
	}
}

// Tests that the cache directory version suffix is stripped and a name without one is kept.
func TestPackageIdentityNameFromCacheDir(t *testing.T) {
	cases := map[string]string{
		packageName + "@1.2.3": packageName,
		packageName:            packageName,
	}
	for dirName, expected := range cases {
		if actual := packageIdentityNameFromCacheDir(dirName); actual != expected {
			t.Errorf("packageIdentityNameFromCacheDir(%q) = %q, want %q", dirName, actual, expected)
		}
	}
}

// Tests the package root priority order: Packages/src, the package name, its alias, other locations, then PackageCache.
func TestUnityCliLoopPackagePriority(t *testing.T) {
	projectRoot := t.TempDir()
	cases := map[string]int{
		filepath.Join(projectRoot, "Packages", "src"):                               0,
		filepath.Join(projectRoot, "Packages", packageName):                         1,
		filepath.Join(projectRoot, "Packages", packageNameAlias):                    2,
		filepath.Join(t.TempDir(), "elsewhere"):                                     10,
		filepath.Join(projectRoot, "Library", "PackageCache", packageName+"@1.0.0"): 20,
	}
	for packageRoot, expected := range cases {
		if actual := unityCliLoopPackagePriority(projectRoot, packageRoot); actual != expected {
			t.Errorf("priority(%q) = %d, want %d", packageRoot, actual, expected)
		}
	}
}
