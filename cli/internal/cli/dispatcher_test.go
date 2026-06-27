package cli

import (
	"archive/tar"
	"archive/zip"
	"bytes"
	"compress/gzip"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
	"time"
)

type dispatcherArchiveTestEntry struct {
	Name    string
	Content string
}

type dispatcherRoundTripFunc func(*http.Request) (*http.Response, error)

func (roundTrip dispatcherRoundTripFunc) RoundTrip(request *http.Request) (*http.Response, error) {
	return roundTrip(request)
}

func TestRunDispatcherUsesProjectPinAndCachedRealCLI(t *testing.T) {
	// Verifies dispatcher reads the project pin and executes the cached real CLI.
	projectRoot := createDispatcherUnityProject(t)
	cacheRoot := t.TempDir()
	writeDispatcherProjectPin(t, projectRoot, version)
	expectedCLIPath := writeCachedDispatcherRealCLI(t, cacheRoot, version)
	t.Setenv(dispatcherCacheDirEnvName, cacheRoot)
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")
	t.Chdir(projectRoot)

	previousRunner := dispatcherRunRealCLI
	defer func() {
		dispatcherRunRealCLI = previousRunner
	}()
	var actualPath string
	var actualArgs []string
	dispatcherRunRealCLI = func(ctx context.Context, realCLIPath string, args []string, stdout io.Writer, stderr io.Writer) int {
		actualPath = realCLIPath
		actualArgs = append([]string{}, args...)
		return 7
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"compile", "--force-recompile"}, &stdout, &stderr)

	if code != 7 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	if actualPath != expectedCLIPath {
		t.Fatalf("real CLI path mismatch: %s", actualPath)
	}
	if stderr.String() != "" {
		t.Fatalf("cached CLI should not write dispatcher download status: %s", stderr.String())
	}
	assertStringSliceEqual(t, actualArgs, []string{"compile", "--force-recompile"})
}

func TestRunDispatcherPreservesExplicitProjectPathForRealCLI(t *testing.T) {
	// Verifies dispatcher accepts trailing --project-path and passes the original arguments onward.
	projectRoot := createDispatcherUnityProject(t)
	cacheRoot := t.TempDir()
	writeDispatcherProjectPin(t, projectRoot, version)
	writeCachedDispatcherRealCLI(t, cacheRoot, version)
	t.Setenv(dispatcherCacheDirEnvName, cacheRoot)
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")
	t.Chdir(t.TempDir())

	previousRunner := dispatcherRunRealCLI
	defer func() {
		dispatcherRunRealCLI = previousRunner
	}()
	var actualArgs []string
	dispatcherRunRealCLI = func(ctx context.Context, realCLIPath string, args []string, stdout io.Writer, stderr io.Writer) int {
		actualArgs = append([]string{}, args...)
		return 0
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"compile", "--project-path", projectRoot}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("dispatcher failed: code=%d stderr=%s", code, stderr.String())
	}
	assertStringSliceEqual(t, actualArgs, []string{"compile", "--project-path", projectRoot})
}

func TestRunDispatcherCommandHelpDoesNotRequireProjectPin(t *testing.T) {
	// Verifies dispatcher handles command help before project and pin resolution.
	t.Chdir(t.TempDir())

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"compile", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("dispatcher command help failed: code=%d stderr=%s", code, stderr.String())
	}
	if !bytes.Contains(stdout.Bytes(), []byte("uloop compile")) {
		t.Fatalf("compile help output mismatch: %s", stdout.String())
	}
}

func TestRunDispatcherUnknownLeadingOptionDoesNotRequireProjectPin(t *testing.T) {
	// Verifies dispatcher reports leading option mistakes before project and pin resolution.
	t.Chdir(t.TempDir())

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"--project-pathology"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("dispatcher unknown option code mismatch: code=%d stdout=%s", code, stdout.String())
	}
	if !bytes.Contains(stderr.Bytes(), []byte("Unknown global option")) {
		t.Fatalf("dispatcher unknown option output mismatch: %s", stderr.String())
	}
}

