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
	dispatcherPackagesRelativePath         = "Packages"
	dispatcherPackageJSONFileName          = "package.json"
	dispatcherV2MajorVersion               = "2"
	dispatcherPackageLockSourceGit         = "git"
	dispatcherFileDependencyPrefix         = "file:"
)

type dispatcherV2Project struct {
	IsV2                     bool
	PackageVersion           string
	PackageVersionCandidates []string
	// AmbiguousEmbedded is set when PackageVersionCandidates came from duplicate embedded package
	// directories rather than PackageCache, since the two ambiguities need different recovery guidance.
	AmbiguousEmbedded bool
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
	Hash    string `json:"hash"`
}

type dispatcherPackageJSON struct {
	Name    string `json:"name"`
	Version string `json:"version"`
}

// detectV2DispatcherProject identifies V2 projects from Unity's currently resolved package state.
// Why: a resolved V2 package must take precedence over stale V3 pins that can remain after a downgrade.
// Why disk vs. lock priority: authority follows where Unity actually loads the package entity from.
// For registry/git dependencies Unity loads a PackageCache copy, so the lock's resolved semver is
// authoritative; for file:/embedded packages Unity loads the on-disk directory directly, so that
// directory's own package.json is authoritative there, even when the lock has no usable version for it.
func detectV2DispatcherProject(projectRoot string) (dispatcherV2Project, error) {
	manifestDependency, hasPackage, err := dispatcherProjectUnityPackageDependency(projectRoot)
	if err != nil {
		return dispatcherV2Project{}, err
	}
	if !hasPackage {
		return detectV2DispatcherEmbeddedProject(projectRoot)
	}

	lockEntry, found, err := dispatcherPackageLockEntryForProject(projectRoot)
	if err != nil {
		return dispatcherV2Project{}, err
	}
	if project, handled := detectV2DispatcherFromResolvedLockVersion(lockEntry, found); handled {
		return project, nil
	}

	if isDispatcherFileDependency(manifestDependency) {
		return detectV2DispatcherFileDependencyProject(projectRoot, manifestDependency)
	}

	if !shouldDetectV2FromPackageCache(found, lockEntry, manifestDependency) {
		return dispatcherV2Project{}, nil
	}

	return detectV2DispatcherFromPackageCache(projectRoot, lockEntry, found)
}

// detectV2DispatcherFromResolvedLockVersion trusts packages-lock.json when Unity
// already resolved a valid semver. Why not fall through: that lock version is
// the entity Unity loads for registry/git packages.
func detectV2DispatcherFromResolvedLockVersion(lockEntry dispatcherPackageLockEntry, found bool) (dispatcherV2Project, bool) {
	packageVersion := lockEntry.Version
	if !found || !sharedversion.IsValid(strings.TrimSpace(packageVersion)) {
		return dispatcherV2Project{}, false
	}
	if !isDispatcherV2PackageVersion(packageVersion) {
		return dispatcherV2Project{}, true
	}
	return dispatcherV2Project{IsV2: true, PackageVersion: packageVersion}, true
}

func shouldDetectV2FromPackageCache(found bool, lockEntry dispatcherPackageLockEntry, manifestDependency json.RawMessage) bool {
	if found && lockEntry.Source != dispatcherPackageLockSourceGit {
		return false
	}
	if !found && !isDispatcherGitPackageDependency(manifestDependency) {
		return false
	}
	return true
}

func detectV2DispatcherFromPackageCache(projectRoot string, lockEntry dispatcherPackageLockEntry, found bool) (dispatcherV2Project, error) {
	packageCacheEntries, err := dispatcherPackageCacheEntries(projectRoot)
	if err != nil {
		return dispatcherV2Project{}, err
	}
	if project, handled := detectV2DispatcherFromGitCacheHash(packageCacheEntries, lockEntry, found); handled {
		return project, nil
	}
	return detectV2DispatcherFromDistinctCacheVersions(packageCacheEntries, projectRoot)
}

