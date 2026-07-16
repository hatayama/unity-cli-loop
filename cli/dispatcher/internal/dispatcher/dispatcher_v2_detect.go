package dispatcher

import (
	"encoding/json"
	"errors"
	"fmt"
	"net/url"
	"os"
	"path/filepath"
	"sort"
	"strings"

	sharedversion "github.com/hatayama/unity-cli-loop/common/version"
)

const (
	dispatcherPackagesManifestRelativePath = "Packages/manifest.json"
	dispatcherPackagesLockRelativePath     = "Packages/packages-lock.json"
	dispatcherPackageJSONFileName          = "package.json"
	dispatcherV2MajorVersion               = "2"
	dispatcherPackageLockSourceGit         = "git"
)

type dispatcherV2Project struct {
	IsV2                     bool
	PackageVersion           string
	PackageVersionCandidates []string
}

type dispatcherPackagesManifest struct {
	Dependencies map[string]json.RawMessage `json:"dependencies"`
}

type dispatcherPackagesLock struct {
	Dependencies map[string]dispatcherPackageLockEntry `json:"dependencies"`
}

type dispatcherPackageLockEntry struct {
	Version string `json:"version"`
	Source  string `json:"source"`
}

type dispatcherPackageJSON struct {
	Version string `json:"version"`
}

// detectV2DispatcherProject identifies V2 projects from Unity's currently resolved package state.
// Why: a resolved V2 package must take precedence over stale V3 pins that can remain after a downgrade.
func detectV2DispatcherProject(projectRoot string) (dispatcherV2Project, error) {
	manifestDependency, hasPackage, err := dispatcherProjectUnityPackageDependency(projectRoot)
	if err != nil {
		return dispatcherV2Project{}, err
	}
	if !hasPackage {
		return dispatcherV2Project{}, nil
	}

	lockEntry, found, err := dispatcherPackageLockEntryForProject(projectRoot)
	if err != nil {
		return dispatcherV2Project{}, err
	}
	packageVersion := lockEntry.Version
	if found && sharedversion.IsValid(strings.TrimSpace(packageVersion)) {
		if !isDispatcherV2PackageVersion(packageVersion) {
			return dispatcherV2Project{}, nil
		}
		return dispatcherV2Project{IsV2: true, PackageVersion: packageVersion}, nil
	}
	if found && lockEntry.Source != dispatcherPackageLockSourceGit {
		return dispatcherV2Project{}, nil
	}
	if !found && !isDispatcherGitPackageDependency(manifestDependency) {
		return dispatcherV2Project{}, nil
	}

	packageVersions, err := dispatcherPackageCacheVersions(projectRoot)
	if err != nil {
		return dispatcherV2Project{}, err
	}
	if len(packageVersions) == 0 {
		return dispatcherV2Project{}, nil
	}
	if len(packageVersions) == 1 {
		if !isDispatcherV2PackageVersion(packageVersions[0]) {
			return dispatcherV2Project{}, nil
		}
		return dispatcherV2Project{IsV2: true, PackageVersion: packageVersions[0]}, nil
	}
	for _, version := range packageVersions {
		if !isDispatcherV2PackageVersion(version) {
			return dispatcherV2Project{}, fmt.Errorf("multiple package generations found in %s", filepath.Join(projectRoot, "Library", "PackageCache"))
		}
	}

	return dispatcherV2Project{IsV2: true, PackageVersionCandidates: packageVersions}, nil
}

func dispatcherProjectUnityPackageDependency(projectRoot string) (json.RawMessage, bool, error) {
	manifestPath := filepath.Join(projectRoot, filepath.FromSlash(dispatcherPackagesManifestRelativePath))
	content, err := os.ReadFile(manifestPath)
	if errors.Is(err, os.ErrNotExist) {
		return nil, false, nil
	}
	if err != nil {
		return nil, false, err
	}

	manifest := dispatcherPackagesManifest{}
	if err := json.Unmarshal(content, &manifest); err != nil {
		return nil, false, fmt.Errorf("parse %s: %w", manifestPath, err)
	}
	dependency, found := manifest.Dependencies[dispatcherUnityPackageName]
	return dependency, found, nil
}

func isDispatcherGitPackageDependency(dependency json.RawMessage) bool {
	value := ""
	if err := json.Unmarshal(dependency, &value); err != nil {
		return false
	}
	withoutRevision, _, _ := strings.Cut(strings.TrimSpace(value), "#")
	withoutQuery, _, _ := strings.Cut(withoutRevision, "?")
	if strings.HasPrefix(withoutQuery, "git@") {
		return strings.HasSuffix(withoutQuery, ".git")
	}
	parsed, err := url.Parse(withoutQuery)
	if err != nil {
		return false
	}
	switch strings.ToLower(parsed.Scheme) {
	case "git", "ssh", "http", "https", "git+ssh", "git+http", "git+https":
		return strings.HasSuffix(strings.TrimSuffix(parsed.Path, "/"), ".git")
	default:
		return false
	}
}

func dispatcherPackageCacheVersions(projectRoot string) ([]string, error) {
	cacheDirectory := filepath.Join(projectRoot, "Library", "PackageCache")
	entries, err := os.ReadDir(cacheDirectory)
	if errors.Is(err, os.ErrNotExist) {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}

	prefix := dispatcherUnityPackageName + "@"
	versions := map[string]struct{}{}
	for _, entry := range entries {
		if !entry.IsDir() || !strings.HasPrefix(entry.Name(), prefix) {
			continue
		}
		packagePath := filepath.Join(cacheDirectory, entry.Name(), dispatcherPackageJSONFileName)
		version, err := readDispatcherPackageVersion(packagePath)
		if err != nil {
			continue
		}
		if sharedversion.IsValid(strings.TrimSpace(version)) {
			versions[strings.TrimSpace(version)] = struct{}{}
		}
	}
	result := make([]string, 0, len(versions))
	for version := range versions {
		result = append(result, version)
	}
	sort.Strings(result)
	return result, nil
}

func dispatcherPackageLockEntryForProject(projectRoot string) (dispatcherPackageLockEntry, bool, error) {
	lockPath := filepath.Join(projectRoot, filepath.FromSlash(dispatcherPackagesLockRelativePath))
	content, err := os.ReadFile(lockPath)
	if errors.Is(err, os.ErrNotExist) {
		return dispatcherPackageLockEntry{}, false, nil
	}
	if err != nil {
		return dispatcherPackageLockEntry{}, false, err
	}

	lock := dispatcherPackagesLock{}
	if err := json.Unmarshal(content, &lock); err != nil {
		return dispatcherPackageLockEntry{}, false, fmt.Errorf("parse %s: %w", lockPath, err)
	}
	entry, found := lock.Dependencies[dispatcherUnityPackageName]
	if !found {
		return dispatcherPackageLockEntry{}, false, nil
	}
	return entry, true, nil
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
