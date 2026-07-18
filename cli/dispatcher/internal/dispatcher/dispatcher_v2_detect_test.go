package dispatcher

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
)

func TestDetectV2DispatcherProjectFindsPackageCacheVersion(t *testing.T) {
	// Verifies a V2 package is detected from package.json even when its cache directory has a hash suffix.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writePackagesLockWithSource(t, projectRoot, "https://example.invalid/package.git", "git")
	writeV2PackageCachePackageJSON(t, projectRoot, "abc123", "2.2.0")

	v2Project, err := detectV2DispatcherProject(projectRoot)
	if err != nil {
		t.Fatalf("detect V2 project: %v", err)
	}
	if !v2Project.IsV2 {
		t.Fatal("expected V2 project")
	}
	if v2Project.PackageVersion != "2.2.0" {
		t.Fatalf("package version = %q, want 2.2.0", v2Project.PackageVersion)
	}
}

func TestDetectV2DispatcherProjectFindsPackageCacheVersionWithBracketedProjectPath(t *testing.T) {
	// Verifies PackageCache discovery treats glob characters in project paths literally.
	projectRoot := filepath.Join(t.TempDir(), "project[legacy]")
	for _, directory := range []string{"Assets", "ProjectSettings"} {
		if err := os.MkdirAll(filepath.Join(projectRoot, directory), 0o755); err != nil {
			t.Fatalf("create Unity project directory: %v", err)
		}
	}
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "abc123", "2.2.0")

	v2Project, err := detectV2DispatcherProject(projectRoot)
	if err != nil {
		t.Fatalf("detect V2 project: %v", err)
	}
	if !v2Project.IsV2 {
		t.Fatal("expected V2 project")
	}
}

func TestDetectV2DispatcherProjectSkipsInvalidPackageCacheEntry(t *testing.T) {
	// Verifies an invalid stale PackageCache entry does not hide another valid V2 package.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	invalidPackagePath := filepath.Join(projectRoot, "Library", "PackageCache", dispatcherUnityPackageName+"@broken", "package.json")
	if err := os.MkdirAll(filepath.Dir(invalidPackagePath), 0o755); err != nil {
		t.Fatalf("create invalid package cache: %v", err)
	}
	if err := os.WriteFile(invalidPackagePath, []byte("{"), 0o644); err != nil {
		t.Fatalf("write invalid package.json: %v", err)
	}
	writeV2PackageCachePackageJSON(t, projectRoot, "valid", "2.2.0")

	v2Project, err := detectV2DispatcherProject(projectRoot)
	if err != nil {
		t.Fatalf("detect V2 project: %v", err)
	}
	if !v2Project.IsV2 {
		t.Fatal("expected V2 project")
	}
}

func TestDetectV2DispatcherProjectPrefersV2PackageLockVersionOverStaleCache(t *testing.T) {
	// Verifies the resolved V2 lock version wins when PackageCache also contains an older version.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "aaa-older", "2.1.0")
	writeV2PackageCachePackageJSON(t, projectRoot, "zzz-current", "2.2.0")
	writeV2PackagesLock(t, projectRoot, "2.2.0")

	v2Project, err := detectV2DispatcherProject(projectRoot)
	if err != nil {
		t.Fatalf("detect V2 project: %v", err)
	}
	if !v2Project.IsV2 || v2Project.PackageVersion != "2.2.0" {
		t.Fatalf("V2 project = %#v, want package version 2.2.0", v2Project)
	}
}

func TestDetectV2DispatcherProjectPrefersV3PackageLockVersionOverStaleV2Cache(t *testing.T) {
	// Verifies a resolved V3 lock version prevents a stale V2 cache entry from causing delegation.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "stale", "2.2.0")
	writeV2PackagesLock(t, projectRoot, "3.0.0")

	v2Project, err := detectV2DispatcherProject(projectRoot)
	if err != nil {
		t.Fatalf("detect V2 project: %v", err)
	}
	if v2Project.IsV2 {
		t.Fatalf("unexpected V2 project: %#v", v2Project)
	}
}