func TestRunDispatcherLaunchQuitDoesNotRequireProjectPin(t *testing.T) {
	// Verifies launch can bootstrap a project before Unity has generated the dispatcher pin.
	projectRoot := createDispatcherUnityProject(t)
	t.Chdir(t.TempDir())

	previousFinder := findRunningUnityProcessForLaunch
	findRunningUnityProcessForLaunch = func(context.Context, string) (*unityProcess, error) {
		return nil, nil
	}
	defer func() {
		findRunningUnityProcessForLaunch = previousFinder
	}()

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"launch", projectRoot, "--quit"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("dispatcher launch failed: code=%d stderr=%s", code, stderr.String())
	}
	if !bytes.Contains(stdout.Bytes(), []byte(`"Quit": true`)) {
		t.Fatalf("dispatcher launch output mismatch: %s", stdout.String())
	}
}

func TestRunDispatcherVersionUsesDispatcherVersion(t *testing.T) {
	// Verifies the global launcher reports its own dispatcher release version.
	t.Chdir(t.TempDir())

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"--version"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("dispatcher version failed: code=%d stderr=%s", code, stderr.String())
	}
	if strings.TrimSpace(stdout.String()) != dispatcherVersion {
		t.Fatalf("dispatcher version mismatch: %s", stdout.String())
	}
}

func TestResolveDispatcherRealCLIRejectsInvalidCLIVersion(t *testing.T) {
	// Verifies project pins cannot escape the dispatcher cache through cliVersion path segments.
	t.Setenv(dispatcherCacheDirEnvName, t.TempDir())

	_, err := resolveDispatcherRealCLI(context.Background(), dispatcherPin{CLIVersion: "../../../../payload"}, io.Discard)

	if err == nil {
		t.Fatal("expected invalid cliVersion error")
	}
}

func TestEnforceDispatcherFreshnessRequiresManualUpdateWhenSelfUpdateDisabled(t *testing.T) {
	// Verifies disabling mutation does not disable the minimum dispatcher version contract.
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")

	var stderr bytes.Buffer
	handled, code := enforceDispatcherFreshness(
		context.Background(),
		dispatcherPin{MinimumDispatcherVersion: "999.0.0"},
		&stderr)

	if !handled || code != 1 {
		t.Fatalf("freshness result mismatch: handled=%t code=%d", handled, code)
	}
	if !bytes.Contains(stderr.Bytes(), []byte(errorCodeCLIUpdateRequired)) {
		t.Fatalf("freshness output mismatch: %s", stderr.String())
	}
}

func TestEnforceDispatcherFreshnessDoesNotMarkFailedOptionalUpdateChecked(t *testing.T) {
	// Verifies transient optional update failures stay retryable on the next command.
	cacheRoot := t.TempDir()
	t.Setenv(dispatcherCacheDirEnvName, cacheRoot)

	previousRunner := dispatcherRunUpdate
	defer func() {
		dispatcherRunUpdate = previousRunner
	}()
	dispatcherRunUpdate = func(context.Context) error {
		return errors.New("network unavailable")
	}

	var stderr bytes.Buffer
	handled, code := enforceDispatcherFreshness(
		context.Background(),
		dispatcherPin{MinimumDispatcherVersion: dispatcherVersion},
		&stderr)

	if handled || code != 0 {
		t.Fatalf("freshness result mismatch: handled=%t code=%d", handled, code)
	}
	if !bytes.Contains(stderr.Bytes(), []byte("dispatcher self-update skipped")) {
		t.Fatalf("freshness output mismatch: %s", stderr.String())
	}
	statePath := filepath.Join(cacheRoot, dispatcherUpdateStateFileName)
	if _, err := os.Stat(statePath); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("expected no update state after failed optional update, got err=%v", err)
	}
}

