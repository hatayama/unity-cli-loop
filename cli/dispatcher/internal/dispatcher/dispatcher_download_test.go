package dispatcher

import (
	"archive/tar"
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

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
)

// stubDispatcherHTTPResponses serves the given body for every request path ending with a key
// and 404 for everything else, then restores the real client when the test ends.
func stubDispatcherHTTPResponses(t *testing.T, bodies map[string][]byte) {
	t.Helper()
	previousHTTPClient := dispatcherHTTPClient
	t.Cleanup(func() {
		dispatcherHTTPClient = previousHTTPClient
	})
	dispatcherHTTPClient = &http.Client{
		Transport: dispatcherRoundTripFunc(func(request *http.Request) (*http.Response, error) {
			content := []byte{}
			statusCode := http.StatusNotFound
			for suffix, body := range bodies {
				if strings.HasSuffix(request.URL.Path, suffix) {
					content = body
					statusCode = http.StatusOK
				}
			}
			return &http.Response{
				StatusCode: statusCode,
				Status:     http.StatusText(statusCode),
				Body:       io.NopCloser(bytes.NewReader(content)),
			}, nil
		}),
	}
}

func dispatcherTestChecksumLine(t *testing.T, filePath string) []byte {
	t.Helper()
	content, err := os.ReadFile(filePath)
	if err != nil {
		t.Fatalf("failed to read %s: %v", filePath, err)
	}
	checksum := sha256.Sum256(content)
	return []byte(hex.EncodeToString(checksum[:]) + "  " + filepath.Base(filePath) + "\n")
}

func writeDispatcherTestFile(t *testing.T, filePath string, content string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(filePath), 0o755); err != nil {
		t.Fatalf("failed to create directory for %s: %v", filePath, err)
	}
	if err := os.WriteFile(filePath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write %s: %v", filePath, err)
	}
}

func TestDispatcherReleaseAssetNameCoversSupportedAndUnsupportedPlatforms(t *testing.T) {
	// Verifies each published platform maps to its archive name and every other platform is rejected with a descriptive error.
	cases := []struct {
		goos      string
		goarch    string
		wantName  string
		wantError string
	}{
		{goos: "darwin", goarch: "amd64", wantName: "uloop-project-runner-darwin-amd64.tar.gz"},
		{goos: "darwin", goarch: "arm64", wantName: "uloop-project-runner-darwin-arm64.tar.gz"},
		{goos: "darwin", goarch: "386", wantError: "unsupported darwin architecture: 386"},
		{goos: "windows", goarch: "amd64", wantName: "uloop-project-runner-windows-amd64.zip"},
		{goos: "windows", goarch: "arm64", wantError: "unsupported windows architecture: arm64"},
		{goos: "linux", goarch: "amd64", wantName: "uloop-project-runner-linux-amd64.tar.gz"},
		{goos: "linux", goarch: "arm64", wantError: "unsupported platform: linux-arm64"},
		{goos: "freebsd", goarch: "amd64", wantError: "unsupported platform: freebsd-amd64"},
	}
	for _, testCase := range cases {
		t.Run(testCase.goos+"-"+testCase.goarch, func(t *testing.T) {
			name, err := dispatcherReleaseAssetName(testCase.goos, testCase.goarch)
			if testCase.wantError != "" {
				if err == nil || err.Error() != testCase.wantError {
					t.Fatalf("error mismatch: got %v want %q", err, testCase.wantError)
				}
				return
			}
			if err != nil {
				t.Fatalf("unexpected error: %v", err)
			}
			if name != testCase.wantName {
				t.Fatalf("asset name mismatch: got %s want %s", name, testCase.wantName)
			}
		})
	}
}

func TestDownloadDispatcherFileRejectsNonSuccessStatus(t *testing.T) {
	// Verifies a non-2xx response is reported with the URL and status and leaves no destination file.
	stubDispatcherHTTPResponses(t, nil)
	destinationPath := filepath.Join(t.TempDir(), "asset")

	err := downloadDispatcherFile(context.Background(), "https://example.invalid/asset", destinationPath)

	if err == nil || !strings.Contains(err.Error(), "download failed for https://example.invalid/asset") {
		t.Fatalf("expected a download status error, got %v", err)
	}
	if fileExists(destinationPath) {
		t.Fatal("a failed download must not create the destination file")
	}
}