// detectV2DispatcherFromGitCacheHash binds a git lock hash to one PackageCache
// generation. Why hash match first: multiple cached copies can coexist after
// upgrades, and only the lock's hash identifies the loaded one.
func detectV2DispatcherFromGitCacheHash(packageCacheEntries []dispatcherPackageCacheEntry, lockEntry dispatcherPackageLockEntry, found bool) (dispatcherV2Project, bool) {
	if !found || lockEntry.Source != dispatcherPackageLockSourceGit {
		return dispatcherV2Project{}, false
	}
	version, ok := dispatcherPackageCacheVersionForHash(packageCacheEntries, lockEntry.Hash)
	if !ok {
		return dispatcherV2Project{}, false
	}
	if !isDispatcherV2PackageVersion(version) {
		return dispatcherV2Project{}, true
	}
	return dispatcherV2Project{IsV2: true, PackageVersion: version}, true
}

func detectV2DispatcherFromDistinctCacheVersions(packageCacheEntries []dispatcherPackageCacheEntry, projectRoot string) (dispatcherV2Project, error) {
	packageVersions := dispatcherDistinctPackageCacheVersions(packageCacheEntries)
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

// isDispatcherFileDependency reports whether a manifest dependency value is a file: reference,
// covering both local checkouts outside Packages/ and embedded packages referenced by relative path.
func isDispatcherFileDependency(dependency json.RawMessage) bool {
	value := ""
	if err := json.Unmarshal(dependency, &value); err != nil {
		return false
	}
	return strings.HasPrefix(strings.TrimSpace(value), dispatcherFileDependencyPrefix)
}

// detectV2DispatcherFileDependencyProject reads the package.json at a file: dependency's target
// directly, since file: sources have no reliable semver in packages-lock.json or PackageCache.
func detectV2DispatcherFileDependencyProject(projectRoot string, dependency json.RawMessage) (dispatcherV2Project, error) {
	value := ""
	if err := json.Unmarshal(dependency, &value); err != nil {
		return dispatcherV2Project{}, nil
	}
	targetDirectory, ok := resolveDispatcherFileDependencyTarget(projectRoot, value)
	if !ok {
		return dispatcherV2Project{}, nil
	}
	return detectV2DispatcherPackageAtPath(filepath.Join(targetDirectory, dispatcherPackageJSONFileName))
}

// resolveDispatcherFileDependencyTarget resolves a manifest file: dependency value to an absolute
// directory. Relative values are resolved against Packages/, matching Unity's own resolution base.
// Backslashes are normalized before joining so Windows-authored manifest values still resolve on any OS.
func resolveDispatcherFileDependencyTarget(projectRoot string, dependencyValue string) (string, bool) {
	trimmed := strings.TrimSpace(dependencyValue)
	if !strings.HasPrefix(trimmed, dispatcherFileDependencyPrefix) {
		return "", false
	}
	rawPath := strings.TrimPrefix(trimmed, dispatcherFileDependencyPrefix)
	normalizedPath := filepath.FromSlash(strings.ReplaceAll(rawPath, "\\", "/"))
	if normalizedPath == "" {
		return "", false
	}
	if filepath.IsAbs(normalizedPath) {
		return filepath.Clean(normalizedPath), true
	}
	packagesDirectory := filepath.Join(projectRoot, dispatcherPackagesRelativePath)
	return filepath.Clean(filepath.Join(packagesDirectory, normalizedPath)), true
}

// detectV2DispatcherEmbeddedProject scans Packages/ for embedded packages, which never appear in
// manifest.json dependencies and are therefore invisible to the manifest-driven detection above.
func detectV2DispatcherEmbeddedProject(projectRoot string) (dispatcherV2Project, error) {
	packagesDirectory := filepath.Join(projectRoot, dispatcherPackagesRelativePath)
	entries, err := os.ReadDir(packagesDirectory)
	if errors.Is(err, os.ErrNotExist) {
		return dispatcherV2Project{}, nil
	}
	if err != nil {
		return dispatcherV2Project{}, err
	}

	versions := map[string]struct{}{}
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		packagePath := filepath.Join(packagesDirectory, entry.Name(), dispatcherPackageJSONFileName)
		project, err := detectV2DispatcherPackageAtPath(packagePath)
		if err != nil {
			return dispatcherV2Project{}, err
		}
		if project.IsV2 {
			versions[project.PackageVersion] = struct{}{}
		}
	}

	switch len(versions) {
	case 0:
		return dispatcherV2Project{}, nil
	case 1:
		for version := range versions {
			return dispatcherV2Project{IsV2: true, PackageVersion: version}, nil
		}
	}
	candidates := make([]string, 0, len(versions))
	for version := range versions {
		candidates = append(candidates, version)
	}
	sort.Strings(candidates)
	return dispatcherV2Project{IsV2: true, PackageVersionCandidates: candidates, AmbiguousEmbedded: true}, nil
}