func TestExtractDispatcherRealCLIFromTarPrefersRealCLI(t *testing.T) {
	// Verifies legacy bridge archives that contain dispatcher first still extract the real CLI binary.
	tempDir := t.TempDir()
	archivePath := filepath.Join(tempDir, "uloop-darwin-arm64.tar.gz")
	writeDispatcherTarGzArchive(t, archivePath, []dispatcherArchiveTestEntry{
		{Name: "uloop", Content: "dispatcher"},
		{Name: "uloop-cli", Content: "real"},
	})
	destinationPath := filepath.Join(tempDir, "uloop-cli")

	err := extractDispatcherRealCLI(archivePath, filepath.Base(archivePath), destinationPath, "darwin")
	if err != nil {
		t.Fatalf("extractDispatcherRealCLI failed: %v", err)
	}
	assertFileContent(t, destinationPath, "real")
}

func TestExtractDispatcherRealCLIFromZipPrefersRealCLI(t *testing.T) {
	// Verifies Windows legacy bridge archives that contain dispatcher first still extract the real CLI binary.
	tempDir := t.TempDir()
	archivePath := filepath.Join(tempDir, "uloop-windows-amd64.zip")
	writeDispatcherZipArchive(t, archivePath, []dispatcherArchiveTestEntry{
		{Name: "uloop.exe", Content: "dispatcher"},
		{Name: "uloop-cli.exe", Content: "real"},
	})
	destinationPath := filepath.Join(tempDir, "uloop-cli.exe")

	err := extractDispatcherRealCLI(archivePath, filepath.Base(archivePath), destinationPath, "windows")
	if err != nil {
		t.Fatalf("extractDispatcherRealCLI failed: %v", err)
	}
	assertFileContent(t, destinationPath, "real")
}

func TestExtractDispatcherRealCLIFromTarRequiresRealCLIAsset(t *testing.T) {
	// Verifies CLI release archives extract the real CLI binary without dispatcher payloads.
	tempDir := t.TempDir()
	archivePath := filepath.Join(tempDir, "uloop-cli-darwin-arm64.tar.gz")
	writeDispatcherTarGzArchive(t, archivePath, []dispatcherArchiveTestEntry{
		{Name: "uloop-cli", Content: "real"},
	})
	destinationPath := filepath.Join(tempDir, "uloop-cli")

	err := extractDispatcherRealCLI(archivePath, filepath.Base(archivePath), destinationPath, "darwin")
	if err != nil {
		t.Fatalf("extractDispatcherRealCLI failed: %v", err)
	}
	assertFileContent(t, destinationPath, "real")
}

func TestExtractDispatcherRealCLIFromZipRequiresRealCLIAsset(t *testing.T) {
	// Verifies Windows CLI release archives extract the real CLI binary without dispatcher payloads.
	tempDir := t.TempDir()
	archivePath := filepath.Join(tempDir, "uloop-cli-windows-amd64.zip")
	writeDispatcherZipArchive(t, archivePath, []dispatcherArchiveTestEntry{
		{Name: "uloop-cli.exe", Content: "real"},
	})
	destinationPath := filepath.Join(tempDir, "uloop-cli.exe")

	err := extractDispatcherRealCLI(archivePath, filepath.Base(archivePath), destinationPath, "windows")
	if err != nil {
		t.Fatalf("extractDispatcherRealCLI failed: %v", err)
	}
	assertFileContent(t, destinationPath, "real")
}

func TestDispatcherHTTPClientHasDownloadTimeout(t *testing.T) {
	// Verifies dispatcher release downloads cannot hang indefinitely.
	if dispatcherHTTPClient.Timeout != 2*time.Minute {
		t.Fatalf("dispatcher HTTP timeout mismatch: %s", dispatcherHTTPClient.Timeout)
	}
}

