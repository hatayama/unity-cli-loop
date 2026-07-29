package dispatcher

import (
	"bytes"
	"context"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// Verifies package help lists install, status, and --version.
func TestPackageHelpListsSubcommands(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{"package", "--help"}, &stdout, &stderr)
	if code != 0 {
		t.Fatalf("package help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{"install", "status", "--version", "--project-path"} {
		if !strings.Contains(output, expected) {
			t.Fatalf("package help missing %q:\n%s", expected, output)
		}
	}
}

// Verifies install writes registry and dependency using dist-tags.latest from the registry.
func TestPackageInstallWritesManifestUsingRegistryLatest(t *testing.T) {
	projectRoot := createPackageTestProject(t, barePackageManifest())
	restore := stubOpenUPMRegistry(t, `{"dist-tags":{"latest":"1.2.3"}}`)
	defer restore()

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(
		context.Background(),
		[]string{"package", "install", "--project-path", projectRoot},
		&stdout,
		&stderr,
	)
	if code != 0 {
		t.Fatalf("package install failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{
		"Added scoped registry https://package.openupm.com",
		"Added " + dispatcherUnityPackageName + " 1.2.3",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("install output missing %q:\n%s", expected, output)
		}
	}

	content := readPackageManifest(t, projectRoot)
	if !strings.Contains(content, `"url": "https://package.openupm.com"`) {
		t.Fatalf("registry missing from manifest:\n%s", content)
	}
	if !strings.Contains(content, `"`+dispatcherUnityPackageName+`": "1.2.3"`) {
		t.Fatalf("dependency missing from manifest:\n%s", content)
	}
}

// Verifies --version skips the registry HTTP lookup entirely.
func TestPackageInstallWithVersionSkipsRegistryLookup(t *testing.T) {
	projectRoot := createPackageTestProject(t, barePackageManifest())
	server := httptest.NewServer(http.HandlerFunc(func(_ http.ResponseWriter, _ *http.Request) {
		t.Fatal("registry must not be contacted when --version is set")
	}))
	t.Cleanup(server.Close)

	previousURL := openUPMRegistryBaseURL
	previousClient := packageRegistryHTTPClient
	openUPMRegistryBaseURL = server.URL
	packageRegistryHTTPClient = server.Client()
	t.Cleanup(func() {
		openUPMRegistryBaseURL = previousURL
		packageRegistryHTTPClient = previousClient
	})

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(
		context.Background(),
		[]string{"package", "install", "--version", "1.2.3", "--project-path", projectRoot},
		&stdout,
		&stderr,
	)
	if code != 0 {
		t.Fatalf("package install failed: code=%d stderr=%s", code, stderr.String())
	}
	content := readPackageManifest(t, projectRoot)
	if !strings.Contains(content, `"`+dispatcherUnityPackageName+`": "1.2.3"`) {
		t.Fatalf("dependency missing from manifest:\n%s", content)
	}
}

// Verifies a second install reports already installed and leaves the manifest unchanged.
func TestPackageInstallIsIdempotent(t *testing.T) {
	projectRoot := createPackageTestProject(t, barePackageManifest())
	restore := stubOpenUPMRegistry(t, `{"dist-tags":{"latest":"1.2.3"}}`)
	defer restore()

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	firstCode := RunDispatcher(
		context.Background(),
		[]string{"package", "install", "--project-path", projectRoot},
		&stdout,
		&stderr,
	)
	if firstCode != 0 {
		t.Fatalf("first install failed: code=%d stderr=%s", firstCode, stderr.String())
	}
	before := readPackageManifest(t, projectRoot)

	stdout.Reset()
	stderr.Reset()
	secondCode := RunDispatcher(
		context.Background(),
		[]string{"package", "install", "--project-path", projectRoot},
		&stdout,
		&stderr,
	)
	if secondCode != 0 {
		t.Fatalf("second install failed: code=%d stderr=%s", secondCode, stderr.String())
	}
	if !strings.Contains(stdout.String(), "already installed") {
		t.Fatalf("expected already installed message:\n%s", stdout.String())
	}
	after := readPackageManifest(t, projectRoot)
	if before != after {
		t.Fatalf("manifest changed on idempotent install:\nbefore:\n%s\nafter:\n%s", before, after)
	}
}

// Verifies a project without Packages/manifest.json returns PACKAGE_MANIFEST_INVALID.
func TestPackageInstallFailsWithoutManifest(t *testing.T) {
	projectRoot := t.TempDir()
	for _, directory := range []string{"Assets", "ProjectSettings", "Packages"} {
		if err := os.MkdirAll(filepath.Join(projectRoot, directory), 0o755); err != nil {
			t.Fatalf("mkdir failed: %v", err)
		}
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(
		context.Background(),
		[]string{"package", "install", "--version", "1.2.3", "--project-path", projectRoot},
		&stdout,
		&stderr,
	)
	if code == 0 {
		t.Fatal("expected failure without manifest")
	}
	if !strings.Contains(stderr.String(), "PACKAGE_MANIFEST_INVALID") {
		t.Fatalf("expected PACKAGE_MANIFEST_INVALID:\n%s", stderr.String())
	}
}

// Verifies status reports not installed when the package is absent.
func TestPackageStatusReportsNotInstalled(t *testing.T) {
	projectRoot := createPackageTestProject(t, barePackageManifest())
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(
		context.Background(),
		[]string{"package", "status", "--project-path", projectRoot},
		&stdout,
		&stderr,
	)
	if code != 0 {
		t.Fatalf("status failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{
		"Package: " + dispatcherUnityPackageName,
		"Scoped registry: not installed",
		"Dependency: not installed",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("status output missing %q:\n%s", expected, output)
		}
	}
}

// Verifies status reports the installed dependency version and registry.
func TestPackageStatusReportsInstalledVersion(t *testing.T) {
	projectRoot := createPackageTestProject(t, installedPackageManifest("1.2.3"))
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(
		context.Background(),
		[]string{"package", "status", "--project-path", projectRoot},
		&stdout,
		&stderr,
	)
	if code != 0 {
		t.Fatalf("status failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{
		"Scoped registry: installed (https://package.openupm.com)",
		"Dependency: installed (1.2.3)",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("status output missing %q:\n%s", expected, output)
		}
	}
}

// Verifies an unknown package subcommand fails with guidance for valid subcommands.
func TestPackageUnknownSubcommandFails(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"package", "bogus"}, &stdout, &stderr)
	if code != 1 {
		t.Fatalf("expected exit 1, got %d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stderr.String(), "uloop package install") || !strings.Contains(stderr.String(), "uloop package status") {
		t.Fatalf("expected valid subcommand guidance:\n%s", stderr.String())
	}
}

func createPackageTestProject(t *testing.T, manifest string) string {
	t.Helper()
	projectRoot := t.TempDir()
	for _, directory := range []string{"Assets", "ProjectSettings", "Packages"} {
		if err := os.MkdirAll(filepath.Join(projectRoot, directory), 0o755); err != nil {
			t.Fatalf("failed to create %s: %v", directory, err)
		}
	}
	manifestPath := filepath.Join(projectRoot, "Packages", "manifest.json")
	if err := os.WriteFile(manifestPath, []byte(manifest), 0o644); err != nil {
		t.Fatalf("failed to write manifest: %v", err)
	}
	return projectRoot
}

func barePackageManifest() string {
	return `{
  "dependencies": {
    "com.unity.modules.ai": "1.0.0"
  }
}
`
}

func installedPackageManifest(version string) string {
	return `{
  "dependencies": {
    "com.unity.modules.ai": "1.0.0",
    "io.github.hatayama.uloopmcp": "` + version + `"
  },
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": [
        "io.github.hatayama.uloopmcp"
      ]
    }
  ]
}
`
}

func readPackageManifest(t *testing.T, projectRoot string) string {
	t.Helper()
	content, err := os.ReadFile(filepath.Join(projectRoot, "Packages", "manifest.json"))
	if err != nil {
		t.Fatalf("read manifest failed: %v", err)
	}
	return string(content)
}

func stubOpenUPMRegistry(t *testing.T, body string) func() {
	t.Helper()
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		if request.URL.Path != "/"+dispatcherUnityPackageName {
			t.Fatalf("unexpected path: %s", request.URL.Path)
		}
		writer.Header().Set("Content-Type", "application/json")
		_, _ = writer.Write([]byte(body))
	}))
	previousURL := openUPMRegistryBaseURL
	previousClient := packageRegistryHTTPClient
	openUPMRegistryBaseURL = server.URL
	packageRegistryHTTPClient = server.Client()
	return func() {
		server.Close()
		openUPMRegistryBaseURL = previousURL
		packageRegistryHTTPClient = previousClient
	}
}
