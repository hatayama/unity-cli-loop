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

	sharedupdate "github.com/hatayama/unity-cli-loop/cli/internal/update"
)

var dispatcherHTTPClient = http.DefaultClient

func resolveDispatcherRealCLI(ctx context.Context, pin dispatcherPin) (string, error) {
	if siblingPath, ok := dispatcherSiblingRealCLIPath(pin); ok {
		return siblingPath, nil
	}

	cacheRoot, err := dispatcherCacheRoot(runtime.GOOS)
	if err != nil {
		return "", err
	}
	realCLIPath := dispatcherCachedRealCLIPath(cacheRoot, pin.CLIVersion, runtime.GOOS, runtime.GOARCH)
	if isExecutableFile(realCLIPath) {
		return realCLIPath, nil
	}

	return downloadDispatcherRealCLI(ctx, cacheRoot, pin.CLIVersion, runtime.GOOS, runtime.GOARCH)
}

func dispatcherSiblingRealCLIPath(pin dispatcherPin) (string, bool) {
	if pin.CLIVersion != version {
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

func dispatcherCachedRealCLIPath(cacheRoot string, cliVersion string, goos string, goarch string) string {
	return filepath.Join(
		cacheRoot,
		dispatcherVersionsDirectoryName,
		cliVersion,
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

func dispatcherLegacyCLIFileName(goos string) string {
	if goos == "windows" {
		return dispatcherLegacyWindowsFileName
	}
	return dispatcherLegacyUnixFileName
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

func downloadDispatcherRealCLI(ctx context.Context, cacheRoot string, cliVersion string, goos string, goarch string) (string, error) {
	assetName, err := dispatcherReleaseAssetName(goos, goarch)
	if err != nil {
		return "", err
	}
	realCLIPath := dispatcherCachedRealCLIPath(cacheRoot, cliVersion, goos, goarch)
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
	assetURL := dispatcherReleaseAssetURL(cliVersion, assetName)
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
	if err := os.Remove(realCLIPath); err != nil && !errors.Is(err, os.ErrNotExist) {
		return "", err
	}
	if err := os.Rename(tempRealCLIPath, realCLIPath); err != nil {
		return "", err
	}
	return realCLIPath, nil
}

func dispatcherReleaseAssetName(goos string, goarch string) (string, error) {
	switch goos {
	case "darwin":
		if goarch != "arm64" && goarch != "amd64" {
			return "", fmt.Errorf("unsupported darwin architecture: %s", goarch)
		}
		return "uloop-darwin-" + goarch + ".tar.gz", nil
	case "windows":
		if goarch != "amd64" {
			return "", fmt.Errorf("unsupported windows architecture: %s", goarch)
		}
		return "uloop-windows-amd64.zip", nil
	default:
		return "", fmt.Errorf("unsupported platform: %s-%s", goos, goarch)
	}
}

func dispatcherReleaseAssetURL(cliVersion string, assetName string) string {
	return dispatcherReleaseBaseURL + "/" + sharedupdate.ReleaseTag(cliVersion) + "/" + assetName
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
	file, err := os.Open(archivePath)
	if err != nil {
		return err
	}
	defer func() {
		_ = file.Close()
	}()
	gzipReader, err := gzip.NewReader(file)
	if err != nil {
		return err
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
			return err
		}
		if header.Typeflag != tar.TypeReg {
			continue
		}
		if !dispatcherArchiveEntryMatchesCLI(header.Name, goos) {
			continue
		}
		return writeDispatcherExtractedCLI(destinationPath, tarReader)
	}
	return fmt.Errorf("archive does not contain %s", dispatcherRealCLIFileName(goos))
}

func extractDispatcherRealCLIFromZip(archivePath string, destinationPath string, goos string) error {
	reader, err := zip.OpenReader(archivePath)
	if err != nil {
		return err
	}
	defer func() {
		_ = reader.Close()
	}()
	for _, entry := range reader.File {
		if entry.FileInfo().IsDir() || !dispatcherArchiveEntryMatchesCLI(entry.Name, goos) {
			continue
		}
		entryReader, err := entry.Open()
		if err != nil {
			return err
		}
		writeErr := writeDispatcherExtractedCLI(destinationPath, entryReader)
		closeErr := entryReader.Close()
		if writeErr != nil {
			return writeErr
		}
		return closeErr
	}
	return fmt.Errorf("archive does not contain %s", dispatcherRealCLIFileName(goos))
}

func dispatcherArchiveEntryMatchesCLI(entryName string, goos string) bool {
	normalizedEntryName := strings.ReplaceAll(entryName, `\`, "/")
	baseName := path.Base(normalizedEntryName)
	return baseName == dispatcherRealCLIFileName(goos) || baseName == dispatcherLegacyCLIFileName(goos)
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