// detectV2DispatcherPackageAtPath reads a package.json directly and reports whether it is the V2
// Unity package by name, matching by content rather than by directory naming convention.
func detectV2DispatcherPackageAtPath(packagePath string) (dispatcherV2Project, error) {
	packageInfo, err := readDispatcherPackageInfo(packagePath)
	if err != nil {
		return dispatcherV2Project{}, nil
	}
	if packageInfo.Name != dispatcherUnityPackageName {
		return dispatcherV2Project{}, nil
	}
	version := strings.TrimSpace(packageInfo.Version)
	if !isDispatcherV2PackageVersion(version) {
		return dispatcherV2Project{}, nil
	}
	return dispatcherV2Project{IsV2: true, PackageVersion: version}, nil
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

// dispatcherPackageCacheEntry pairs a PackageCache directory's hash suffix with its resolved
// package version, so a lock's git hash can be matched to the exact generation it produced.
type dispatcherPackageCacheEntry struct {
	DirectorySuffix string
	Version         string
}

func dispatcherPackageCacheEntries(projectRoot string) ([]dispatcherPackageCacheEntry, error) {
	cacheDirectory := filepath.Join(projectRoot, "Library", "PackageCache")
	entries, err := os.ReadDir(cacheDirectory)
	if errors.Is(err, os.ErrNotExist) {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}

	prefix := dispatcherUnityPackageName + "@"
	result := make([]dispatcherPackageCacheEntry, 0, len(entries))
	for _, entry := range entries {
		if !entry.IsDir() || !strings.HasPrefix(entry.Name(), prefix) {
			continue
		}
		packagePath := filepath.Join(cacheDirectory, entry.Name(), dispatcherPackageJSONFileName)
		version, err := readDispatcherPackageVersion(packagePath)
		if err != nil {
			continue
		}
		trimmedVersion := strings.TrimSpace(version)
		if !sharedversion.IsValid(trimmedVersion) {
			continue
		}
		result = append(result, dispatcherPackageCacheEntry{
			DirectorySuffix: strings.TrimPrefix(entry.Name(), prefix),
			Version:         trimmedVersion,
		})
	}
	return result, nil
}

func dispatcherDistinctPackageCacheVersions(entries []dispatcherPackageCacheEntry) []string {
	versions := map[string]struct{}{}
	for _, entry := range entries {
		versions[entry.Version] = struct{}{}
	}
	result := make([]string, 0, len(versions))
	for version := range versions {
		result = append(result, version)
	}
	sort.Strings(result)
	return result
}

// dispatcherPackageCacheVersionForHash disambiguates multiple cached git package generations by
// matching packages-lock.json's resolved commit hash against each PackageCache directory's suffix.
// Why prefix match, not a fixed length: Unity truncates the hash to a directory suffix whose length
// can differ across Editor versions, so the suffix is compared as a leading prefix of the full hash
// rather than a hardcoded character count.
func dispatcherPackageCacheVersionForHash(entries []dispatcherPackageCacheEntry, hash string) (string, bool) {
	trimmedHash := strings.TrimSpace(hash)
	if trimmedHash == "" {
		return "", false
	}
	for _, entry := range entries {
		if entry.DirectorySuffix != "" && strings.HasPrefix(trimmedHash, entry.DirectorySuffix) {
			return entry.Version, true
		}
	}
	return "", false
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
	packageInfo, err := readDispatcherPackageInfo(packagePath)
	if err != nil {
		return "", err
	}
	return packageInfo.Version, nil
}

func readDispatcherPackageInfo(packagePath string) (dispatcherPackageJSON, error) {
	content, err := os.ReadFile(packagePath)
	if err != nil {
		return dispatcherPackageJSON{}, err
	}
	packageInfo := dispatcherPackageJSON{}
	if err := json.Unmarshal(content, &packageInfo); err != nil {
		return dispatcherPackageJSON{}, fmt.Errorf("parse %s: %w", packagePath, err)
	}
	return packageInfo, nil
}

func isDispatcherV2PackageVersion(version string) bool {
	trimmed := strings.TrimSpace(version)
	if !sharedversion.IsValid(trimmed) {
		return false
	}
	major, _, _ := strings.Cut(strings.TrimLeft(trimmed, "vV"), ".")
	return major == dispatcherV2MajorVersion
}
