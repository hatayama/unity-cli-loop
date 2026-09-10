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
	unityIgnoredNameSuffix       = "~"
	unityHiddenNamePrefix        = "."
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

// projectIndexRoots lists the directories a project keeps compilable C# under.
func projectIndexRoots(projectRoot string) []string {
	roots := []string{
		filepath.Join(projectRoot, assetsDirectoryName),
		filepath.Join(projectRoot, packagesDirectoryName),
		filepath.Join(projectRoot, libraryDirectoryName, packageCacheDirectoryName),
	}

	return append(roots, localPackageRoots(projectRoot)...)
}

// IndexAssemblyDefinitions maps every assembly name in the project to the .asmdef that declares it.
func IndexAssemblyDefinitions(projectRoot string) (map[string]AssemblyDefinition, error) {
	index := map[string]AssemblyDefinition{}
	for _, root := range projectIndexRoots(projectRoot) {
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
			if path != root && isUnityIgnoredName(entry.Name()) {
				return filepath.SkipDir
			}

			return nil
		}
		if isUnityIgnoredName(entry.Name()) {
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
func RebuildSources(
	projectRoot string, rsp ResponseFile, asmdef *AssemblyDefinition, owners assemblyOwnerIndex,
) ([]string, error) {
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

	globbed, err := globAssemblySources(projectRoot, asmdef.Directory, owners)
	if err != nil {
		return nil, err
	}

	if movedErr := detectMovedAssemblyDefinition(*asmdef, rsp, globbed); movedErr != nil {
		return nil, movedErr
	}

	return rescueRecordedSources(projectRoot, *asmdef, globbed, existing, owners)
}

// detectMovedAssemblyDefinition refuses a run whose .asmdef now sits over C# files the response file
// never recorded for it, which is what moving an .asmdef into another assembly's folder looks like
// from the outside. It reuses the glob the rebuild already did, and it is not gated on the .asmdef
// timestamp because moving a file with os.Rename carries its modification time along.
// A directory holding no source of its own says nothing either way: an assembly can legitimately own
// every source it compiles through .asmref folders elsewhere. Neither does a response file that
// recorded no source at all, which is not a build this check can compare against.
func detectMovedAssemblyDefinition(
	asmdef AssemblyDefinition, rsp ResponseFile, globbed []string,
) error {
	if len(globbed) == 0 || len(rsp.Sources) == 0 || recordsAnySource(rsp, globbed) {
		return nil
	}

	return unityBuildRequired(
		"assembly definition %s no longer owns any source the last build recorded, "+
			"so it moved after the last Unity build", asmdef.Name)
}

// rescueRecordedSources adds back the recorded sources the glob did not produce, which are the files
// an .asmref attaches to this assembly from a folder the glob never reaches - the folder is not
// always outside the assembly directory, since an .asmref nested inside a child assembly's directory
// sits under it yet stops the glob at the child's boundary.
// Why each one is checked against the .asmref that owns its folder rather than simply kept: removing
// or retargeting an .asmref leaves the .cs file where it is, so the response file still records it
// while the project has handed it to another assembly, and keeping it would compile the file into
// two assemblies at once. Sources deleted since the build are already gone from existing.
func rescueRecordedSources(
	projectRoot string, asmdef AssemblyDefinition, globbed []string, existing []string,
	owners assemblyOwnerIndex,
) ([]string, error) {
	result := globbed
	globbedSet := map[string]bool{}
	for _, source := range globbed {
		globbedSet[filepath.ToSlash(source)] = true
	}
	for _, source := range existing {
		if globbedSet[filepath.ToSlash(source)] {
			continue
		}
		if !owners.attachesTo(filepath.Dir(sourcePath(projectRoot, source)), asmdef.Name) {
			return nil, unityBuildRequired(
				"the last build recorded %s in %s, which no assembly reference attaches to it any more",
				filepath.Base(source), asmdef.Name)
		}
		result = append(result, source)
	}
	sort.Strings(result)

	return result, nil
}

// globAssemblySources lists the .cs files an assembly owns, stopping at nested assembly boundaries.
func globAssemblySources(
	projectRoot string, assemblyDirectory string, owners assemblyOwnerIndex,
) ([]string, error) {
	sources := []string{}
	err := filepath.WalkDir(assemblyDirectory, func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() {
			if path == assemblyDirectory {
				return nil
			}
			if isUnityIgnoredName(entry.Name()) || owners.startsAnotherAssembly(path) {
				return filepath.SkipDir
			}

			return nil
		}
		if isUnityIgnoredName(entry.Name()) {
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

// directoryHoldsAssemblyDefinition reports whether a directory declares an assembly of its own.
func directoryHoldsAssemblyDefinition(path string) bool {
	entries, err := os.ReadDir(path)
	if err != nil {
		return false
	}
	for _, entry := range entries {
		if entry.IsDir() || isUnityIgnoredName(entry.Name()) {
			continue
		}
		if filepath.Ext(entry.Name()) == assemblyDefinitionExtension {
			return true
		}
	}

	return false
}

// isUnityIgnoredName reports whether Unity excludes a file or directory from compilation by its
// name. Why files matter as much as directories: macOS writes AppleDouble siblings named "._X" next
// to files on non-native volumes, so a project can hold a "._X.asmdef" whose content is binary
// resource-fork metadata and a "._X.cs" that is not C# at all.
func isUnityIgnoredName(name string) bool {
	return strings.HasSuffix(name, unityIgnoredNameSuffix) ||
		strings.HasPrefix(name, unityHiddenNamePrefix)
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