func TestDownloadDispatcherRealCLIWritesDownloadStatus(t *testing.T) {
	// Verifies cache misses tell callers that dispatcher is downloading the pinned CLI.
	tempDir := t.TempDir()
	archivePath := filepath.Join(tempDir, "uloop-cli-darwin-arm64.tar.gz")
	writeDispatcherTarGzArchive(t, archivePath, []dispatcherArchiveTestEntry{
		{Name: "uloop-cli", Content: "real"},
	})
	archiveContent, err := os.ReadFile(archivePath)
	if err != nil {
		t.Fatalf("failed to read archive: %v", err)
	}
	checksum := sha256.Sum256(archiveContent)
	checksumContent := []byte(hex.EncodeToString(checksum[:]) + "  " + filepath.Base(archivePath) + "\n")

	previousHTTPClient := dispatcherHTTPClient
	defer func() {
		dispatcherHTTPClient = previousHTTPClient
	}()
	dispatcherHTTPClient = &http.Client{
		Transport: dispatcherRoundTripFunc(func(request *http.Request) (*http.Response, error) {
			content := []byte{}
			statusCode := http.StatusNotFound
			if strings.HasSuffix(request.URL.Path, "/uloop-cli-darwin-arm64.tar.gz") {
				content = archiveContent
				statusCode = http.StatusOK
			}
			if strings.HasSuffix(request.URL.Path, "/uloop-cli-darwin-arm64.tar.gz.sha256") {
				content = checksumContent
				statusCode = http.StatusOK
			}
			return &http.Response{
				StatusCode: statusCode,
				Status:     http.StatusText(statusCode),
				Body:       io.NopCloser(bytes.NewReader(content)),
			}, nil
		}),
	}

	var stderr bytes.Buffer
	realCLIPath, err := downloadDispatcherRealCLI(
		context.Background(),
		t.TempDir(),
		"3.0.0-beta.88",
		"darwin",
		"arm64",
		&stderr)
	if err != nil {
		t.Fatalf("downloadDispatcherRealCLI failed: %v", err)
	}
	expectedStatus := "uloop: downloading pinned CLI 3.0.0-beta.88 for darwin-arm64...\n"
	if stderr.String() != expectedStatus {
		t.Fatalf("download status mismatch: %q", stderr.String())
	}
	assertFileContent(t, realCLIPath, "real")
}

func TestInstallDownloadedDispatcherRealCLIKeepsExistingExecutable(t *testing.T) {
	// Verifies concurrent downloads do not delete an executable another dispatcher already cached.
	tempDir := t.TempDir()
	realCLIPath := filepath.Join(tempDir, dispatcherRealCLIFileName(runtime.GOOS))
	tempRealCLIPath := filepath.Join(tempDir, "downloaded-"+dispatcherRealCLIFileName(runtime.GOOS))
	if err := os.WriteFile(realCLIPath, []byte("existing"), 0o755); err != nil {
		t.Fatalf("failed to write existing real CLI: %v", err)
	}
	if err := os.WriteFile(tempRealCLIPath, []byte("downloaded"), 0o755); err != nil {
		t.Fatalf("failed to write temp real CLI: %v", err)
	}

	path, err := installDownloadedDispatcherRealCLI(tempRealCLIPath, realCLIPath)
	if err != nil {
		t.Fatalf("installDownloadedDispatcherRealCLI failed: %v", err)
	}
	if path != realCLIPath {
		t.Fatalf("real CLI path mismatch: %s", path)
	}
	assertFileContent(t, realCLIPath, "existing")
}

func TestLoadDispatcherPinFallsBackToPackagePin(t *testing.T) {
	// Verifies dispatcher can read the package-level pin when the project copy is missing.
	projectRoot := createDispatcherUnityProject(t)
	packageRoot := filepath.Join(projectRoot, "Packages", "src")
	if err := os.MkdirAll(packageRoot, 0o755); err != nil {
		t.Fatalf("failed to create package root: %v", err)
	}
	writeDispatcherPinFile(t, filepath.Join(packageRoot, dispatcherPackagePinFileName), "3.0.0-beta.55")

	pin, err := loadDispatcherPin(projectRoot)
	if err != nil {
		t.Fatalf("loadDispatcherPin failed: %v", err)
	}
	if pin.CLIVersion != "3.0.0-beta.55" {
		t.Fatalf("cliVersion mismatch: %s", pin.CLIVersion)
	}
}