func TestDownloadDispatcherFileReturnsTransportError(t *testing.T) {
	// Verifies transport failures are returned to the caller unchanged.
	previousHTTPClient := dispatcherHTTPClient
	t.Cleanup(func() {
		dispatcherHTTPClient = previousHTTPClient
	})
	transportErr := errors.New("simulated transport failure")
	dispatcherHTTPClient = &http.Client{
		Transport: dispatcherRoundTripFunc(func(*http.Request) (*http.Response, error) {
			return nil, transportErr
		}),
	}

	err := downloadDispatcherFile(context.Background(), "https://example.invalid/asset", filepath.Join(t.TempDir(), "asset"))

	if !errors.Is(err, transportErr) {
		t.Fatalf("expected the transport error, got %v", err)
	}
}

func TestDownloadDispatcherFileRejectsInvalidURL(t *testing.T) {
	// Verifies a URL that cannot form a request fails before any network call.
	err := downloadDispatcherFile(context.Background(), "://missing-scheme", filepath.Join(t.TempDir(), "asset"))
	if err == nil || !strings.Contains(err.Error(), "missing protocol scheme") {
		t.Fatalf("expected an invalid URL error, got %v", err)
	}
}

func TestDownloadDispatcherFileReportsUnwritableDestination(t *testing.T) {
	// Verifies a destination inside a missing directory surfaces the file creation error.
	stubDispatcherHTTPResponses(t, map[string][]byte{"/asset": []byte("content")})
	destinationPath := filepath.Join(t.TempDir(), "missing", "asset")

	err := downloadDispatcherFile(context.Background(), "https://example.invalid/asset", destinationPath)

	if !errors.Is(err, os.ErrNotExist) || !strings.Contains(err.Error(), "open "+destinationPath) {
		t.Fatalf("expected a destination open error, got %v", err)
	}
}

func TestVerifyDispatcherChecksumFailures(t *testing.T) {
	// Verifies each way checksum verification can fail is reported instead of accepting the asset.
	tempDir := t.TempDir()
	assetPath := filepath.Join(tempDir, "asset.tar.gz")
	writeDispatcherTestFile(t, assetPath, "asset")
	validChecksumPath := filepath.Join(tempDir, "valid.sha256")
	if err := os.WriteFile(validChecksumPath, dispatcherTestChecksumLine(t, assetPath), 0o644); err != nil {
		t.Fatalf("failed to write checksum: %v", err)
	}
	emptyChecksumPath := filepath.Join(tempDir, "empty.sha256")
	writeDispatcherTestFile(t, emptyChecksumPath, "  \n")
	wrongChecksumPath := filepath.Join(tempDir, "wrong.sha256")
	writeDispatcherTestFile(t, wrongChecksumPath, strings.Repeat("0", 64)+"  asset.tar.gz\n")

	cases := []struct {
		name         string
		assetPath    string
		checksumPath string
		wantMessage  string
		wantNotExist bool
	}{
		{name: "missing checksum file", assetPath: assetPath, checksumPath: filepath.Join(tempDir, "missing.sha256"), wantMessage: "missing.sha256", wantNotExist: true},
		{name: "empty checksum file", assetPath: assetPath, checksumPath: emptyChecksumPath, wantMessage: "checksum file is empty"},
		{name: "missing asset", assetPath: filepath.Join(tempDir, "missing.tar.gz"), checksumPath: validChecksumPath, wantMessage: "missing.tar.gz", wantNotExist: true},
		{name: "unreadable asset", assetPath: tempDir, checksumPath: validChecksumPath, wantMessage: "read " + tempDir},
		{name: "mismatch", assetPath: assetPath, checksumPath: wrongChecksumPath, wantMessage: "checksum mismatch for asset.tar.gz"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			err := verifyDispatcherChecksum(testCase.assetPath, testCase.checksumPath)
			assertDispatcherTestError(t, err, testCase.wantMessage, testCase.wantNotExist)
		})
	}
}

