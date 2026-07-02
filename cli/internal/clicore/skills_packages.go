package clicore

import (
	"encoding/json"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

const (
	manifestFileName = "manifest.json"
	packageName      = "io.github.hatayama.uloopmcp"
	packageNameAlias = "io.github.hatayama.uLoopMCP"
)

type manifestData struct {
	Dependencies map[string]string `json:"dependencies"`
}

func FindEditorFolders(basePath string, maxDepth int) []string {
	editorFolders := []string{}
	var scan func(string, int)
	scan = func(currentPath string, depth int) {
		if depth > maxDepth {
			return
		}
		entries, err := os.ReadDir(currentPath)
		if err != nil {
			return
		}
		for _, entry := range entries {
			if !entry.IsDir() || ExcludedSkillSearchDirs[entry.Name()] {
				continue
			}
			fullPath := filepath.Join(currentPath, entry.Name())
			if entry.Name() == "Editor" {
				editorFolders = append(editorFolders, fullPath)
				continue
			}
			scan(fullPath, depth+1)
		}
	}
	scan(basePath, 0)
	sort.Strings(editorFolders)
	return editorFolders
}

func enumerateDirectProjectPackageRoots(projectRoot string) []string {
	packagesRoot := filepath.Join(projectRoot, "Packages")
	entries, err := os.ReadDir(packagesRoot)
	if err != nil {
		return []string{}
	}
	packageRoots := []string{}
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		packageRoots = append(packageRoots, resolveSkillSearchRootCandidate(filepath.Join(packagesRoot, entry.Name())))
	}
	sort.Strings(packageRoots)
	return packageRoots
}

func resolveManifestLocalPackageRoots(projectRoot string) []string {
	return resolveManifestLocalPackageRootsMatching(projectRoot, nil)
}

func resolveTargetManifestLocalPackageRoots(projectRoot string) []string {
	return resolveManifestLocalPackageRootsMatching(projectRoot, isTargetManifestPackageName)
}

func resolveManifestLocalPackageRootsMatching(projectRoot string, includeDependency func(string) bool) []string {
	dependencies := readManifestDependencies(projectRoot)
	if len(dependencies) == 0 {
		return []string{}
	}
	packageRoots := []string{}
	for dependencyName, dependencyValue := range dependencies {
		if includeDependency != nil && !includeDependency(dependencyName) {
			continue
		}
		localPath := resolveLocalDependencyPath(dependencyValue, projectRoot)
		if localPath == "" {
			continue
		}
		packageRoots = append(packageRoots, resolveSkillSearchRootCandidate(localPath))
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
	packageCacheDir := filepath.Join(projectRoot, "Library", "PackageCache")
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
		packageRoots = append(packageRoots, resolveSkillSearchRootCandidate(filepath.Join(packageCacheDir, entry.Name())))
	}
	sort.Strings(packageRoots)
	return packageRoots
}

func ResolvePackageRoot(projectRoot string) string {
	candidates := []string{
		filepath.Join(projectRoot, "Packages", "src"),
		filepath.Join(projectRoot, "Packages", packageName),
		filepath.Join(projectRoot, "Packages", packageNameAlias),
	}
	for _, candidate := range candidates {
		if resolvedRoot := resolvePackageRootCandidate(candidate); resolvedRoot != "" {
			return resolvedRoot
		}
	}
	for _, candidate := range resolveTargetManifestLocalPackageRoots(projectRoot) {
		if resolvedRoot := resolvePackageRootCandidate(candidate); resolvedRoot != "" {
			return resolvedRoot
		}
	}

	return resolvePackageCacheRoot(projectRoot)
}

func resolvePackageCacheRoot(projectRoot string) string {
	packageCacheDir := filepath.Join(projectRoot, "Library", "PackageCache")
	entries, err := os.ReadDir(packageCacheDir)
	if err != nil {
		return ""
	}
	for _, entry := range entries {
		if !entry.IsDir() || !isTargetPackageCacheDir(entry.Name()) {
			continue
		}
		if resolvedRoot := resolvePackageRootCandidate(filepath.Join(packageCacheDir, entry.Name())); resolvedRoot != "" {
			return resolvedRoot
		}
	}
	return ""
}

func resolvePackageRootCandidate(candidate string) string {
	if _, err := os.Stat(candidate); err != nil {
		return ""
	}
	directToolsPath := filepath.Join(candidate, "Editor", "FirstPartyTools")
	if _, err := os.Stat(directToolsPath); err == nil {
		return candidate
	}
	nestedRoot := filepath.Join(candidate, "Packages", "src")
	nestedToolsPath := filepath.Join(nestedRoot, "Editor", "FirstPartyTools")
	if _, err := os.Stat(nestedToolsPath); err == nil {
		return nestedRoot
	}
	return ""
}

func resolveSkillSearchRootCandidate(candidate string) string {
	nestedRoot := filepath.Join(candidate, "Packages", "src")
	if _, err := os.Stat(nestedRoot); err == nil {
		return nestedRoot
	}
	return candidate
}

func readManifestDependencies(projectRoot string) map[string]string {
	manifestPath := filepath.Join(projectRoot, "Packages", manifestFileName)
	content, err := os.ReadFile(manifestPath)
	if err != nil {
		return map[string]string{}
	}
	manifest := manifestData{}
	if err := json.Unmarshal(content, &manifest); err != nil {
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
	rawPath = strings.TrimPrefix(rawPath, "//")
	if filepath.IsAbs(rawPath) {
		return rawPath
	}
	return filepath.Join(projectRoot, rawPath)
}

func isTargetPackageCacheDir(dirName string) bool {
	normalizedName := strings.ToLower(dirName)
	return strings.HasPrefix(normalizedName, strings.ToLower(packageName)+"@") ||
		strings.HasPrefix(normalizedName, strings.ToLower(packageNameAlias)+"@")
}

func isTargetManifestPackageName(dependencyName string) bool {
	normalizedName := strings.ToLower(dependencyName)
	return normalizedName == strings.ToLower(packageName) ||
		normalizedName == strings.ToLower(packageNameAlias)
}
