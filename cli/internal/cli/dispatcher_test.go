package cli

import (
	"archive/tar"
	"archive/zip"
	"bytes"
	"compress/gzip"
	"context"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"testing"
)

type dispatcherArchiveTestEntry struct {
	Name    string
	Content string
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

func TestExtractDispatcherRealCLIFromTarPrefersRealCLI(t *testing.T) {
	// Verifies release archives that contain dispatcher first still extract the real CLI binary.
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
	// Verifies Windows release archives that contain dispatcher first still extract the real CLI binary.
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

func TestLoadDispatcherPinFallsBackToCliConstants(t *testing.T) {
	// Verifies old package layouts can still resolve a CLI version from CliConstants.cs.
	projectRoot := createDispatcherUnityProject(t)
	constantsPath := filepath.Join(projectRoot, "Packages", "src", "Editor", "Domain", "CliConstants.cs")
	if err := os.MkdirAll(filepath.Dir(constantsPath), 0o755); err != nil {
		t.Fatalf("failed to create constants directory: %v", err)
	}
	content := `public const int REQUIRED_CLI_PROTOCOL_VERSION = 3;
public const string MINIMUM_REQUIRED_CLI_VERSION = "3.0.0-beta.56";`
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
	if err := os.MkdirAll(filepath.Dir(pinPath), 0o755); err != nil {
		t.Fatalf("failed to create pin directory: %v", err)
	}
	content := `{"schemaVersion":1,"packageName":"io.github.hatayama.uloopmcp","packageVersion":"3.0.0-beta.1","cliVersion":"` +
		cliVersion +
		`","requiredProtocolVersion":2,"minimumDispatcherVersion":"` +
		version +
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