func assertDispatcherTestError(t *testing.T, err error, wantMessage string, wantNotExist bool) {
	t.Helper()
	if wantNotExist && !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("expected a not-exist error, got %v", err)
	}
	if err == nil || !strings.Contains(err.Error(), wantMessage) {
		t.Fatalf("expected error containing %q, got %v", wantMessage, err)
	}
}

func TestVerifyDispatcherChecksumAcceptsUppercaseDigest(t *testing.T) {
	// Verifies the expected digest is compared case-insensitively.
	tempDir := t.TempDir()
	assetPath := filepath.Join(tempDir, "asset.tar.gz")
	writeDispatcherTestFile(t, assetPath, "asset")
	checksumPath := filepath.Join(tempDir, "asset.tar.gz.sha256")
	writeDispatcherTestFile(t, checksumPath, strings.ToUpper(string(dispatcherTestChecksumLine(t, assetPath))))

	if err := verifyDispatcherChecksum(assetPath, checksumPath); err != nil {
		t.Fatalf("uppercase digest must be accepted: %v", err)
	}
}

func TestExtractDispatcherRealCLIFromTarGzFailures(t *testing.T) {
	// Verifies tar.gz extraction rejects missing, corrupt, and runner-less archives.
	tempDir := t.TempDir()
	runnerlessArchivePath := filepath.Join(tempDir, "runnerless.tar.gz")
	writeDispatcherTarGzArchive(t, runnerlessArchivePath, []dispatcherArchiveTestEntry{
		{Name: "README.md", Content: "docs"},
	})
	notGzipPath := filepath.Join(tempDir, "not-gzip.tar.gz")
	writeDispatcherTestFile(t, notGzipPath, "plain text")
	corruptTarPath := filepath.Join(tempDir, "corrupt-tar.tar.gz")
	writeDispatcherGzipFile(t, corruptTarPath, []byte(strings.Repeat("x", 600)))

	cases := []struct {
		name         string
		archivePath  string
		wantMessage  string
		wantNotExist bool
	}{
		{name: "missing archive", archivePath: filepath.Join(tempDir, "missing.tar.gz"), wantMessage: "missing.tar.gz", wantNotExist: true},
		{name: "not gzip", archivePath: notGzipPath, wantMessage: "gzip: invalid header"},
		{name: "corrupt tar", archivePath: corruptTarPath, wantMessage: "archive/tar"},
		{name: "runner missing", archivePath: runnerlessArchivePath, wantMessage: "archive does not contain uloop-project-runner"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			destinationPath := filepath.Join(t.TempDir(), "uloop-project-runner")
			err := extractDispatcherRealCLI(testCase.archivePath, "asset.tar.gz", destinationPath, "linux")
			assertDispatcherTestError(t, err, testCase.wantMessage, testCase.wantNotExist)
			if fileExists(destinationPath) {
				t.Fatal("a failed extraction must not create the destination file")
			}
		})
	}
}

func writeDispatcherGzipFile(t *testing.T, filePath string, content []byte) {
	t.Helper()
	var buffer bytes.Buffer
	gzipWriter := gzip.NewWriter(&buffer)
	if _, err := gzipWriter.Write(content); err != nil {
		t.Fatalf("failed to write gzip content: %v", err)
	}
	if err := gzipWriter.Close(); err != nil {
		t.Fatalf("failed to close gzip writer: %v", err)
	}
	if err := os.WriteFile(filePath, buffer.Bytes(), 0o644); err != nil {
		t.Fatalf("failed to write gzip file: %v", err)
	}
}

func TestExtractDispatcherRealCLIFromTarGzSkipsNonRegularEntries(t *testing.T) {
	// Verifies a directory entry named like the runner is skipped and the regular file in a subdirectory is extracted.
	tempDir := t.TempDir()
	archivePath := filepath.Join(tempDir, "runner.tar.gz")
	writeDispatcherTarGzArchiveWithDirectory(t, archivePath, "uloop-project-runner/", []dispatcherArchiveTestEntry{
		{Name: "docs/README.md", Content: "docs"},
		{Name: "bin/uloop-project-runner", Content: "runner"},
	})
	destinationPath := filepath.Join(tempDir, "extracted")

	if err := extractDispatcherRealCLI(archivePath, "asset.tar.gz", destinationPath, "linux"); err != nil {
		t.Fatalf("extractDispatcherRealCLI failed: %v", err)
	}
	assertFileContent(t, destinationPath, "runner")
}