func TestDetectV2DispatcherProjectRejectsAmbiguousPackageCacheVersions(t *testing.T) {
	// Verifies a git dependency with multiple cached V2 versions is identified without choosing one arbitrarily.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "older", "2.1.0")
	writeV2PackageCachePackageJSON(t, projectRoot, "newer", "2.2.0")

	v2Project, err := detectV2DispatcherProject(projectRoot)
	if err != nil {
		t.Fatalf("detect V2 project: %v", err)
	}
	if !v2Project.IsV2 || v2Project.PackageVersion != "" {
		t.Fatalf("ambiguous V2 project = %#v", v2Project)
	}
	assertStringSliceEqual(t, v2Project.PackageVersionCandidates, []string{"2.1.0", "2.2.0"})
}

func TestDetectV2DispatcherProjectUsesResolvedV2PackageDespiteStalePin(t *testing.T) {
	// Verifies a resolved V2 package wins over a stale pin left by a previous V3 package.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackagesLock(t, projectRoot, "2.2.0")
	writeDispatcherProjectPin(t, projectRoot, "3.0.0")

	v2Project, err := detectV2DispatcherProject(projectRoot)
	if err != nil {
		t.Fatalf("detect V2 project: %v", err)
	}
	if !v2Project.IsV2 || v2Project.PackageVersion != "2.2.0" {
		t.Fatalf("V2 project = %#v, want package version 2.2.0", v2Project)
	}
}

func TestDetectV2DispatcherProjectSkipsProjectWithoutPackage(t *testing.T) {
	// Verifies a missing Unity package does not classify a pinless project as V2.
	projectRoot := createDispatcherUnityProject(t)

	v2Project, err := detectV2DispatcherProject(projectRoot)
	if err != nil {
		t.Fatalf("detect V2 project: %v", err)
	}
	if v2Project.IsV2 {
		t.Fatalf("unexpected V2 project: %#v", v2Project)
	}
}

func TestDetectV2DispatcherProjectFindsPackageLockVersionWithoutPackageCache(t *testing.T) {
	// Verifies a V2 package is detected from packages-lock.json when PackageCache is unavailable.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackagesLock(t, projectRoot, "2.2.0")

	v2Project, err := detectV2DispatcherProject(projectRoot)
	if err != nil {
		t.Fatalf("detect V2 project: %v", err)
	}
	if !v2Project.IsV2 {
		t.Fatal("expected V2 project")
	}
	if v2Project.PackageVersion != "2.2.0" {
		t.Fatalf("package version = %q, want 2.2.0", v2Project.PackageVersion)
	}
}

func TestDetectV2DispatcherProjectSkipsV3PackageLockVersion(t *testing.T) {
	// Verifies a non-V2 packages-lock.json version does not classify a project as V2.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackagesLock(t, projectRoot, "3.0.0")

	v2Project, err := detectV2DispatcherProject(projectRoot)
	if err != nil {
		t.Fatalf("detect V2 project: %v", err)
	}
	if v2Project.IsV2 {
		t.Fatalf("unexpected V2 project: %#v", v2Project)
	}
}

func TestDetectV2DispatcherProjectRejectsEmptyLocalPackageLockVersion(t *testing.T) {
	// Verifies an existing local lock entry cannot use a stale V2 cache when its version is empty.
	projectRoot := createDispatcherUnityProject(t)
	writePackageManifest(t, projectRoot, "file:../v3-package")
	writePackagesLockWithSource(t, projectRoot, "", "local")
	writeV2PackageCachePackageJSON(t, projectRoot, "stale", "2.2.0")

	v2Project, err := detectV2DispatcherProject(projectRoot)
	if err != nil {
		t.Fatalf("detect V2 project: %v", err)
	}
	if v2Project.IsV2 {
		t.Fatalf("unexpected V2 project: %#v", v2Project)
	}
}

