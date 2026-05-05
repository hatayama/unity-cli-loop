package dispatcher

import (
	"encoding/json"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

const (
	manifestFileName        = "manifest.json"
	packageCacheRelativeDir = "Library/PackageCache"
)

type manifestData struct {
	Dependencies map[string]string `json:"dependencies"`
}

func resolveManifestLocalPackageRoots(projectRoot string) []string {
	dependencies := readManifestDependencies(projectRoot)
	if len(dependencies) == 0 {
		return []string{}
	}
	packageRoots := []string{}
	for _, dependencyValue := range dependencies {
		localPath := resolveLocalDependencyPath(dependencyValue, projectRoot)
		if localPath == "" {
			continue
		}
		packageRoots = append(packageRoots, resolvePackageRootCandidate(localPath))
	}
	sort.Strings(packageRoots)
	return packageRoots
}

func resolveDependencyPackageCacheRoots(projectRoot string) []string {
	dependencies := readManifestDependencies(projectRoot)
	if len(dependencies) == 0 {
		return []string{}
	}
	dependencyNames := map[string]bool{}
	for dependencyName := range dependencies {
		dependencyNames[strings.ToLower(dependencyName)] = true
	}

	packageCacheDir := filepath.Join(projectRoot, packageCacheRelativeDir)
	entries, err := os.ReadDir(packageCacheDir)
	if err != nil {
		return []string{}
	}

	packageRoots := []string{}
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		dependencyName := entry.Name()
		if separatorIndex := strings.Index(dependencyName, "@"); separatorIndex >= 0 {
			dependencyName = dependencyName[:separatorIndex]
		}
		if !dependencyNames[strings.ToLower(dependencyName)] {
			continue
		}
		packageRoots = append(packageRoots, resolvePackageRootCandidate(filepath.Join(packageCacheDir, entry.Name())))
	}
	sort.Strings(packageRoots)
	return packageRoots
}

func readManifestDependencies(projectRoot string) map[string]string {
	content, err := os.ReadFile(filepath.Join(projectRoot, "Packages", manifestFileName))
	if err != nil {
		return map[string]string{}
	}
	manifest := manifestData{}
	if json.Unmarshal(content, &manifest) != nil {
		return map[string]string{}
	}
	if manifest.Dependencies == nil {
		return map[string]string{}
	}
	return manifest.Dependencies
}

func resolveLocalDependencyPath(dependencyValue string, projectRoot string) string {
	rawPath := ""
	switch {
	case strings.HasPrefix(dependencyValue, "file:"):
		rawPath = strings.TrimPrefix(dependencyValue, "file:")
	case strings.HasPrefix(dependencyValue, "path:"):
		rawPath = strings.TrimPrefix(dependencyValue, "path:")
	default:
		return ""
	}
	rawPath = strings.TrimSpace(rawPath)
	if rawPath == "" {
		return ""
	}
	if strings.HasPrefix(rawPath, "///") {
		rawPath = strings.TrimPrefix(rawPath, "//")
	}
	if filepath.IsAbs(rawPath) {
		return rawPath
	}
	return filepath.Join(projectRoot, rawPath)
}

func resolvePackageRootCandidate(candidate string) string {
	nestedRoot := filepath.Join(candidate, "Packages", "src")
	if _, err := os.Stat(nestedRoot); err == nil {
		return nestedRoot
	}
	return candidate
}