func TestExtractDispatcherRealCLIFromZipFailures(t *testing.T) {
	// Verifies zip extraction rejects corrupt archives and archives without the runner executable.
	tempDir := t.TempDir()
	runnerlessArchivePath := filepath.Join(tempDir, "runnerless.zip")
	writeDispatcherZipArchive(t, runnerlessArchivePath, []dispatcherArchiveTestEntry{
		{Name: "uloop-project-runner.exe/", Content: ""},
		{Name: "README.md", Content: "docs"},
	})
	corruptArchivePath := filepath.Join(tempDir, "corrupt.zip")
	writeDispatcherTestFile(t, corruptArchivePath, "not a zip")

	cases := []struct {
		name        string
		archivePath string
		wantMessage string
	}{
		{name: "corrupt zip", archivePath: corruptArchivePath, wantMessage: "zip: not a valid zip file"},
		{name: "runner missing", archivePath: runnerlessArchivePath, wantMessage: "archive does not contain uloop-project-runner.exe"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			destinationPath := filepath.Join(t.TempDir(), "uloop-project-runner.exe")
			err := extractDispatcherRealCLI(testCase.archivePath, "asset.zip", destinationPath, "windows")
			assertDispatcherTestError(t, err, testCase.wantMessage, false)
		})
	}
}

func TestExtractDispatcherRealCLIReportsUnwritableDestination(t *testing.T) {
	// Verifies extraction surfaces a destination write failure for both archive formats.
	tempDir := t.TempDir()
	tarPath := filepath.Join(tempDir, "runner.tar.gz")
	writeDispatcherTarGzArchive(t, tarPath, []dispatcherArchiveTestEntry{{Name: "uloop-project-runner", Content: "runner"}})
	zipPath := filepath.Join(tempDir, "runner.zip")
	writeDispatcherZipArchive(t, zipPath, []dispatcherArchiveTestEntry{{Name: "uloop-project-runner.exe", Content: "runner"}})
	destinationPath := filepath.Join(tempDir, "missing", "runner")

	for _, err := range []error{
		extractDispatcherRealCLI(tarPath, "asset.tar.gz", destinationPath, "linux"),
		extractDispatcherRealCLI(zipPath, "asset.zip", destinationPath, "windows"),
	} {
		if !errors.Is(err, os.ErrNotExist) || !strings.Contains(err.Error(), "open "+destinationPath) {
			t.Fatalf("expected the destination open error, got %v", err)
		}
	}
}

func TestInstallDownloadedDispatcherRealCLIRetriesRenameAfterRemovingIncompleteEntry(t *testing.T) {
	// Verifies a first rename failure over an incomplete cache entry removes that entry and retries the rename.
	tempDir := t.TempDir()
	realCLIPath := filepath.Join(tempDir, "uloop-project-runner")
	tempRealCLIPath := filepath.Join(tempDir, "downloaded")
	writeDispatcherTestFile(t, realCLIPath, "incomplete")
	writeDispatcherTestFile(t, tempRealCLIPath, "downloaded")

	previousRename := dispatcherRename
	t.Cleanup(func() {
		dispatcherRename = previousRename
	})
	renameCalls := 0
	dispatcherRename = func(oldPath string, newPath string) error {
		renameCalls++
		if renameCalls == 1 {
			return errors.New("destination busy")
		}
		if fileExists(newPath) {
			t.Fatalf("incomplete entry must be removed before the retry: %s", newPath)
		}
		return previousRename(oldPath, newPath)
	}

	path, err := installDownloadedDispatcherRealCLI(tempRealCLIPath, realCLIPath)
	if err != nil {
		t.Fatalf("installDownloadedDispatcherRealCLI failed: %v", err)
	}
	if path != realCLIPath || renameCalls != 2 {
		t.Fatalf("unexpected result: path=%s renameCalls=%d", path, renameCalls)
	}
	assertFileContent(t, realCLIPath, "downloaded")
	assertFileContent(t, dispatcherRealCLIReadyPath(realCLIPath), "ready\n")
}