func TestLoadDispatcherPinSkipsInvalidPackageCandidate(t *testing.T) {
	// Verifies stale package pins do not block a valid PackageCache pin during first startup.
	projectRoot := createDispatcherUnityProject(t)
	sourcePackageRoot := filepath.Join(projectRoot, "Packages", "src")
	cachePackageRoot := filepath.Join(projectRoot, "Library", "PackageCache", dispatcherUnityPackageName+"@3.0.0-beta.57")
	if err := os.MkdirAll(sourcePackageRoot, 0o755); err != nil {
		t.Fatalf("failed to create source package root: %v", err)
	}
	if err := os.WriteFile(filepath.Join(sourcePackageRoot, dispatcherPackagePinFileName), []byte("{"), 0o644); err != nil {
		t.Fatalf("failed to write invalid package pin: %v", err)
	}
	writeDispatcherPinFile(t, filepath.Join(cachePackageRoot, dispatcherPackagePinFileName), "3.0.0-beta.57")

	pin, err := loadDispatcherPin(projectRoot)
	if err != nil {
		t.Fatalf("loadDispatcherPin failed: %v", err)
	}
	if pin.CLIVersion != "3.0.0-beta.57" {
		t.Fatalf("cliVersion mismatch: %s", pin.CLIVersion)
	}
}

func TestLoadDispatcherPinNormalizesVersionPrefixes(t *testing.T) {
	// Verifies v-prefixed pin versions are normalized before semantic-version validation.
	projectRoot := createDispatcherUnityProject(t)
	pinPath := filepath.Join(projectRoot, dispatcherProjectPinRelativePath)
	writeDispatcherPinFileWithMinimum(t, pinPath, "v3.0.0-beta.58", "V3.0.0-beta.39")

	pin, err := loadDispatcherPin(projectRoot)
	if err != nil {
		t.Fatalf("loadDispatcherPin failed: %v", err)
	}
	if pin.CLIVersion != "3.0.0-beta.58" {
		t.Fatalf("cliVersion mismatch: %s", pin.CLIVersion)
	}
	if pin.MinimumDispatcherVersion != "3.0.0-beta.39" {
		t.Fatalf("minimumDispatcherVersion mismatch: %s", pin.MinimumDispatcherVersion)
	}
}

func TestLoadDispatcherPinRejectsInvalidCLIVersion(t *testing.T) {
	// Verifies project pin cliVersion must be a release version, not a filesystem path.
	projectRoot := createDispatcherUnityProject(t)
	pinPath := filepath.Join(projectRoot, dispatcherProjectPinRelativePath)
	if err := os.MkdirAll(filepath.Dir(pinPath), 0o755); err != nil {
		t.Fatalf("failed to create pin directory: %v", err)
	}
	content := `{"schemaVersion":1,"packageName":"io.github.hatayama.uloopmcp","packageVersion":"3.0.0-beta.1","cliVersion":"../../payload","requiredProtocolVersion":2,"minimumDispatcherVersion":"3.0.0-beta.39"}`
	if err := os.WriteFile(pinPath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write pin: %v", err)
	}

	_, err := loadDispatcherPin(projectRoot)

	if err == nil {
		t.Fatal("expected invalid cliVersion error")
	}
}

func TestLoadDispatcherPinRejectsInvalidMinimumDispatcherVersion(t *testing.T) {
	// Verifies malformed dispatcher minimums fail closed instead of bypassing freshness checks.
	projectRoot := createDispatcherUnityProject(t)
	pinPath := filepath.Join(projectRoot, dispatcherProjectPinRelativePath)
	writeDispatcherPinFileWithMinimum(t, pinPath, "3.0.0-beta.58", "../../payload")

	_, err := loadDispatcherPin(projectRoot)

	if err == nil {
		t.Fatal("expected invalid minimumDispatcherVersion error")
	}
}

