package compilecheck

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

const (
	assetsDirectoryName          = "Assets"
	packagesDirectoryName        = "Packages"
	libraryDirectoryName         = "Library"
	packageCacheDirectoryName    = "PackageCache"
	assemblyDefinitionExtension  = ".asmdef"
	assemblyDefinitionMetaSuffix = ".meta"
	assemblyDefinitionGUIDKey    = "guid:"
	assemblyReferenceExtension   = ".asmref"
	cSharpSourceExtension        = ".cs"
	unityIgnoredDirectorySuffix  = "~"
	unityHiddenDirectoryPrefix   = "."
	assemblyDefinitionIndexError = "failed to index assembly definitions under %s: %w"
)

// utf8ByteOrderMark is the prefix editors on Windows put in front of UTF-8 text.
var utf8ByteOrderMark = []byte{0xEF, 0xBB, 0xBF}

// AssemblyDefinition is one .asmdef file, keyed by the assembly name it declares.
type AssemblyDefinition struct {
	Name      string
	Path      string // absolute path of the .asmdef file
	Directory string // absolute path of the directory that owns the assembly
	GUID      string // the GUID in the sibling .meta, empty when Unity has not written one yet
}

// IndexAssemblyDefinitions maps every assembly name in the project to the .asmdef that declares it.
func IndexAssemblyDefinitions(projectRoot string) (map[string]AssemblyDefinition, error) {
	index := map[string]AssemblyDefinition{}
	roots := []string{
		filepath.Join(projectRoot, assetsDirectoryName),
		filepath.Join(projectRoot, packagesDirectoryName),
		filepath.Join(projectRoot, libraryDirectoryName, packageCacheDirectoryName),
	}
	roots = append(roots, localPackageRoots(projectRoot)...)
	for _, root := range roots {
		if !directoryExists(root) {
			continue
		}
		if err := indexAssemblyDefinitionsUnder(root, index); err != nil {
			return nil, fmt.Errorf(assemblyDefinitionIndexError, root, err)
		}
	}

	return index, nil
}

// indexAssemblyDefinitionsUnder adds every .asmdef found below one project root to the index.
func indexAssemblyDefinitionsUnder(root string, index map[string]AssemblyDefinition) error {
	return filepath.WalkDir(root, func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() {
			if path != root && isUnityIgnoredDirectory(entry.Name()) {
				return filepath.SkipDir
			}

			return nil
		}
		if filepath.Ext(path) != assemblyDefinitionExtension {
			return nil
		}
		name, readErr := readAssemblyDefinitionName(path)
		if readErr != nil {
			return readErr
		}
		// Why the first wins: Unity itself rejects duplicate assembly names, so a second one means a
		// stale copy, and taking it would point the rebuild at the wrong directory.
		if _, exists := index[name]; !exists {
			index[name] = AssemblyDefinition{
				Name:      name,
				Path:      path,
				Directory: filepath.Dir(path),
				GUID:      readAssemblyDefinitionGUID(path),
			}
		}

		return nil
	})
}

// readUnityJSONFile reads one of Unity's JSON files into target.
// Why the byte order mark is stripped: editors on Windows write UTF-8 with a BOM by default, and
// encoding/json rejects it, which would turn one such .asmdef into a failure of the whole command.
func readUnityJSONFile(path string, target any) error {
	content, err := os.ReadFile(path)
	if err != nil {
		return err
	}

	return json.Unmarshal(bytes.TrimPrefix(content, utf8ByteOrderMark), target)
}

// readAssemblyDefinitionName reads the assembly name an .asmdef declares.
func readAssemblyDefinitionName(path string) (string, error) {
	var definition struct {
		Name string `json:"name"`
	}
	if err := readUnityJSONFile(path, &definition); err != nil {
		return "", fmt.Errorf("failed to read %s: %w", path, err)
	}
	if definition.Name == "" {
		return "", fmt.Errorf("%s declares no assembly name", path)
	}

	return definition.Name, nil
}