func TestDetectV2DispatcherProjectRejectsNonGitManifestFallbackWithoutLockEntry(t *testing.T) {
	// Verifies local, embedded, and unknown manifest sources cannot use stale V2 cache data without a lock entry.
	testCases := []struct {
		name       string
		dependency string
	}{
		{name: "local", dependency: "file:../v3-package"},
		{name: "embedded", dependency: "file:../Packages/v3-package"},
		{name: "unknown", dependency: "vendor:v3-package"},
	}
	for _, testCase := range testCases {
		t.Run(testCase.name, func(t *testing.T) {
			projectRoot := createDispatcherUnityProject(t)
			writePackageManifest(t, projectRoot, testCase.dependency)
			writeV2PackageCachePackageJSON(t, projectRoot, "stale", "2.2.0")

			v2Project, err := detectV2DispatcherProject(projectRoot)
			if err != nil {
				t.Fatalf("detect V2 project: %v", err)
			}
			if v2Project.IsV2 {
				t.Fatalf("unexpected V2 project: %#v", v2Project)
			}
		})
	}
}

func TestIsDispatcherGitPackageDependencyAcceptsPathQueries(t *testing.T) {
	// Verifies Unity Git dependencies with path queries remain eligible for PackageCache fallback.
	testCases := []struct {
		name       string
		dependency string
	}{
		{name: "scp", dependency: "git@example.invalid:package.git?path=/sub"},
		{name: "https", dependency: "https://example.invalid/package.git?path=/sub"},
	}
	for _, testCase := range testCases {
		t.Run(testCase.name, func(t *testing.T) {
			dependency, err := json.Marshal(testCase.dependency)
			if err != nil {
				t.Fatalf("marshal dependency: %v", err)
			}
			if !isDispatcherGitPackageDependency(dependency) {
				t.Fatalf("Git dependency was rejected: %s", testCase.dependency)
			}
		})
	}
}

func TestRunDispatcherReportsV2ProjectGuidanceWhenPinIsMissing(t *testing.T) {
	// Verifies pinless V2 projects receive migration guidance instead of the missing-pin error.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "abc123", "2.2.0")
	t.Chdir(projectRoot)

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	deps := defaultDispatcherRunDeps()
	deps.runV2CLI = func(context.Context, string, []string, io.Writer, io.Writer) (int, error) {
		return 0, os.ErrNotExist
	}
	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, &stdout, &stderr, deps)

	if code != 1 {
		t.Fatalf("exit code = %d, want 1; stderr=%s", code, stderr.String())
	}
	envelope := clierrors.CLIErrorEnvelope{}
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("parse error envelope: %v; stderr=%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != clierrors.ErrorCodeV2ProjectDetected {
		t.Fatalf("error code = %q, want %q", envelope.Error.ErrorCode, clierrors.ErrorCodeV2ProjectDetected)
	}
	if len(envelope.Error.NextActions) < 2 {
		t.Fatalf("next actions = %#v, want Node and npx guidance", envelope.Error.NextActions)
	}
	if envelope.Error.Details["Cause"] != os.ErrNotExist.Error() {
		t.Fatalf("cause = %#v, want %q", envelope.Error.Details["Cause"], os.ErrNotExist.Error())
	}
}

func TestRunDispatcherReportsVersionResolutionGuidanceForAmbiguousV2Cache(t *testing.T) {
	// Verifies ambiguous V2 cache versions produce V2-specific recovery guidance without attempting delegation.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "older", "2.1.0")
	writeV2PackageCachePackageJSON(t, projectRoot, "newer", "2.2.0")
	t.Chdir(projectRoot)

	var stderr bytes.Buffer
	deps := defaultDispatcherRunDeps()
	deps.runV2CLI = func(context.Context, string, []string, io.Writer, io.Writer) (int, error) {
		t.Fatal("ambiguous V2 package version must not be delegated")
		return 0, nil
	}
	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, io.Discard, &stderr, deps)

	if code != 1 {
		t.Fatalf("exit code = %d, want 1; stderr=%s", code, stderr.String())
	}
	envelope := clierrors.CLIErrorEnvelope{}
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("parse error envelope: %v; stderr=%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != clierrors.ErrorCodeV2ProjectDetected {
		t.Fatalf("error code = %q, want %q", envelope.Error.ErrorCode, clierrors.ErrorCodeV2ProjectDetected)
	}
	for _, expected := range []string{"Packages/packages-lock.json", "npx uloop-cli@2", "2.1.0", "2.2.0"} {
		if !bytes.Contains(stderr.Bytes(), []byte(expected)) {
			t.Fatalf("V2 recovery guidance missing %q: %s", expected, stderr.String())
		}
	}
}

