package compilecheck

import (
	"path/filepath"
	"strings"
)

const (
	packageManifestFileName  = "manifest.json"
	localPackagePathPrefix   = "file:"
	localPackagePathRelative = ".."
)

// localPackageRoots lists the directories of the packages this project develops from a local path.
// Why they matter: a `file:` dependency lives outside the project root, so its assembly definitions
// and sources are invisible to a walk of Assets, Packages and the package cache, and every assembly
// the package owns then looks like one Unity built and the project since deleted.
// A missing or unreadable manifest yields no roots rather than an error: the built-in roots still
// describe the project, and a manifest problem must not turn into a removed-assembly refusal.
func localPackageRoots(projectRoot string) []string {
	packagesDirectory := filepath.Join(projectRoot, packagesDirectoryName)

	var manifest struct {
		Dependencies map[string]string `json:"dependencies"`
	}
	if err := readUnityJSONFile(
		filepath.Join(packagesDirectory, packageManifestFileName), &manifest); err != nil {
		return nil
	}

	roots := []string{}
	for _, location := range manifest.Dependencies {
		root, isLocal := resolveLocalPackagePath(packagesDirectory, location)
		if !isLocal {
			continue
		}
		roots = append(roots, root)
	}

	return roots
}

// resolveLocalPackagePath turns one manifest dependency into the directory it points at, when the
// dependency is a local path at all. Unity resolves a relative path against the Packages folder.
func resolveLocalPackagePath(packagesDirectory string, location string) (string, bool) {
	if !strings.HasPrefix(location, localPackagePathPrefix) {
		return "", false
	}

	path := strings.TrimPrefix(location, localPackagePathPrefix)
	if path == "" {
		return "", false
	}
	if filepath.IsAbs(path) {
		return filepath.Clean(path), true
	}

	return filepath.Join(packagesDirectory, path), true
}

// sourcePath turns one response-file source entry into a path this process can open. Bee records a
// source inside the project relative to the project root and one outside it as an absolute path.
func sourcePath(projectRoot string, source string) string {
	if filepath.IsAbs(source) {
		return source
	}

	return filepath.Join(projectRoot, source)
}

// recordSourcePath spells one source the way Bee spells it, so a rebuilt list can be compared with
// the recorded one. A source outside the project root has no meaningful relative form, and using
// one would make every file of a locally developed package read as removed and added at once.
func recordSourcePath(projectRoot string, path string) string {
	relativePath, err := filepath.Rel(projectRoot, path)
	if err != nil || relativePath == localPackagePathRelative ||
		strings.HasPrefix(relativePath, localPackagePathRelative+string(filepath.Separator)) {
		return path
	}

	return filepath.ToSlash(relativePath)
}