// readAssemblyDefinitionGUID reads the GUID Unity assigned an .asmdef, which is how other assembly
// definitions spell a reference to it. An unreadable .meta yields an empty GUID rather than an
// error: the GUID only refines a check that already tolerates references it cannot resolve.
func readAssemblyDefinitionGUID(assemblyDefinitionPath string) string {
	content, err := os.ReadFile(assemblyDefinitionPath + assemblyDefinitionMetaSuffix)
	if err != nil {
		return ""
	}

	for _, rawLine := range strings.Split(string(content), "\n") {
		line := strings.TrimSpace(rawLine)
		if !strings.HasPrefix(line, assemblyDefinitionGUIDKey) {
			continue
		}

		return strings.TrimSpace(strings.TrimPrefix(line, assemblyDefinitionGUIDKey))
	}

	return ""
}

// RebuildSources lists the sources to compile now, reflecting .cs files added or deleted since Bee ran.
func RebuildSources(projectRoot string, rsp ResponseFile, asmdef *AssemblyDefinition) ([]string, error) {
	existing := make([]string, 0, len(rsp.Sources))
	for _, source := range rsp.Sources {
		if fileExists(sourcePath(projectRoot, source)) {
			existing = append(existing, source)
		}
	}

	// Why package-cache assemblies are not re-globbed: their contents are restored from the package
	// manager and never edited in place, so the walk would only cost time.
	if asmdef == nil || isUnderPackageCache(projectRoot, asmdef.Directory) {
		sort.Strings(existing)

		return existing, nil
	}

	globbed, err := globAssemblySources(projectRoot, asmdef.Directory)
	if err != nil {
		return nil, err
	}

	// Why sources outside the directory survive: an .asmref pulls files from elsewhere into this
	// assembly, and only the response file records where they came from.
	result := globbed
	for _, source := range existing {
		if !isUnderDirectory(sourcePath(projectRoot, source), asmdef.Directory) {
			result = append(result, source)
		}
	}
	sort.Strings(result)

	return result, nil
}

// globAssemblySources lists the .cs files an assembly owns, stopping at nested assembly boundaries.
func globAssemblySources(projectRoot string, assemblyDirectory string) ([]string, error) {
	sources := []string{}
	err := filepath.WalkDir(assemblyDirectory, func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() {
			if path == assemblyDirectory {
				return nil
			}
			if isUnityIgnoredDirectory(entry.Name()) || directoryOwnsOwnAssembly(path) {
				return filepath.SkipDir
			}

			return nil
		}
		if filepath.Ext(path) != cSharpSourceExtension {
			return nil
		}
		sources = append(sources, recordSourcePath(projectRoot, path))

		return nil
	})
	if err != nil {
		return nil, fmt.Errorf("failed to list sources under %s: %w", assemblyDirectory, err)
	}

	return sources, nil
}

// directoryOwnsOwnAssembly reports whether a directory starts a different assembly.
func directoryOwnsOwnAssembly(path string) bool {
	entries, err := os.ReadDir(path)
	if err != nil {
		return false
	}
	for _, entry := range entries {
		if entry.IsDir() {
			continue
		}
		extension := filepath.Ext(entry.Name())
		if extension == assemblyDefinitionExtension || extension == assemblyReferenceExtension {
			return true
		}
	}

	return false
}

// isUnityIgnoredDirectory reports whether Unity excludes a directory from compilation by its name.
func isUnityIgnoredDirectory(name string) bool {
	return strings.HasSuffix(name, unityIgnoredDirectorySuffix) ||
		strings.HasPrefix(name, unityHiddenDirectoryPrefix)
}

// isUnderPackageCache reports whether a directory belongs to the read-only package cache.
func isUnderPackageCache(projectRoot string, directory string) bool {
	return isUnderDirectory(
		directory, filepath.Join(projectRoot, libraryDirectoryName, packageCacheDirectoryName))
}

// isUnderDirectory reports whether a path sits inside a directory.
func isUnderDirectory(path string, directory string) bool {
	relativePath, err := filepath.Rel(directory, path)
	if err != nil {
		return false
	}

	return relativePath != ".." && !strings.HasPrefix(relativePath, ".."+string(filepath.Separator))
}