func TestRunDispatcherDelegatesResolvedV2PackageDespiteStalePin(t *testing.T) {
	// Verifies a resolved V2 lock version delegates before loading a stale V3 project-runner pin.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackagesLock(t, projectRoot, "2.2.0")
	writeDispatcherProjectPin(t, projectRoot, "3.0.0")
	t.Chdir(projectRoot)

	deps := defaultDispatcherRunDeps()
	deps.runV2CLI = func(ctx context.Context, version string, args []string, stdout io.Writer, stderr io.Writer) (int, error) {
		if version != "2.2.0" {
			t.Fatalf("V2 version = %q, want 2.2.0", version)
		}
		return 7, nil
	}
	deps.runRealCLI = func(context.Context, string, []string, io.Writer, io.Writer) int {
		t.Fatal("stale V3 pin must not run the project runner")
		return 0
	}

	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, io.Discard, io.Discard, deps)
	if code != 7 {
		t.Fatalf("exit code = %d, want 7", code)
	}
}

// Verifies launch stays in the native dispatcher for a V2 project while live commands still use the V2 CLI.
func TestRunDispatcherKeepsV2LaunchInNativeDispatcher(t *testing.T) {
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "abc123", "2.2.0")
	t.Chdir(projectRoot)

	deps := defaultDispatcherRunDeps()
	deps.runV2CLI = func(context.Context, string, []string, io.Writer, io.Writer) (int, error) {
		t.Fatal("V2 launch must not be delegated to the V2 CLI")
		return 0, nil
	}
	deps.launch.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return nil, nil
	}

	var stdout bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"launch", "--quit", projectRoot}, &stdout, io.Discard, deps)
	if code != 0 {
		t.Fatalf("V2 launch quit exit code = %d", code)
	}
	if !strings.Contains(stdout.String(), `"Quit": true`) {
		t.Fatalf("native V2 launch response missing quit result: %s", stdout.String())
	}
}

func TestRunDispatcherForwardsResolvedV3PackageToPinnedRunner(t *testing.T) {
	// Verifies a resolved V3 lock version keeps using the pinned project runner.
	projectRoot := createDispatcherUnityProject(t)
	cacheRoot := t.TempDir()
	writeV2PackageManifest(t, projectRoot)
	writeV2PackagesLock(t, projectRoot, "3.0.0")
	writeDispatcherProjectPin(t, projectRoot, "3.0.0")
	writeCachedDispatcherRealCLI(t, cacheRoot, "3.0.0")
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")
	t.Chdir(projectRoot)

	deps := defaultDispatcherRunDeps()
	deps.runV2CLI = func(context.Context, string, []string, io.Writer, io.Writer) (int, error) {
		t.Fatal("resolved V3 package must not delegate to V2")
		return 0, nil
	}
	forwarded := false
	deps.runRealCLI = func(context.Context, string, []string, io.Writer, io.Writer) int {
		forwarded = true
		return 9
	}

	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, io.Discard, io.Discard, deps)
	if code != 9 || !forwarded {
		t.Fatalf("V3 runner was not forwarded: code=%d forwarded=%v", code, forwarded)
	}
}

func TestRunDispatcherFallsBackToPinnedRunnerWhenV2DetectionFails(t *testing.T) {
	// Verifies a malformed package lock cannot block a healthy pinned V3 runner.
	projectRoot := createDispatcherUnityProject(t)
	cacheRoot := t.TempDir()
	writeV2PackageManifest(t, projectRoot)
	lockPath := filepath.Join(projectRoot, filepath.FromSlash(dispatcherPackagesLockRelativePath))
	if err := os.WriteFile(lockPath, []byte("{"), 0o644); err != nil {
		t.Fatalf("write malformed packages lock: %v", err)
	}
	writeDispatcherProjectPin(t, projectRoot, "3.0.0")
	writeCachedDispatcherRealCLI(t, cacheRoot, "3.0.0")
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")
	t.Chdir(projectRoot)

	deps := defaultDispatcherRunDeps()
	deps.runV2CLI = func(context.Context, string, []string, io.Writer, io.Writer) (int, error) {
		t.Fatal("failed V2 detection must not delegate")
		return 0, nil
	}
	forwarded := false
	deps.runRealCLI = func(context.Context, string, []string, io.Writer, io.Writer) int {
		forwarded = true
		return 11
	}

	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, io.Discard, io.Discard, deps)
	if code != 11 || !forwarded {
		t.Fatalf("V3 runner fallback failed: code=%d forwarded=%v", code, forwarded)
	}
}

