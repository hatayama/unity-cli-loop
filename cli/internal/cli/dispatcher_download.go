package cli

import (
	"archive/tar"
	"archive/zip"
	"compress/gzip"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"path"
	"path/filepath"
	"runtime"
	"strings"
	"time"

	sharedupdate "github.com/hatayama/unity-cli-loop/cli/internal/update"
)

var dispatcherHTTPClient = &http.Client{Timeout: 2 * time.Minute}

func resolveDispatcherRealCLI(ctx context.Context, pin dispatcherPin, stderr io.Writer) (string, error) {
	pin.ProjectRunnerVersion = strings.TrimSpace(pin.ProjectRunnerVersion)
	if err := validateDispatcherProjectRunnerVersion(pin.ProjectRunnerVersion); err != nil {
		return "", err
	}
	if siblingPath, ok := dispatcherSiblingRealCLIPath(pin); ok {
		return siblingPath, nil
	}

	cacheRoot, err := dispatcherCacheRoot(runtime.GOOS)
	if err != nil {
		return "", err
	}
	realCLIPath := dispatcherCachedRealCLIPath(cacheRoot, pin.ProjectRunnerVersion, runtime.GOOS, runtime.GOARCH)
	if isExecutableFile(realCLIPath) {
		return realCLIPath, nil
	}

	return downloadDispatcherRealCLIForPin(ctx, cacheRoot, pin, runtime.GOOS, runtime.GOARCH, stderr)
}

func dispatcherSiblingRealCLIPath(pin dispatcherPin) (string, bool) {
	if pin.ProjectRunnerVersion != version {
		return "", false
	}
	executablePath, err := os.Executable()
	if err != nil {
		return "", false
	}
	if resolvedPath, err := filepath.EvalSymlinks(executablePath); err == nil {
		executablePath = resolvedPath
	}
	candidate := filepath.Join(filepath.Dir(executablePath), dispatcherRealCLIFileName(runtime.GOOS))
	if !isExecutableFile(candidate) {
		return "", false
	}
	return candidate, true
}

func dispatcherCacheRoot(goos string) (string, error) {
	if explicitCacheRoot := strings.TrimSpace(os.Getenv(dispatcherCacheDirEnvName)); explicitCacheRoot != "" {
		return explicitCacheRoot, nil
	}
	switch goos {
	case "darwin":
		home, err := os.UserHomeDir()
		if err != nil {
			return "", err
		}
		return filepath.Join(home, "Library", "Caches", dispatcherCacheDirectoryName), nil
	case "windows":
		localAppData := os.Getenv(nativeLocalAppDataEnvName)
		if localAppData == "" {
			return "", errors.New("LOCALAPPDATA is required to resolve the uloop cache directory")
		}
		return filepath.Join(localAppData, dispatcherCacheDirectoryName), nil
	default:
		if xdgCacheHome := strings.TrimSpace(os.Getenv("XDG_CACHE_HOME")); xdgCacheHome != "" {
			return filepath.Join(xdgCacheHome, dispatcherCacheDirectoryName), nil
		}
		home, err := os.UserHomeDir()
		if err != nil {
			return "", err
		}
		return filepath.Join(home, ".cache", dispatcherCacheDirectoryName), nil
	}
}

func dispatcherCachedRealCLIPath(cacheRoot string, projectRunnerVersion string, goos string, goarch string) string {
	return filepath.Join(
		cacheRoot,
		dispatcherVersionsDirectoryName,
		projectRunnerVersion,
		dispatcherPlatformName(goos, goarch),
		dispatcherRealCLIFileName(goos))
}

func dispatcherPlatformName(goos string, goarch string) string {
	return goos + "-" + goarch
}

func dispatcherRealCLIFileName(goos string) string {
	if goos == "windows" {
		return dispatcherRealCLIWindowsFileName
	}
	return dispatcherRealCLIUnixFileName
}

func isExecutableFile(filePath string) bool {
	info, err := os.Stat(filePath)
	if err != nil || info.IsDir() {
		return false
	}
	if runtime.GOOS == "windows" {
		return true
	}
	return info.Mode()&0o111 != 0
}

func downloadDispatcherRealCLI(ctx context.Context, cacheRoot string, projectRunnerVersion string, goos string, goarch string, stderr io.Writer) (string, error) {
	return downloadDispatcherRealCLIForPin(
		ctx,
		cacheRoot,
		dispatcherPin{ProjectRunnerVersion: projectRunnerVersion},
		goos,
		goarch,
		stderr)
}