func TestInstallDownloadedDispatcherRealCLIReportsSecondRenameFailure(t *testing.T) {
	// Verifies a rename that keeps failing is reported and no READY marker is written.
	tempDir := t.TempDir()
	realCLIPath := filepath.Join(tempDir, "uloop-project-runner")
	tempRealCLIPath := filepath.Join(tempDir, "downloaded")
	writeDispatcherTestFile(t, tempRealCLIPath, "downloaded")

	previousRename := dispatcherRename
	t.Cleanup(func() {
		dispatcherRename = previousRename
	})
	renameErr := errors.New("rename always fails")
	dispatcherRename = func(string, string) error {
		return renameErr
	}

	_, err := installDownloadedDispatcherRealCLI(tempRealCLIPath, realCLIPath)
	if !errors.Is(err, renameErr) {
		t.Fatalf("expected the rename error, got %v", err)
	}
	if fileExists(dispatcherRealCLIReadyPath(realCLIPath)) {
		t.Fatal("READY must not be written when the runner was not installed")
	}
}

func TestInstallDownloadedDispatcherRealCLIReportsCleanupFailures(t *testing.T) {
	// Verifies failures to clear a stale READY marker or a stale cache entry abort the install.
	cases := []struct {
		name        string
		setup       func(t *testing.T, realCLIPath string)
		blockedPath func(realCLIPath string) string
	}{
		{
			name: "stale READY cannot be removed",
			setup: func(t *testing.T, realCLIPath string) {
				writeDispatcherTestFile(t, filepath.Join(dispatcherRealCLIReadyPath(realCLIPath), "child"), "x")
			},
			blockedPath: dispatcherRealCLIReadyPath,
		},
		{
			name: "stale entry cannot be removed",
			setup: func(t *testing.T, realCLIPath string) {
				writeDispatcherTestFile(t, filepath.Join(realCLIPath, "child"), "x")
			},
			blockedPath: func(realCLIPath string) string { return realCLIPath },
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			tempDir := t.TempDir()
			realCLIPath := filepath.Join(tempDir, "uloop-project-runner")
			tempRealCLIPath := filepath.Join(tempDir, "downloaded")
			writeDispatcherTestFile(t, tempRealCLIPath, "downloaded")
			testCase.setup(t, realCLIPath)
			stubDispatcherRenameFailsOnce(t)

			_, err := installDownloadedDispatcherRealCLI(tempRealCLIPath, realCLIPath)
			if err == nil || !strings.Contains(err.Error(), "remove "+testCase.blockedPath(realCLIPath)+":") {
				t.Fatalf("expected the cleanup failure to abort the install, got %v", err)
			}
			assertFileContent(t, tempRealCLIPath, "downloaded")
		})
	}
}

func stubDispatcherRenameFailsOnce(t *testing.T) {
	t.Helper()
	previousRename := dispatcherRename
	t.Cleanup(func() {
		dispatcherRename = previousRename
	})
	renameCalls := 0
	dispatcherRename = func(oldPath string, newPath string) error {
		renameCalls++
		if renameCalls == 1 {
			return errors.New("destination busy")
		}
		return previousRename(oldPath, newPath)
	}
}

func TestMarkDispatcherRealCLIReadyReportsWriteFailure(t *testing.T) {
	// Verifies a READY marker that cannot be written is reported instead of claiming the runner is ready.
	tempDir := t.TempDir()
	realCLIPath := filepath.Join(tempDir, "uloop-project-runner")
	if err := os.MkdirAll(dispatcherRealCLIReadyPath(realCLIPath), 0o755); err != nil {
		t.Fatalf("failed to create READY directory: %v", err)
	}

	path, err := markDispatcherRealCLIReady(realCLIPath)
	if err == nil || path != "" || !strings.Contains(err.Error(), "is a directory") {
		t.Fatalf("expected a write failure, got path=%q err=%v", path, err)
	}
}