func TestRunDispatcherKeepsPinnedRunnerForLocalPackageSource(t *testing.T) {
	// Verifies a local V3 package cannot be replaced by a stale V2 PackageCache entry.
	assertPackageLockSourceKeepsPinnedRunner(t, "file:../v3-package", "local")
}

func TestRunDispatcherKeepsPinnedRunnerForUnknownPackageSource(t *testing.T) {
	// Verifies an unknown package source fails open to the pinned runner instead of stale V2 cache data.
	assertPackageLockSourceKeepsPinnedRunner(t, "vendor:v3-package", "future-source")
}

func TestRunDispatcherDelegatesV2ProjectWithOriginalArguments(t *testing.T) {
	// Verifies V2 delegation preserves the original argument sequence.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "abc123", "2.2.0")
	t.Chdir(projectRoot)
	deps := defaultDispatcherRunDeps()
	var actualVersion string
	var actualArgs []string
	deps.runV2CLI = func(ctx context.Context, version string, args []string, stdout io.Writer, stderr io.Writer) (int, error) {
		actualVersion = version
		actualArgs = append([]string{}, args...)
		return 7, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"compile", "--project-path", projectRoot}, &stdout, &stderr, deps)

	if code != 7 {
		t.Fatalf("exit code = %d, want 7", code)
	}
	if actualVersion != "2.2.0" {
		t.Fatalf("V2 version = %q, want 2.2.0", actualVersion)
	}
	assertStringSliceEqual(t, actualArgs, []string{"compile", "--project-path", projectRoot})
}

func TestRunDispatcherDelegatesBareVersionForV2Project(t *testing.T) {
	// Verifies a V2 project receives its own CLI version for the Setup window check.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "abc123", "2.2.0")
	t.Chdir(projectRoot)
	deps := defaultDispatcherRunDeps()
	delegated := false
	deps.runV2CLI = func(ctx context.Context, version string, args []string, stdout io.Writer, stderr io.Writer) (int, error) {
		delegated = true
		assertStringSliceEqual(t, args, []string{"--version"})
		return 0, nil
	}

	code := runDispatcherWithDeps(context.Background(), []string{"--version"}, io.Discard, io.Discard, deps)
	if code != 0 || !delegated {
		t.Fatalf("V2 version was not delegated: code=%d delegated=%v", code, delegated)
	}
}

func TestRunDispatcherReturnsDispatcherVersionOutsideProject(t *testing.T) {
	// Verifies a version request outside a project remains handled by the dispatcher.
	t.Chdir(t.TempDir())
	deps := defaultDispatcherRunDeps()
	deps.runV2CLI = func(context.Context, string, []string, io.Writer, io.Writer) (int, error) {
		t.Fatal("V2 CLI must not run outside a project")
		return 0, nil
	}
	var stdout bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"--version"}, &stdout, io.Discard, deps)
	if code != 0 || stdout.String() != dispatcherVersion+"\n" {
		t.Fatalf("dispatcher version output = %q, code=%d", stdout.String(), code)
	}
}

func TestRunDispatcherDelegatesSkillsForV2Project(t *testing.T) {
	// Verifies the project-scoped skills command is delegated to the V2 CLI.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "abc123", "2.2.0")
	t.Chdir(projectRoot)
	deps := defaultDispatcherRunDeps()
	delegated := false
	deps.runV2CLI = func(ctx context.Context, version string, args []string, stdout io.Writer, stderr io.Writer) (int, error) {
		delegated = true
		assertStringSliceEqual(t, args, []string{"skills", "list"})
		return 0, nil
	}

	code := runDispatcherWithDeps(context.Background(), []string{"skills", "list"}, io.Discard, io.Discard, deps)
	if code != 0 || !delegated {
		t.Fatalf("V2 skills was not delegated: code=%d delegated=%v", code, delegated)
	}
}

