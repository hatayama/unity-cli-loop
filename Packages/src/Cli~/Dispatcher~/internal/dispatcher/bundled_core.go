package dispatcher

import (
	"encoding/json"
	"os"
	"path/filepath"
	"runtime"
)

const (
	packageName             = "io.github.hatayama.uloopmcp"
	packageNameAlias        = "io.github.hatayama.uLoopMCP"
	packageManifestFileName = "package.json"
	cliDirectoryName        = "Cli~"
	coreDirectoryName       = "Core~"
	distDirectoryName       = "dist"
)

type packageManifest struct {
	Name string `json:"name"`
}

func findBundledCorePath(projectRoot string) (string, error) {
	for _, packageRoot := range bundledPackageRoots(projectRoot) {
		corePath := filepath.Join(packageRoot, cliDirectoryName, coreDirectoryName, distDirectoryName, runtime.GOOS+"-"+runtime.GOARCH, coreBinaryName())
		_, err := os.Stat(corePath)
		if err == nil {
			return corePath, nil
		}
		if !os.IsNotExist(err) {
			return "", err
		}
	}
	return "", nil
}

func bundledPackageRoots(projectRoot string) []string {
	candidates := []string{
		filepath.Join(projectRoot, "Packages", "src"),
		filepath.Join(projectRoot, "Packages", packageName),
		filepath.Join(projectRoot, "Packages", packageNameAlias),
	}
	candidates = append(candidates, childDirectories(filepath.Join(projectRoot, "Packages"))...)
	candidates = append(candidates, resolveManifestLocalPackageRoots(projectRoot)...)
	candidates = append(candidates, resolveDependencyPackageCacheRoots(projectRoot)...)
	candidates = append(candidates, childDirectories(filepath.Join(projectRoot, "Library", "PackageCache"))...)

	roots := []string{}
	seen := map[string]bool{}
	for _, candidate := range candidates {
		if seen[candidate] || !isULoopPackageRoot(candidate) {
			continue
		}
		seen[candidate] = true
		roots = append(roots, candidate)
	}
	return roots
}

func isULoopPackageRoot(candidate string) bool {
	content, err := os.ReadFile(filepath.Join(candidate, packageManifestFileName))
	if err != nil {
		return false
	}
	var manifest packageManifest
	if json.Unmarshal(content, &manifest) != nil {
		return false
	}
	return manifest.Name == packageName || manifest.Name == packageNameAlias
}

func coreBinaryName() string {
	if runtime.GOOS == "windows" {
		return "uloop-core.exe"
	}
	return "uloop-core"
}
