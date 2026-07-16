package dispatcher

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"

	sharedversion "github.com/hatayama/unity-cli-loop/common/version"
)

const (
	dispatcherPackagesManifestRelativePath = "Packages/manifest.json"
	dispatcherPackagesLockRelativePath     = "Packages/packages-lock.json"
	dispatcherPackageJSONFileName          = "package.json"
	dispatcherV2MajorVersion               = "2"
)

type dispatcherV2Project struct {
	IsV2           bool
	PackageVersion string
}

type dispatcherPackagesManifest struct {
	Dependencies map[string]json.RawMessage `json:"dependencies"`
}

type dispatcherPackagesLock struct {
	Dependencies map[string]dispatcherPackageLockEntry `json:"dependencies"`
}

type dispatcherPackageLockEntry struct {
	Version string `json:"version"`
}

type dispatcherPackageJSON struct {
	Version string `json:"version"`
}

// detectV2DispatcherProject identifies pinless Unity projects that use the V2 package.
// Why: V2 projects have no project runner pin, but pin absence alone can also indicate a broken V3 installation.
func detectV2DispatcherProject(projectRoot string) (dispatcherV2Project, error) {
	hasPin, err := dispatcherProjectHasPin(projectRoot)
	if err != nil {
		return dispatcherV2Project{}, err
	}
	if hasPin {
		return dispatcherV2Project{}, nil
	}

	hasPackage, err := dispatcherProjectHasUnityPackage(projectRoot)
	if err != nil {
		return dispatcherV2Project{}, err
	}
	if !hasPackage {
		return dispatcherV2Project{}, nil
	}

	packageVersion, found, err := dispatcherV2PackageCacheVersion(projectRoot)
	if err != nil {
		return dispatcherV2Project{}, err
	}
	if !found {
		packageVersion, found, err = dispatcherV2PackageLockVersion(projectRoot)
		if err != nil {
			return dispatcherV2Project{}, err
		}
	}
	if !found || !isDispatcherV2PackageVersion(packageVersion) {
		return dispatcherV2Project{}, nil
	}

	return dispatcherV2Project{IsV2: true, PackageVersion: packageVersion}, nil
}

func dispatcherProjectHasPin(projectRoot string) (bool, error) {
	for _, candidate := range dispatcherPinCandidatePaths(projectRoot) {
		_, err := os.Stat(candidate.Path)
		if err == nil {
			return true, nil
		}
		if !errors.Is(err, os.ErrNotExist) {
			return false, err
		}
	}
	return false, nil
}

func dispatcherProjectHasUnityPackage(projectRoot string) (bool, error) {
	manifestPath := filepath.Join(projectRoot, filepath.FromSlash(dispatcherPackagesManifestRelativePath))
	content, err := os.ReadFile(manifestPath)
	if errors.Is(err, os.ErrNotExist) {
		return false, nil
	}
	if err != nil {
		return false, err
	}

	manifest := dispatcherPackagesManifest{}
	if err := json.Unmarshal(content, &manifest); err != nil {
		return false, fmt.Errorf("parse %s: %w", manifestPath, err)
	}
	_, found := manifest.Dependencies[dispatcherUnityPackageName]
	return found, nil
}

func dispatcherV2PackageCacheVersion(projectRoot string) (string, bool, error) {
	cacheDirectory := filepath.Join(projectRoot, "Library", "PackageCache")
	entries, err := os.ReadDir(cacheDirectory)
	if errors.Is(err, os.ErrNotExist) {
		return "", false, nil
	}
	if err != nil {
		return "", false, err
	}

	prefix := dispatcherUnityPackageName + "@"
	for _, entry := range entries {
		if !entry.IsDir() || !strings.HasPrefix(entry.Name(), prefix) {
			continue
		}
		packagePath := filepath.Join(cacheDirectory, entry.Name(), dispatcherPackageJSONFileName)
		version, err := readDispatcherPackageVersion(packagePath)
		if err != nil {
			continue
		}
		if isDispatcherV2PackageVersion(version) {
			return version, true, nil
		}
	}
	return "", false, nil
}

func dispatcherV2PackageLockVersion(projectRoot string) (string, bool, error) {
	lockPath := filepath.Join(projectRoot, filepath.FromSlash(dispatcherPackagesLockRelativePath))
	content, err := os.ReadFile(lockPath)
	if errors.Is(err, os.ErrNotExist) {
		return "", false, nil
	}
	if err != nil {
		return "", false, err
	}

	lock := dispatcherPackagesLock{}
	if err := json.Unmarshal(content, &lock); err != nil {
		return "", false, fmt.Errorf("parse %s: %w", lockPath, err)
	}
	entry, found := lock.Dependencies[dispatcherUnityPackageName]
	if !found {
		return "", false, nil
	}
	return entry.Version, entry.Version != "", nil
}

func readDispatcherPackageVersion(packagePath string) (string, error) {
	content, err := os.ReadFile(packagePath)
	if err != nil {
		return "", err
	}
	packageInfo := dispatcherPackageJSON{}
	if err := json.Unmarshal(content, &packageInfo); err != nil {
		return "", fmt.Errorf("parse %s: %w", packagePath, err)
	}
	return packageInfo.Version, nil
}

func isDispatcherV2PackageVersion(version string) bool {
	trimmed := strings.TrimSpace(version)
	if !sharedversion.IsValid(trimmed) {
		return false
	}
	major, _, _ := strings.Cut(strings.TrimLeft(trimmed, "vV"), ".")
	return major == dispatcherV2MajorVersion
}