func TestShouldKeepDispatcherProcessCommandKeepsUpdate(t *testing.T) {
	// Verifies global update remains owned by the V3 dispatcher.
	if !shouldKeepDispatcherProcessCommand([]string{"update"}) {
		t.Fatal("update must remain in the dispatcher")
	}
}

func writeV2PackageManifest(t *testing.T, projectRoot string) {
	t.Helper()
	writePackageManifest(t, projectRoot, "https://example.invalid/package.git")
}

func writePackageManifest(t *testing.T, projectRoot string, dependency string) {
	t.Helper()
	manifestPath := filepath.Join(projectRoot, "Packages", "manifest.json")
	if err := os.MkdirAll(filepath.Dir(manifestPath), 0o755); err != nil {
		t.Fatalf("create Packages directory: %v", err)
	}
	content := "{\n  \"dependencies\": {\n    \"" + dispatcherUnityPackageName + "\": \"" + dependency + "\"\n  }\n}\n"
	if err := os.WriteFile(manifestPath, []byte(content), 0o644); err != nil {
		t.Fatalf("write manifest: %v", err)
	}
}

func writeV2PackageCachePackageJSON(t *testing.T, projectRoot string, suffix string, version string) {
	t.Helper()
	packagePath := filepath.Join(projectRoot, "Library", "PackageCache", dispatcherUnityPackageName+"@"+suffix, "package.json")
	if err := os.MkdirAll(filepath.Dir(packagePath), 0o755); err != nil {
		t.Fatalf("create package cache: %v", err)
	}
	content := "{\n  \"version\": \"" + version + "\"\n}\n"
	if err := os.WriteFile(packagePath, []byte(content), 0o644); err != nil {
		t.Fatalf("write package.json: %v", err)
	}
}

func writeV2PackagesLock(t *testing.T, projectRoot string, version string) {
	t.Helper()
	writePackagesLockWithSource(t, projectRoot, version, "")
}

func writePackagesLockWithSource(t *testing.T, projectRoot string, version string, source string) {
	t.Helper()
	lockPath := filepath.Join(projectRoot, "Packages", "packages-lock.json")
	content := "{\n  \"dependencies\": {\n    \"" + dispatcherUnityPackageName + "\": {\n      \"version\": \"" + version + "\"\n    }\n  }\n}\n"
	if source != "" {
		content = "{\n  \"dependencies\": {\n    \"" + dispatcherUnityPackageName + "\": {\n      \"version\": \"" + version + "\",\n      \"source\": \"" + source + "\"\n    }\n  }\n}\n"
	}
	if err := os.WriteFile(lockPath, []byte(content), 0o644); err != nil {
		t.Fatalf("write packages-lock.json: %v", err)
	}
}

func assertPackageLockSourceKeepsPinnedRunner(t *testing.T, lockVersion string, source string) {
	t.Helper()
	projectRoot := createDispatcherUnityProject(t)
	cacheRoot := t.TempDir()
	writeV2PackageManifest(t, projectRoot)
	writePackagesLockWithSource(t, projectRoot, lockVersion, source)
	writeV2PackageCachePackageJSON(t, projectRoot, "stale", "2.2.0")
	writeDispatcherProjectPin(t, projectRoot, "3.0.0")
	writeCachedDispatcherRealCLI(t, cacheRoot, "3.0.0")
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")
	t.Chdir(projectRoot)

	deps := defaultDispatcherRunDeps()
	deps.runV2CLI = func(context.Context, string, []string, io.Writer, io.Writer) (int, error) {
		t.Fatal("non-git package source must not delegate using stale V2 cache data")
		return 0, nil
	}
	forwarded := false
	deps.runRealCLI = func(context.Context, string, []string, io.Writer, io.Writer) int {
		forwarded = true
		return 13
	}

	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, io.Discard, io.Discard, deps)
	if code != 13 || !forwarded {
		t.Fatalf("pinned runner was not used: code=%d forwarded=%v", code, forwarded)
	}
}
