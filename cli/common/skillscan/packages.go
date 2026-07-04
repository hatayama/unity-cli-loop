package skillscan

import (
	"encoding/json"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

const (
	manifestFileName = "manifest.json"
	packageFileName  = "package.json"
	packageName      = "io.github.hatayama.uloopmcp"
	packageNameAlias = "io.github.hatayama.uLoopMCP"
)

type manifestData struct {
	Dependencies map[string]string `json:"dependencies"`
}

type packageData struct {
	Name string `json:"name"`
}

// PackageIdentity identifies a Unity package independently from where it was found.
type PackageIdentity struct {
	Name string
}

// PackageSearchResult is a package root candidate discovered from project, manifest, or cache state.
type PackageSearchResult struct {
	Identity PackageIdentity
	Root     string
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

func EnumeratePackageSearchResults(projectRoot string) []PackageSearchResult {
	results := []PackageSearchResult{}
	seen := map[string]bool{}
	addResult := func(result PackageSearchResult) {
		if result.Root == "" || result.Identity.Name == "" {
			return
		}
		absoluteRoot, err := filepath.Abs(result.Root)
		if err != nil {
			return
		}
		key := strings.ToLower(result.Identity.Name) + "\n" + absoluteRoot
		if seen[key] {
			return
		}
		seen[key] = true
		result.Root = absoluteRoot
		results = append(results, result)
	}

	for _, result := range enumerateDirectProjectPackageResults(projectRoot) {
		addResult(result)
	}
	for _, result := range enumerateManifestLocalPackageResults(projectRoot) {
		addResult(result)
	}
	for _, result := range enumeratePackageCacheResults(projectRoot) {
		addResult(result)
	}
	for _, result := range enumerateUnityCliLoopPackageCacheFallbackResults(projectRoot) {
		addResult(result)
	}
	return results
}

func FindUnityCliLoopPackage(projectRoot string) (PackageSearchResult, bool) {
	bestResult := PackageSearchResult{}
	bestPriority := 0
	found := false
	for _, result := range EnumeratePackageSearchResults(projectRoot) {
		if !isTargetPackageIdentity(result.Identity) {
			continue
		}
		resolvedRoot := resolvePackageRootCandidate(result.Root)
		if resolvedRoot == "" {
			continue
		}
		result.Root = resolvedRoot
		priority := unityCliLoopPackagePriority(projectRoot, resolvedRoot)
		if !found || priority < bestPriority || priority == bestPriority && result.Root < bestResult.Root {
			bestResult = result
			bestPriority = priority
			found = true
		}
	}
	return bestResult, found
}

func enumerateDirectProjectPackageResults(projectRoot string) []PackageSearchResult {
	packagesRoot := filepath.Join(projectRoot, "Packages")
	entries, err := os.ReadDir(packagesRoot)
	if err != nil {
		return []PackageSearchResult{}
	}
	results := []PackageSearchResult{}
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		root := resolveSkillSearchRootCandidate(filepath.Join(packagesRoot, entry.Name()))
		identity := resolvePackageIdentity(root, entry.Name())
		results = append(results, PackageSearchResult{Identity: identity, Root: root})
	}
	sortPackageSearchResults(results)
	return results
}

func enumerateManifestLocalPackageResults(projectRoot string) []PackageSearchResult {
	dependencies := readManifestDependencies(projectRoot)
	if len(dependencies) == 0 {
		return []PackageSearchResult{}
	}
	results := []PackageSearchResult{}
	for dependencyName, dependencyValue := range dependencies {
		localPath := resolveLocalDependencyPath(dependencyValue, projectRoot)
		if localPath == "" {
			continue
		}
		root := resolveSkillSearchRootCandidate(localPath)
		results = append(results, PackageSearchResult{
			Identity: PackageIdentity{Name: dependencyName},
			Root:     root,
		})
	}
	sortPackageSearchResults(results)
	return results
}

func enumeratePackageCacheResults(projectRoot string) []PackageSearchResult {
	dependencies := readManifestDependencies(projectRoot)
	if len(dependencies) == 0 {
		return []PackageSearchResult{}
	}
	dependencyNames := map[string]bool{}
	for dependencyName := range dependencies {
		dependencyNames[strings.ToLower(dependencyName)] = true
	}
	packageCacheDir := filepath.Join(projectRoot, "Library", "PackageCache")
	entries, err := os.ReadDir(packageCacheDir)
	if err != nil {
		return []PackageSearchResult{}
	}
	results := []PackageSearchResult{}
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
		root := resolveSkillSearchRootCandidate(filepath.Join(packageCacheDir, entry.Name()))
		results = append(results, PackageSearchResult{
			Identity: PackageIdentity{Name: dependencyName},
			Root:     root,
		})
	}
	sortPackageSearchResults(results)
	return results
}