func downloadDispatcherRealCLIForPin(ctx context.Context, cacheRoot string, pin dispatcherPin, goos string, goarch string, stderr io.Writer) (string, error) {
	assetName, err := dispatcherReleaseAssetName(goos, goarch)
	if err != nil {
		return "", err
	}
	realCLIPath := dispatcherCachedRealCLIPath(cacheRoot, pin.ProjectRunnerVersion, goos, goarch)
	if err := os.MkdirAll(filepath.Dir(realCLIPath), 0o755); err != nil {
		return "", err
	}

	tempDir, err := os.MkdirTemp(filepath.Dir(realCLIPath), "download-")
	if err != nil {
		return "", err
	}
	defer func() {
		_ = os.RemoveAll(tempDir)
	}()

	archivePath := filepath.Join(tempDir, assetName)
	checksumPath := archivePath + ".sha256"
	assetURL := dispatcherReleaseAssetURL(pin.ProjectRunnerVersion, assetName)
	writeFormat(stderr, "uloop: downloading pinned project runner %s for %s...\n", pin.ProjectRunnerVersion, dispatcherPlatformName(goos, goarch))
	if err := downloadDispatcherFile(ctx, assetURL, archivePath); err != nil {
		return "", err
	}
	if err := downloadDispatcherFile(ctx, assetURL+".sha256", checksumPath); err != nil {
		return "", err
	}
	if err := verifyDispatcherChecksum(archivePath, checksumPath); err != nil {
		return "", err
	}

	tempRealCLIPath := filepath.Join(tempDir, dispatcherRealCLIFileName(goos))
	if err := extractDispatcherRealCLI(archivePath, assetName, tempRealCLIPath, goos); err != nil {
		return "", err
	}
	if err := os.Chmod(tempRealCLIPath, 0o755); err != nil {
		return "", err
	}
	return installDownloadedDispatcherRealCLI(tempRealCLIPath, realCLIPath)
}

func installDownloadedDispatcherRealCLI(tempRealCLIPath string, realCLIPath string) (string, error) {
	if isExecutableFile(realCLIPath) {
		return realCLIPath, nil
	}
	if err := os.Rename(tempRealCLIPath, realCLIPath); err == nil {
		return realCLIPath, nil
	}
	if isExecutableFile(realCLIPath) {
		return realCLIPath, nil
	}
	if err := os.Remove(realCLIPath); err != nil && !errors.Is(err, os.ErrNotExist) {
		return "", err
	}
	if err := os.Rename(tempRealCLIPath, realCLIPath); err != nil {
		return "", err
	}
	return realCLIPath, nil
}

func dispatcherReleaseAssetName(goos string, goarch string) (string, error) {
	assetPrefix := "uloop-project-runner"
	switch goos {
	case "darwin":
		if goarch != "arm64" && goarch != "amd64" {
			return "", fmt.Errorf("unsupported darwin architecture: %s", goarch)
		}
		return assetPrefix + "-darwin-" + goarch + ".tar.gz", nil
	case "windows":
		if goarch != "amd64" {
			return "", fmt.Errorf("unsupported windows architecture: %s", goarch)
		}
		return assetPrefix + "-windows-amd64.zip", nil
	default:
		return "", fmt.Errorf("unsupported platform: %s-%s", goos, goarch)
	}
}

func dispatcherReleaseAssetURL(projectRunnerVersion string, assetName string) string {
	return dispatcherReleaseBaseURL + "/" + sharedupdate.ProjectRunnerReleaseTag(projectRunnerVersion) + "/" + assetName
}

func downloadDispatcherFile(ctx context.Context, url string, destinationPath string) error {
	request, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return err
	}
	response, err := dispatcherHTTPClient.Do(request)
	if err != nil {
		return err
	}
	defer func() {
		_ = response.Body.Close()
	}()
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return fmt.Errorf("download failed for %s: %s", url, response.Status)
	}

	file, err := os.OpenFile(destinationPath, os.O_CREATE|os.O_TRUNC|os.O_WRONLY, 0o644)
	if err != nil {
		return err
	}
	_, copyErr := io.Copy(file, response.Body)
	closeErr := file.Close()
	if copyErr != nil {
		return copyErr
	}
	return closeErr
}