func TestDownloadDispatcherRealCLIForPinFailures(t *testing.T) {
	// Verifies each download stage failure stops the install before a runner is cached.
	archiveDir := t.TempDir()
	archivePath := filepath.Join(archiveDir, "uloop-project-runner-linux-amd64.tar.gz")
	writeDispatcherTarGzArchive(t, archivePath, []dispatcherArchiveTestEntry{{Name: "README.md", Content: "docs"}})
	archiveContent, err := os.ReadFile(archivePath)
	if err != nil {
		t.Fatalf("failed to read archive: %v", err)
	}
	validChecksum := dispatcherTestChecksumLine(t, archivePath)
	assetSuffix := "/uloop-project-runner-linux-amd64.tar.gz"

	cases := []struct {
		name        string
		goarch      string
		bodies      map[string][]byte
		wantMessage string
	}{
		{name: "unsupported platform", goarch: "arm64", wantMessage: "unsupported platform"},
		{name: "archive download", goarch: "amd64", wantMessage: "linux-amd64.tar.gz: Not Found"},
		{name: "checksum download", goarch: "amd64", bodies: map[string][]byte{assetSuffix: archiveContent}, wantMessage: "linux-amd64.tar.gz.sha256: Not Found"},
		{name: "checksum mismatch", goarch: "amd64", bodies: map[string][]byte{assetSuffix: archiveContent, assetSuffix + ".sha256": []byte(strings.Repeat("0", 64))}, wantMessage: "checksum mismatch"},
		{name: "runner missing from archive", goarch: "amd64", bodies: map[string][]byte{assetSuffix: archiveContent, assetSuffix + ".sha256": validChecksum}, wantMessage: "archive does not contain"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			stubDispatcherHTTPResponses(t, testCase.bodies)
			restoreAttestation := stubAttestationVerifyPasses()
			t.Cleanup(restoreAttestation)
			cacheRoot := t.TempDir()

			_, err := downloadDispatcherRealCLIForPin(context.Background(), cacheRoot, dispatcherPin{ProjectRunnerVersion: "3.0.0"}, "linux", testCase.goarch, io.Discard)

			if err == nil || !strings.Contains(err.Error(), testCase.wantMessage) {
				t.Fatalf("expected error containing %q, got %v", testCase.wantMessage, err)
			}
			if fileExists(dispatcherCachedRealCLIPath(cacheRoot, "3.0.0", "linux", testCase.goarch)) {
				t.Fatal("a failed download must not leave a cached runner")
			}
		})
	}
}

func TestDownloadDispatcherRealCLIForPinReportsUnusableCacheRoot(t *testing.T) {
	// Verifies a cache root that is a regular file fails before any download starts.
	cacheRoot := filepath.Join(t.TempDir(), "cache-file")
	writeDispatcherTestFile(t, cacheRoot, "not a directory")
	forbidDispatcherHTTPRequests(t)

	_, err := downloadDispatcherRealCLIForPin(context.Background(), cacheRoot, dispatcherPin{ProjectRunnerVersion: "3.0.0"}, "linux", "amd64", io.Discard)

	if err == nil || !strings.Contains(err.Error(), "mkdir "+cacheRoot) {
		t.Fatalf("expected a cache directory creation error, got %v", err)
	}
}