func enumerateUnityCliLoopPackageCacheFallbackResults(projectRoot string) []PackageSearchResult {
	packageCacheDir := filepath.Join(projectRoot, "Library", "PackageCache")
	entries, err := os.ReadDir(packageCacheDir)
	if err != nil {
		return []PackageSearchResult{}
	}
	results := []PackageSearchResult{}
	for _, entry := range entries {
		if !entry.IsDir() || !isTargetPackageCacheDir(entry.Name()) {
			continue
		}
		root := resolveSkillSearchRootCandidate(filepath.Join(packageCacheDir, entry.Name()))
		results = append(results, PackageSearchResult{
			Identity: PackageIdentity{Name: packageIdentityNameFromCacheDir(entry.Name())},
			Root:     root,
		})
	}
	sortPackageSearchResults(results)
	return results
}

func ResolvePackageRoot(projectRoot string) string {
	result, ok := FindUnityCliLoopPackage(projectRoot)
	if !ok {
		return ""
	}
	return result.Root
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

func resolvePackageIdentity(packageRoot string, fallbackName string) PackageIdentity {
	content, err := os.ReadFile(filepath.Join(packageRoot, packageFileName))
	if err == nil {
		packageManifest := packageData{}
		if json.Unmarshal(content, &packageManifest) == nil && packageManifest.Name != "" {
			return PackageIdentity(packageManifest)
		}
	}
	if resolvePackageRootCandidate(packageRoot) != "" {
		return PackageIdentity{Name: packageName}
	}
	return PackageIdentity{Name: fallbackName}
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

func isTargetPackageIdentity(identity PackageIdentity) bool {
	normalizedName := strings.ToLower(identity.Name)
	return normalizedName == strings.ToLower(packageName) ||
		normalizedName == strings.ToLower(packageNameAlias)
}

func packageIdentityNameFromCacheDir(dirName string) string {
	if separatorIndex := strings.Index(dirName, "@"); separatorIndex >= 0 {
		return dirName[:separatorIndex]
	}
	return dirName
}

func sortPackageSearchResults(results []PackageSearchResult) {
	sort.Slice(results, func(left int, right int) bool {
		if results[left].Root == results[right].Root {
			return results[left].Identity.Name < results[right].Identity.Name
		}
		return results[left].Root < results[right].Root
	})
}

func unityCliLoopPackagePriority(projectRoot string, packageRoot string) int {
	priorityCandidates := []string{
		filepath.Join(projectRoot, "Packages", "src"),
		filepath.Join(projectRoot, "Packages", packageName),
		filepath.Join(projectRoot, "Packages", packageNameAlias),
	}
	normalizedRoot := normalizedAbsolutePath(packageRoot)
	for index, candidate := range priorityCandidates {
		if normalizedRoot == normalizedAbsolutePath(candidate) {
			return index
		}
	}
	if strings.Contains(normalizedRoot, string(filepath.Separator)+"Library"+string(filepath.Separator)+"PackageCache"+string(filepath.Separator)) {
		return 20
	}
	return 10
}

func normalizedAbsolutePath(path string) string {
	absolutePath, err := filepath.Abs(path)
	if err != nil {
		return filepath.Clean(path)
	}
	return filepath.Clean(absolutePath)
}