func verifyDispatcherChecksum(assetPath string, checksumPath string) error {
	content, err := os.ReadFile(checksumPath)
	if err != nil {
		return err
	}
	fields := strings.Fields(string(content))
	if len(fields) == 0 {
		return fmt.Errorf("checksum file is empty: %s", checksumPath)
	}
	expectedHash := strings.ToLower(fields[0])

	file, err := os.Open(assetPath)
	if err != nil {
		return err
	}
	defer func() {
		_ = file.Close()
	}()
	hash := sha256.New()
	if _, err := io.Copy(hash, file); err != nil {
		return err
	}
	actualHash := hex.EncodeToString(hash.Sum(nil))
	if actualHash != expectedHash {
		return fmt.Errorf("checksum mismatch for %s", filepath.Base(assetPath))
	}
	return nil
}

func extractDispatcherRealCLI(archivePath string, assetName string, destinationPath string, goos string) error {
	if strings.HasSuffix(assetName, ".zip") {
		return extractDispatcherRealCLIFromZip(archivePath, destinationPath, goos)
	}
	return extractDispatcherRealCLIFromTarGz(archivePath, destinationPath, goos)
}

func extractDispatcherRealCLIFromTarGz(archivePath string, destinationPath string, goos string) error {
	entryFileName := dispatcherRealCLIFileName(goos)
	found, err := extractDispatcherCLIFromTarGzEntry(archivePath, destinationPath, entryFileName)
	if err != nil || found {
		return err
	}
	return fmt.Errorf("archive does not contain %s", entryFileName)
}

func extractDispatcherCLIFromTarGzEntry(archivePath string, destinationPath string, entryFileName string) (bool, error) {
	file, err := os.Open(archivePath)
	if err != nil {
		return false, err
	}
	defer func() {
		_ = file.Close()
	}()
	gzipReader, err := gzip.NewReader(file)
	if err != nil {
		return false, err
	}
	defer func() {
		_ = gzipReader.Close()
	}()
	tarReader := tar.NewReader(gzipReader)
	for {
		header, err := tarReader.Next()
		if errors.Is(err, io.EOF) {
			break
		}
		if err != nil {
			return false, err
		}
		if header.Typeflag != tar.TypeReg {
			continue
		}
		if !dispatcherArchiveEntryMatchesFileName(header.Name, entryFileName) {
			continue
		}
		return true, writeDispatcherExtractedCLI(destinationPath, tarReader)
	}
	return false, nil
}

func extractDispatcherRealCLIFromZip(archivePath string, destinationPath string, goos string) error {
	reader, err := zip.OpenReader(archivePath)
	if err != nil {
		return err
	}
	defer func() {
		_ = reader.Close()
	}()
	entryFileName := dispatcherRealCLIFileName(goos)
	found, err := extractDispatcherCLIFromZipEntry(reader, destinationPath, entryFileName)
	if err != nil || found {
		return err
	}
	return fmt.Errorf("archive does not contain %s", entryFileName)
}

func extractDispatcherCLIFromZipEntry(reader *zip.ReadCloser, destinationPath string, entryFileName string) (bool, error) {
	for _, entry := range reader.File {
		if entry.FileInfo().IsDir() || !dispatcherArchiveEntryMatchesFileName(entry.Name, entryFileName) {
			continue
		}
		entryReader, err := entry.Open()
		if err != nil {
			return false, err
		}
		writeErr := writeDispatcherExtractedCLI(destinationPath, entryReader)
		closeErr := entryReader.Close()
		if writeErr != nil {
			return false, writeErr
		}
		return true, closeErr
	}
	return false, nil
}

func dispatcherArchiveEntryMatchesFileName(entryName string, fileName string) bool {
	normalizedEntryName := strings.ReplaceAll(entryName, `\`, "/")
	baseName := path.Base(normalizedEntryName)
	return baseName == fileName
}

func writeDispatcherExtractedCLI(destinationPath string, reader io.Reader) error {
	file, err := os.OpenFile(destinationPath, os.O_CREATE|os.O_TRUNC|os.O_WRONLY, 0o755)
	if err != nil {
		return err
	}
	_, copyErr := io.Copy(file, reader)
	closeErr := file.Close()
	if copyErr != nil {
		return copyErr
	}
	return closeErr
}
