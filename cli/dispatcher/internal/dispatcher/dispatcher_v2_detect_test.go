package dispatcher

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"os"
	"path/filepath"
	"testing"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

func TestDetectV2DispatcherProjectFindsPackageCacheVersion(t *testing.T) {
	// Verifies a V2 package is detected from package.json even when its cache directory has a hash suffix.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
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

func TestDetectV2DispatcherProjectSkipsProjectWithPin(t *testing.T) {
	// Verifies a project with a dispatcher pin is not classified as V2.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "abc123", "2.2.0")
	writeDispatcherProjectPin(t, projectRoot, "3.0.0")

	v2Project, err := detectV2DispatcherProject(projectRoot)
	if err != nil {
		t.Fatalf("detect V2 project: %v", err)
	}
	if v2Project.IsV2 {
		t.Fatalf("unexpected V2 project: %#v", v2Project)
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

func writeV2PackageManifest(t *testing.T, projectRoot string) {
	t.Helper()
	manifestPath := filepath.Join(projectRoot, "Packages", "manifest.json")
	if err := os.MkdirAll(filepath.Dir(manifestPath), 0o755); err != nil {
		t.Fatalf("create Packages directory: %v", err)
	}
	content := "{\n  \"dependencies\": {\n    \"" + dispatcherUnityPackageName + "\": \"https://example.invalid/package.git\"\n  }\n}\n"
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
	lockPath := filepath.Join(projectRoot, "Packages", "packages-lock.json")
	content := "{\n  \"dependencies\": {\n    \"" + dispatcherUnityPackageName + "\": {\n      \"version\": \"" + version + "\"\n    }\n  }\n}\n"
	if err := os.WriteFile(lockPath, []byte(content), 0o644); err != nil {
		t.Fatalf("write packages-lock.json: %v", err)
	}
}