func TestLoadDispatcherPinFallsBackToCliConstants(t *testing.T) {
	// Verifies old package layouts can still resolve a CLI version from CliConstants.cs.
	projectRoot := createDispatcherUnityProject(t)
	constantsPath := filepath.Join(projectRoot, "Packages", "src", "Editor", "Domain", "CliConstants.cs")
	if err := os.MkdirAll(filepath.Dir(constantsPath), 0o755); err != nil {
		t.Fatalf("failed to create constants directory: %v", err)
	}
	content := `public const int REQUIRED_CLI_PROTOCOL_VERSION = 3;
public const string MINIMUM_REQUIRED_CLI_VERSION = "3.0.0-beta.56";
public const string MINIMUM_REQUIRED_DISPATCHER_VERSION = "1.0.0";`
	if err := os.WriteFile(constantsPath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write constants: %v", err)
	}

	pin, err := loadDispatcherPin(projectRoot)
	if err != nil {
		t.Fatalf("loadDispatcherPin failed: %v", err)
	}
	if pin.CLIVersion != "3.0.0-beta.56" {
		t.Fatalf("cliVersion mismatch: %s", pin.CLIVersion)
	}
	if pin.RequiredProtocolVersion != 3 {
		t.Fatalf("protocol mismatch: %d", pin.RequiredProtocolVersion)
	}
	if pin.MinimumDispatcherVersion != "1.0.0" {
		t.Fatalf("minimumDispatcherVersion mismatch: %s", pin.MinimumDispatcherVersion)
	}
}

func TestLoadDispatcherPinFromCliConstantsNormalizesVersionPrefix(t *testing.T) {
	// Verifies v-prefixed fallback constants are normalized before dispatcher resolution.
	projectRoot := createDispatcherUnityProject(t)
	constantsPath := filepath.Join(projectRoot, "Packages", "src", "Editor", "Domain", "CliConstants.cs")
	if err := os.MkdirAll(filepath.Dir(constantsPath), 0o755); err != nil {
		t.Fatalf("failed to create constants directory: %v", err)
	}
	content := `public const int REQUIRED_CLI_PROTOCOL_VERSION = 3;
public const string MINIMUM_REQUIRED_CLI_VERSION = "v3.0.0-beta.59";
public const string MINIMUM_REQUIRED_DISPATCHER_VERSION = "v1.0.0";`
	if err := os.WriteFile(constantsPath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write constants: %v", err)
	}

	pin, err := loadDispatcherPin(projectRoot)
	if err != nil {
		t.Fatalf("loadDispatcherPin failed: %v", err)
	}
	if pin.CLIVersion != "3.0.0-beta.59" {
		t.Fatalf("cliVersion mismatch: %s", pin.CLIVersion)
	}
	if pin.MinimumDispatcherVersion != "1.0.0" {
		t.Fatalf("minimumDispatcherVersion mismatch: %s", pin.MinimumDispatcherVersion)
	}
}

func TestDispatcherReleaseAssetNameRejectsUnsupportedPlatform(t *testing.T) {
	// Verifies dispatcher does not invent download assets for unsupported platforms.
	_, err := dispatcherReleaseAssetName("linux", "amd64")

	if err == nil {
		t.Fatal("expected unsupported platform error")
	}
}

func createDispatcherUnityProject(t *testing.T) string {
	t.Helper()
	projectRoot := t.TempDir()
	for _, dirName := range []string{"Assets", "ProjectSettings"} {
		if err := os.MkdirAll(filepath.Join(projectRoot, dirName), 0o755); err != nil {
			t.Fatalf("failed to create Unity project directory: %v", err)
		}
	}
	return projectRoot
}

func writeDispatcherProjectPin(t *testing.T, projectRoot string, cliVersion string) {
	t.Helper()
	pinPath := filepath.Join(projectRoot, dispatcherProjectPinRelativePath)
	writeDispatcherPinFile(t, pinPath, cliVersion)
}

func writeDispatcherPinFile(t *testing.T, pinPath string, cliVersion string) {
	t.Helper()
	writeDispatcherPinFileWithMinimum(t, pinPath, cliVersion, dispatcherVersion)
}

