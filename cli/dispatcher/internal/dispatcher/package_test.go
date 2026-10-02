package dispatcher

import (
	"bytes"
	"context"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"runtime"
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

// Verifies install appends the package scope to an existing OpenUPM registry without duplicating it.
func TestPackageInstallAppendsScopeToExistingOpenUPMRegistry(t *testing.T) {
	manifest := `{
  "dependencies": {
    "com.unity.modules.ai": "1.0.0"
  },
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.other.openupm"
      ]
    }
  ]
}
`
	projectRoot := createPackageTestProject(t, manifest)
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
	if strings.Count(content, `"url": "https://package.openupm.com"`) != 1 {
		t.Fatalf("expected one OpenUPM registry entry:\n%s", content)
	}
	if !strings.Contains(content, `"com.other.openupm"`) {
		t.Fatalf("existing scope missing:\n%s", content)
	}
	assertManifestHasOpenUPMRegistry(t, []byte(content))
	if !strings.Contains(content, `"`+dispatcherUnityPackageName+`": "1.2.3"`) {
		t.Fatalf("dependency missing from manifest:\n%s", content)
	}
	if !strings.Contains(stdout.String(), "Added scoped registry") {
		t.Fatalf("expected scoped registry message:\n%s", stdout.String())
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

func TestInspectPackageManifestStatusCharacterizesInstalledFlags(t *testing.T) {
	// Characterizes inspectPackageManifestStatus dependency and OpenUPM-scope detection.
	testCases := []struct {
		name        string
		content     string
		want        packageManifestStatus
		wantErr     bool
		wantErrText string
	}{
		{
			name: "bare dependencies",
			content: `{
  "dependencies": {
    "com.unity.modules.ai": "1.0.0"
  }
}
`,
			want: packageManifestStatus{},
		},
		{
			name: "dependency without registry",
			content: `{
  "dependencies": {
    "io.github.hatayama.uloopmcp": "1.2.3"
  }
}
`,
			want: packageManifestStatus{
				dependencyInstalled: true,
				dependencyVersion:   "1.2.3",
			},
		},
		{
			name: "openupm without package scope",
			content: `{
  "dependencies": {},
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["com.other.openupm"]
    }
  ]
}
`,
			want: packageManifestStatus{},
		},
		{
			name: "foreign registry only",
			content: `{
  "dependencies": {},
  "scopedRegistries": [
    {
      "name": "Other",
      "url": "https://example.invalid/registry",
      "scopes": ["io.github.hatayama.uloopmcp"]
    }
  ]
}
`,
			want: packageManifestStatus{},
		},
		{
			name: "openupm scope without dependency",
			content: `{
  "dependencies": {},
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["io.github.hatayama.uloopmcp"]
    }
  ]
}
`,
			want: packageManifestStatus{registryInstalled: true},
		},
		{
			name:    "installed dependency and registry",
			content: installedPackageManifest("1.2.3"),
			want: packageManifestStatus{
				registryInstalled:   true,
				dependencyInstalled: true,
				dependencyVersion:   "1.2.3",
			},
		},
		{
			name: "openupm entry missing url",
			content: `{
  "dependencies": {},
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "scopes": ["io.github.hatayama.uloopmcp"]
    }
  ]
}
`,
			want: packageManifestStatus{},
		},
		{
			name:    "malformed json",
			content: `{not json`,
			wantErr: true,
		},
		{
			name: "valid openupm followed by non object",
			content: `{
  "dependencies": {},
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["io.github.hatayama.uloopmcp"]
    },
    "not-an-object"
  ]
}
`,
			wantErr:     true,
			wantErrText: "expected JSON object",
		},
	}

	for _, testCase := range testCases {
		t.Run(testCase.name, func(t *testing.T) {
			status, err := inspectPackageManifestStatus([]byte(testCase.content))
			if testCase.wantErr {
				if err == nil {
					t.Fatal("expected inspectPackageManifestStatus error")
				}
				if testCase.wantErrText != "" && err.Error() != testCase.wantErrText {
					t.Fatalf("error = %q, want %q", err.Error(), testCase.wantErrText)
				}
				return
			}
			if err != nil {
				t.Fatalf("inspectPackageManifestStatus failed: %v", err)
			}
			if status != testCase.want {
				t.Fatalf("status = %#v, want %#v", status, testCase.want)
			}
		})
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
	createPackageProjectAt(t, projectRoot, manifest)
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

// Verifies install resolves a Unity project in a child directory when run from a repository root.
func TestPackageInstallFindsProjectInChildDirectory(t *testing.T) {
	repoRoot := t.TempDir()
	if err := os.MkdirAll(filepath.Join(repoRoot, ".git"), 0o755); err != nil {
		t.Fatalf("failed to create .git: %v", err)
	}
	projectRoot := filepath.Join(repoRoot, "client", "UnityApp")
	createPackageProjectAt(t, projectRoot, barePackageManifest())
	t.Chdir(repoRoot)

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(
		context.Background(),
		[]string{"package", "install", "--version", "9.9.9"},
		&stdout,
		&stderr,
	)
	if code != 0 {
		t.Fatalf("package install failed: code=%d stderr=%s", code, stderr.String())
	}

	content := readPackageManifest(t, projectRoot)
	if !strings.Contains(content, `"`+dispatcherUnityPackageName+`": "9.9.9"`) {
		t.Fatalf("dependency missing from manifest:\n%s", content)
	}
}

// Verifies install fails with guidance when multiple Unity projects exist under the start directory.
func TestPackageInstallFailsWithMultipleChildProjects(t *testing.T) {
	repoRoot := t.TempDir()
	createPackageProjectAt(t, filepath.Join(repoRoot, "AppA"), barePackageManifest())
	createPackageProjectAt(t, filepath.Join(repoRoot, "AppB"), barePackageManifest())
	t.Chdir(repoRoot)

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(
		context.Background(),
		[]string{"package", "install", "--version", "9.9.9"},
		&stdout,
		&stderr,
	)
	if code == 0 {
		t.Fatalf("package install must fail with multiple candidate projects: stdout=%s", stdout.String())
	}
	if !strings.Contains(stderr.String(), "--project-path") {
		t.Fatalf("error must guide toward --project-path:\n%s", stderr.String())
	}
	if !strings.Contains(stderr.String(), `"ErrorCode": "PROJECT_NOT_FOUND"`) {
		t.Fatalf("ambiguous resolution must classify as PROJECT_NOT_FOUND, not an internal error:\n%s", stderr.String())
	}
}

// Verifies install prefers the enclosing Unity project over a nested Unity-shaped child folder
// when run from a subdirectory of a Unity project.
func TestPackageInstallPrefersEnclosingProjectOverNestedChild(t *testing.T) {
	projectRoot := createPackageTestProject(t, barePackageManifest())
	nestedFixture := filepath.Join(projectRoot, "ci", "fixtures", "NestedApp")
	createPackageProjectAt(t, nestedFixture, barePackageManifest())
	t.Chdir(filepath.Join(projectRoot, "ci"))

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(
		context.Background(),
		[]string{"package", "install", "--version", "9.9.9"},
		&stdout,
		&stderr,
	)
	if code != 0 {
		t.Fatalf("package install failed: code=%d stderr=%s", code, stderr.String())
	}

	if !strings.Contains(readPackageManifest(t, projectRoot), `"`+dispatcherUnityPackageName+`": "9.9.9"`) {
		t.Fatalf("enclosing project manifest must be updated")
	}
	if strings.Contains(readPackageManifest(t, nestedFixture), dispatcherUnityPackageName) {
		t.Fatalf("nested fixture manifest must stay untouched")
	}
}

// Verifies status resolves a Unity project in a child directory when run from a repository root.
func TestPackageStatusFindsProjectInChildDirectory(t *testing.T) {
	repoRoot := t.TempDir()
	if err := os.MkdirAll(filepath.Join(repoRoot, ".git"), 0o755); err != nil {
		t.Fatalf("failed to create .git: %v", err)
	}
	createPackageProjectAt(t, filepath.Join(repoRoot, "UnityApp"), barePackageManifest())
	t.Chdir(repoRoot)

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"package", "status"}, &stdout, &stderr)
	if code != 0 {
		t.Fatalf("package status failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Dependency: not installed") {
		t.Fatalf("status output unexpected:\n%s", stdout.String())
	}
}

func createPackageProjectAt(t *testing.T, projectRoot string, manifest string) {
	t.Helper()
	for _, directory := range []string{"Assets", "ProjectSettings", "Packages"} {
		if err := os.MkdirAll(filepath.Join(projectRoot, directory), 0o755); err != nil {
			t.Fatalf("failed to create %s: %v", directory, err)
		}
	}
	manifestPath := filepath.Join(projectRoot, "Packages", "manifest.json")
	if err := os.WriteFile(manifestPath, []byte(manifest), 0o644); err != nil {
		t.Fatalf("failed to write manifest: %v", err)
	}
}

// Verifies skills install resolves a Unity project in a child directory the same way package install does.
func TestResolveSkillsProjectRootFindsProjectInChildDirectory(t *testing.T) {
	repoRoot := t.TempDir()
	if err := os.MkdirAll(filepath.Join(repoRoot, ".git"), 0o755); err != nil {
		t.Fatalf("failed to create .git: %v", err)
	}
	projectRoot := filepath.Join(repoRoot, "UnityApp")
	createPackageProjectAt(t, projectRoot, barePackageManifest())

	resolved, err := resolveSkillsProjectRoot(repoRoot, "", false)
	if err != nil {
		t.Fatalf("resolveSkillsProjectRoot failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

func TestParsePackageOptionsRejectInvalidArguments(t *testing.T) {
	// Verifies each invalid install or status argument is rejected with the message for that mistake.
	installCases := []struct {
		args        []string
		wantMessage string
	}{
		{args: []string{"--bogus", "1"}, wantMessage: "Unknown package install option: --bogus"},
		{args: []string{"--version", ""}, wantMessage: "Empty value for --version"},
		{args: []string{"--version", "1.0.0", "--version", "2.0.0"}, wantMessage: "Duplicate package install option: --version"},
		{args: []string{"--version"}, wantMessage: "--version requires a value"},
	}
	for _, testCase := range installCases {
		if _, err := parsePackageInstallOptions(testCase.args); err == nil || !strings.Contains(err.Error(), testCase.wantMessage) {
			t.Fatalf("%v: expected %q, got %v", testCase.args, testCase.wantMessage, err)
		}
	}
	if err := parsePackageStatusOptions([]string{"--verbose"}); err == nil || err.Error() != "Unknown package status option: --verbose" {
		t.Fatalf("expected the status option error, got %v", err)
	}
}

func TestTryHandlePackageRequestPrintsSubcommandHelp(t *testing.T) {
	// Verifies --help after a subcommand prints package help without touching any project.
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	handled, code := tryHandlePackageRequest(context.Background(), []string{"package", "install", "--help"}, t.TempDir(), "", &stdout, &stderr)

	if !handled || code != 0 || stderr.Len() != 0 || !strings.Contains(stdout.String(), "uloop package install [--version <x.y.z>]") {
		t.Fatalf("unexpected help: code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
}

// runPackageForTest runs a package subcommand against an explicit project and returns its exit code and output.
func runPackageForTest(t *testing.T, projectRoot string, args ...string) (int, string, string) {
	t.Helper()
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	_, code := tryHandlePackageRequest(context.Background(), append([]string{"package"}, args...), t.TempDir(), projectRoot, &stdout, &stderr)
	return code, stdout.String(), stderr.String()
}

func TestPackageCommandsReportFailures(t *testing.T) {
	// Verifies install and status stop with code 1 and the error for each failure, leaving the manifest untouched.
	cases := []struct {
		name        string
		manifest    string
		args        []string
		wantMessage string
	}{
		{name: "install invalid option", manifest: barePackageManifest(), args: []string{"install", "--bogus=1"}, wantMessage: "Unknown package install option: --bogus"},
		{name: "install invalid manifest", manifest: "{", args: []string{"install", "--version", "1.0.0"}, wantMessage: "PACKAGE_MANIFEST_INVALID"},
		{name: "status invalid option", manifest: barePackageManifest(), args: []string{"status", "--verbose"}, wantMessage: "Unknown package status option: --verbose"},
		{name: "status invalid manifest", manifest: "{", args: []string{"status"}, wantMessage: "PACKAGE_MANIFEST_INVALID"},
		{name: "status dependency not a string", manifest: `{"dependencies":{"` + dispatcherUnityPackageName + `":1}}`, args: []string{"status"}, wantMessage: "PACKAGE_MANIFEST_INVALID"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			projectRoot := createPackageTestProject(t, testCase.manifest)

			code, stdout, stderr := runPackageForTest(t, projectRoot, testCase.args...)

			if code != 1 || stdout != "" || !strings.Contains(stderr, testCase.wantMessage) {
				t.Fatalf("unexpected result: code=%d stdout=%s stderr=%s", code, stdout, stderr)
			}
			if manifest := readPackageManifest(t, projectRoot); manifest != testCase.manifest {
				t.Fatalf("manifest must stay untouched: %s", manifest)
			}
		})
	}
}

func TestPackageStatusReportsMissingManifestAndProject(t *testing.T) {
	// Verifies status distinguishes a project without Packages/manifest.json from a path that is no Unity project.
	projectRoot := createDispatcherUnityProject(t)
	code, _, stderr := runPackageForTest(t, projectRoot, "status")
	if code != 1 || !strings.Contains(stderr, "PACKAGE_MANIFEST_INVALID") {
		t.Fatalf("expected the manifest error: code=%d stderr=%s", code, stderr)
	}

	code, _, stderr = runPackageForTest(t, t.TempDir(), "status")
	if code != 1 || strings.Contains(stderr, "PACKAGE_MANIFEST_INVALID") || !strings.Contains(stderr, "Unity project") {
		t.Fatalf("expected the project resolution error: code=%d stderr=%s", code, stderr)
	}
}

func TestPackageInstallReportsRegistryFailure(t *testing.T) {
	// Verifies a failed registry lookup is reported as PACKAGE_REGISTRY_UNAVAILABLE with its cause, without editing the manifest.
	projectRoot := createPackageTestProject(t, barePackageManifest())
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		writer.WriteHeader(http.StatusServiceUnavailable)
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

	code, _, stderr := runPackageForTest(t, projectRoot, "install")

	if code != 1 || !strings.Contains(stderr, "PACKAGE_REGISTRY_UNAVAILABLE") || !strings.Contains(stderr, "OpenUPM registry returned HTTP 503") {
		t.Fatalf("expected the registry error: code=%d stderr=%s", code, stderr)
	}
	if manifest := readPackageManifest(t, projectRoot); manifest != barePackageManifest() {
		t.Fatalf("manifest must stay untouched: %s", manifest)
	}
}

func TestPackageInstallReportsManifestWriteFailure(t *testing.T) {
	// Verifies an unwritable Packages directory fails the install and keeps the original manifest.
	if runtime.GOOS == "windows" {
		t.Skip("POSIX directory permissions are required for this failure.")
	}
	if os.Geteuid() == 0 {
		t.Skip("root ignores directory permissions.")
	}
	projectRoot := createPackageTestProject(t, barePackageManifest())
	packagesDir := filepath.Join(projectRoot, "Packages")
	if err := os.Chmod(packagesDir, 0o555); err != nil {
		t.Fatalf("failed to lock %s: %v", packagesDir, err)
	}
	t.Cleanup(func() { _ = os.Chmod(packagesDir, 0o755) })

	code, _, stderr := runPackageForTest(t, projectRoot, "install", "--version", "1.0.0")

	if code != 1 || !strings.Contains(stderr, "manifest.json.tmp: permission denied") {
		t.Fatalf("expected the write error: code=%d stderr=%s", code, stderr)
	}
	if manifest := readPackageManifest(t, projectRoot); manifest != barePackageManifest() {
		t.Fatalf("manifest must stay untouched: %s", manifest)
	}
}

func TestPackageInstallReportsVersionUpgrade(t *testing.T) {
	// Verifies installing a new version over an existing dependency reports the previous and new versions.
	projectRoot := createPackageTestProject(t, installedPackageManifest("1.0.0"))

	code, stdout, stderr := runPackageForTest(t, projectRoot, "install", "--version", "2.0.0")

	if code != 0 || !strings.Contains(stdout, "Updated "+dispatcherUnityPackageName+" 1.0.0 -> 2.0.0 in Packages/manifest.json") {
		t.Fatalf("unexpected result: code=%d stdout=%s stderr=%s", code, stdout, stderr)
	}
	if strings.Contains(stdout, "Added scoped registry") {
		t.Fatalf("the existing registry must not be reported as added: %s", stdout)
	}
}

func TestInspectPackageManifestStatusRejectsMalformedEntries(t *testing.T) {
	// Verifies malformed dependency or registry entries fail inspection instead of being read as not installed.
	cases := map[string]string{
		"dependencies not an object":  `{"dependencies":[]}`,
		"registries not an array":     `{"scopedRegistries":{}}`,
		"registry entry not object":   `{"scopedRegistries":[1]}`,
		"registry url not a string":   `{"scopedRegistries":[{"url":1}]}`,
		"registry scopes not strings": `{"scopedRegistries":[{"url":"` + openUPMRegistryURL + `","scopes":[1]}]}`,
	}
	for name, manifest := range cases {
		if _, err := inspectPackageManifestStatus([]byte(manifest)); err == nil {
			t.Fatalf("%s: expected an inspection error", name)
		}
	}
}

func TestInspectPackageManifestStatusIgnoresRegistryWithoutScopes(t *testing.T) {
	// Verifies an OpenUPM registry entry without scopes does not count as installed.
	status, err := inspectPackageManifestStatus([]byte(`{"scopedRegistries":[{"url":"` + openUPMRegistryURL + `"}]}`))

	if err != nil || status.registryInstalled {
		t.Fatalf("unexpected status: %+v err=%v", status, err)
	}
}