func TestResolveDispatcherRealCLIDownloadsOnCacheMiss(t *testing.T) {
	// Verifies a cache miss for the pinned version downloads into ULOOP_CACHE_DIR and returns the cached path.
	assetName, err := dispatcherReleaseAssetName(runtime.GOOS, runtime.GOARCH)
	if err != nil {
		t.Skipf("no published runner asset for %s-%s", runtime.GOOS, runtime.GOARCH)
	}
	archivePath := filepath.Join(t.TempDir(), assetName)
	entryName := dispatcherRealCLIFileName(runtime.GOOS)
	if strings.HasSuffix(assetName, ".zip") {
		writeDispatcherZipArchive(t, archivePath, []dispatcherArchiveTestEntry{{Name: entryName, Content: "runner"}})
	} else {
		writeDispatcherTarGzArchive(t, archivePath, []dispatcherArchiveTestEntry{{Name: entryName, Content: "runner"}})
	}
	archiveContent, err := os.ReadFile(archivePath)
	if err != nil {
		t.Fatalf("failed to read archive: %v", err)
	}
	stubDispatcherHTTPResponses(t, map[string][]byte{
		"/" + assetName:             archiveContent,
		"/" + assetName + ".sha256": dispatcherTestChecksumLine(t, archivePath),
	})
	restoreAttestation := stubAttestationVerifyPasses()
	t.Cleanup(restoreAttestation)
	cacheRoot := t.TempDir()
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	t.Setenv(nativepath.ProjectRunnerPathEnvName, "")

	realCLIPath, err := resolveDispatcherRealCLI(context.Background(), dispatcherPin{ProjectRunnerVersion: " 3.0.0 "}, io.Discard)
	if err != nil {
		t.Fatalf("resolveDispatcherRealCLI failed: %v", err)
	}
	if realCLIPath != dispatcherCachedRealCLIPath(cacheRoot, "3.0.0", runtime.GOOS, runtime.GOARCH) {
		t.Fatalf("cached path mismatch: %s", realCLIPath)
	}
	assertFileContent(t, realCLIPath, "runner")
}

func TestResolveDispatcherRealCLIReportsMissingCacheRoot(t *testing.T) {
	// Verifies an unresolvable cache root is reported instead of downloading into an arbitrary directory.
	unsetDispatcherCacheRoot(t)
	t.Setenv(nativepath.ProjectRunnerPathEnvName, "")
	// A regression must not download into the working directory.
	t.Chdir(t.TempDir())
	forbidDispatcherHTTPRequests(t)

	_, err := resolveDispatcherRealCLI(context.Background(), dispatcherPin{ProjectRunnerVersion: "3.0.0"}, io.Discard)

	if err == nil || !strings.Contains(err.Error(), "$HOME is not defined") {
		t.Fatalf("expected a cache root resolution error, got %v", err)
	}
}

func writeDispatcherTarGzArchiveWithDirectory(t *testing.T, archivePath string, directoryName string, entries []dispatcherArchiveTestEntry) {
	t.Helper()
	var buffer bytes.Buffer
	gzipWriter := gzip.NewWriter(&buffer)
	tarWriter := tar.NewWriter(gzipWriter)
	if err := tarWriter.WriteHeader(&tar.Header{Name: directoryName, Typeflag: tar.TypeDir, Mode: 0o755}); err != nil {
		t.Fatalf("failed to write tar directory header: %v", err)
	}
	for _, entry := range entries {
		content := []byte(entry.Content)
		if err := tarWriter.WriteHeader(&tar.Header{Name: entry.Name, Mode: 0o755, Size: int64(len(content))}); err != nil {
			t.Fatalf("failed to write tar header: %v", err)
		}
		if _, err := tarWriter.Write(content); err != nil {
			t.Fatalf("failed to write tar content: %v", err)
		}
	}
	if err := tarWriter.Close(); err != nil {
		t.Fatalf("failed to close tar writer: %v", err)
	}
	if err := gzipWriter.Close(); err != nil {
		t.Fatalf("failed to close gzip writer: %v", err)
	}
	if err := os.WriteFile(archivePath, buffer.Bytes(), 0o644); err != nil {
		t.Fatalf("failed to write tar archive: %v", err)
	}
}

// forbidDispatcherHTTPRequests fails the test on any HTTP request, for paths that must stop before downloading.
func forbidDispatcherHTTPRequests(t *testing.T) {
	t.Helper()
	previousHTTPClient := dispatcherHTTPClient
	t.Cleanup(func() {
		dispatcherHTTPClient = previousHTTPClient
	})
	dispatcherHTTPClient = &http.Client{
		Transport: dispatcherRoundTripFunc(func(request *http.Request) (*http.Response, error) {
			t.Errorf("unexpected HTTP request: %s", request.URL)
			return nil, errors.New("HTTP requests are forbidden in this test")
		}),
	}
}