func writeDispatcherPinFileWithMinimum(t *testing.T, pinPath string, cliVersion string, minimumDispatcherVersion string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(pinPath), 0o755); err != nil {
		t.Fatalf("failed to create pin directory: %v", err)
	}
	content := `{"schemaVersion":1,"packageName":"io.github.hatayama.uloopmcp","packageVersion":"3.0.0-beta.1","cliVersion":"` +
		cliVersion +
		`","requiredProtocolVersion":2,"minimumDispatcherVersion":"` +
		minimumDispatcherVersion +
		`"}`
	if err := os.WriteFile(pinPath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write pin: %v", err)
	}
}

func writeCachedDispatcherRealCLI(t *testing.T, cacheRoot string, cliVersion string) string {
	t.Helper()
	realCLIPath := dispatcherCachedRealCLIPath(cacheRoot, cliVersion, runtime.GOOS, runtime.GOARCH)
	if err := os.MkdirAll(filepath.Dir(realCLIPath), 0o755); err != nil {
		t.Fatalf("failed to create cached CLI directory: %v", err)
	}
	if err := os.WriteFile(realCLIPath, []byte("cached real cli"), 0o755); err != nil {
		t.Fatalf("failed to write cached CLI: %v", err)
	}
	return realCLIPath
}

func writeDispatcherTarGzArchive(t *testing.T, archivePath string, entries []dispatcherArchiveTestEntry) {
	t.Helper()
	file, err := os.Create(archivePath)
	if err != nil {
		t.Fatalf("failed to create tar archive: %v", err)
	}
	gzipWriter := gzip.NewWriter(file)
	tarWriter := tar.NewWriter(gzipWriter)
	for _, entry := range entries {
		content := []byte(entry.Content)
		header := &tar.Header{
			Name: entry.Name,
			Mode: 0o755,
			Size: int64(len(content)),
		}
		if err := tarWriter.WriteHeader(header); err != nil {
			t.Fatalf("failed to write tar header: %v", err)
		}
		if _, err := tarWriter.Write(content); err != nil {
			t.Fatalf("failed to write tar content: %v", err)
		}
	}
	closeErr := tarWriter.Close()
	gzipCloseErr := gzipWriter.Close()
	fileCloseErr := file.Close()
	if closeErr != nil {
		t.Fatalf("failed to close tar archive: %v", closeErr)
	}
	if gzipCloseErr != nil {
		t.Fatalf("failed to close gzip archive: %v", gzipCloseErr)
	}
	if fileCloseErr != nil {
		t.Fatalf("failed to close tar file: %v", fileCloseErr)
	}
}

func writeDispatcherZipArchive(t *testing.T, archivePath string, entries []dispatcherArchiveTestEntry) {
	t.Helper()
	file, err := os.Create(archivePath)
	if err != nil {
		t.Fatalf("failed to create zip archive: %v", err)
	}
	zipWriter := zip.NewWriter(file)
	for _, entry := range entries {
		writer, err := zipWriter.Create(entry.Name)
		if err != nil {
			t.Fatalf("failed to write zip header: %v", err)
		}
		if _, err := writer.Write([]byte(entry.Content)); err != nil {
			t.Fatalf("failed to write zip content: %v", err)
		}
	}
	closeErr := zipWriter.Close()
	fileCloseErr := file.Close()
	if closeErr != nil {
		t.Fatalf("failed to close zip archive: %v", closeErr)
	}
	if fileCloseErr != nil {
		t.Fatalf("failed to close zip file: %v", fileCloseErr)
	}
}

func assertFileContent(t *testing.T, filePath string, expected string) {
	t.Helper()
	content, err := os.ReadFile(filePath)
	if err != nil {
		t.Fatalf("failed to read %s: %v", filePath, err)
	}
	if string(content) != expected {
		t.Fatalf("file content mismatch: %q", string(content))
	}
}

func assertStringSliceEqual(t *testing.T, actual []string, expected []string) {
	t.Helper()
	if len(actual) != len(expected) {
		t.Fatalf("length mismatch: actual=%#v expected=%#v", actual, expected)
	}
	for index, expectedValue := range expected {
		if actual[index] != expectedValue {
			t.Fatalf("value mismatch at %d: actual=%#v expected=%#v", index, actual, expected)
		}
	}
}
