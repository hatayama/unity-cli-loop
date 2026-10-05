package projectverify

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

const (
	manifestDisplayPath    = "Packages/manifest.json"
	packageManifestName    = "package.json"
	localPackagePathPrefix = "file:"
	// utf8ByteOrderMark is the prefix editors on Windows put in front of UTF-8 text.
	utf8ByteOrderMark = "\xEF\xBB\xBF"
)

// collectRoots lists the folders whose .meta files Unity manages: Assets, embedded packages, and
// local file: packages, without a folder that another root already covers.
func (v *verifier) collectRoots() ([]scanRoot, error) {
	roots := []scanRoot{{path: filepath.Join(v.projectRoot, assetsDirectoryName), display: assetsDirectoryName}}
	embedded, err := v.embeddedPackageRoots()
	if err != nil {
		return nil, err
	}
	local, err := v.localPackageRoots()
	if err != nil {
		return nil, err
	}
	return dropNestedRoots(append(append(roots, embedded...), local...))
}

// embeddedPackageRoots lists the package folders checked into Packages/, in name order.
func (v *verifier) embeddedPackageRoots() ([]scanRoot, error) {
	packagesDir := filepath.Join(v.projectRoot, packagesDirectoryName)
	entries, err := os.ReadDir(packagesDir)
	if errors.Is(err, fs.ErrNotExist) {
		return nil, nil
	}
	if err != nil {
		return nil, fmt.Errorf("read %s: %w", packagesDir, err)
	}

	roots := []scanRoot{}
	for _, entry := range entries {
		// IsDir is false for a symbolic link, so a linked package folder is not followed.
		if !entry.IsDir() || isUnityHiddenName(entry.Name(), true) {
			continue
		}
		packageJSONPath := filepath.Join(packagesDir, entry.Name(), packageManifestName)
		info, err := os.Lstat(packageJSONPath)
		if errors.Is(err, fs.ErrNotExist) {
			continue
		}
		if err != nil {
			return nil, fmt.Errorf("inspect %s: %w", packageJSONPath, err)
		}
		if !info.Mode().IsRegular() {
			continue
		}
		roots = append(roots, scanRoot{
			path:    filepath.Join(packagesDir, entry.Name()),
			display: packagesDirectoryName + "/" + entry.Name(),
		})
	}
	return roots, nil
}

// localPackageRoots lists the folders of the manifest's file: dependencies, in dependency name
// order, and reports the manifest problems it finds on the way.
func (v *verifier) localPackageRoots() ([]scanRoot, error) {
	manifestPath := filepath.Join(v.projectRoot, packagesDirectoryName, "manifest.json")
	content, err := os.ReadFile(manifestPath)
	if errors.Is(err, fs.ErrNotExist) {
		v.addManifestFinding(manifestMissingMessage())
		return nil, nil
	}
	if err != nil {
		return nil, fmt.Errorf("read %s: %w", manifestPath, err)
	}

	dependencies, ok := v.parseManifestDependencies(bytes.TrimPrefix(content, []byte(utf8ByteOrderMark)))
	if !ok {
		return nil, nil
	}
	names := make([]string, 0, len(dependencies))
	for name := range dependencies {
		names = append(names, name)
	}
	sort.Strings(names)

	roots := []scanRoot{}
	for _, name := range names {
		location, isString := stringValue(dependencies[name])
		if !isString {
			v.addManifestFinding(dependencyNotStringMessage(name))
			continue
		}
		// Registry and git dependencies live in Library/PackageCache, which this check does not read.
		if !strings.HasPrefix(location, localPackagePathPrefix) {
			continue
		}
		root, found, err := v.resolveLocalPackage(name, strings.TrimPrefix(location, localPackagePathPrefix))
		if err != nil {
			return nil, err
		}
		if found {
			roots = append(roots, root)
		}
	}
	return roots, nil
}

// parseManifestDependencies reports shape problems as findings and returns the dependencies
// object, or ok=false when there is nothing more to read.
func (v *verifier) parseManifestDependencies(content []byte) (map[string]json.RawMessage, bool) {
	if !json.Valid(content) {
		v.addManifestFinding(manifestNotJSONMessage())
		return nil, false
	}
	// A JSON null decodes into a nil map without an error, so nil is checked as well.
	var manifest map[string]json.RawMessage
	if json.Unmarshal(content, &manifest) != nil || manifest == nil {
		v.addManifestFinding(manifestNotObjectMessage())
		return nil, false
	}
	raw, ok := manifest["dependencies"]
	if !ok {
		return map[string]json.RawMessage{}, true
	}
	var dependencies map[string]json.RawMessage
	if json.Unmarshal(raw, &dependencies) != nil || dependencies == nil {
		v.addManifestFinding(dependenciesNotObjectMessage())
		return nil, false
	}
	return dependencies, true
}

// stringValue reports the JSON string in raw. Decoding straight into a Go string would turn
// null into "" without an error and hide a broken dependency.
func stringValue(raw json.RawMessage) (string, bool) {
	var value any
	if json.Unmarshal(raw, &value) != nil {
		return "", false
	}
	text, ok := value.(string)
	return text, ok
}

// resolveLocalPackage turns one file: location into a scan root, the way Unity resolves it:
// a relative path is relative to Packages/. A location Unity cannot load is reported, not returned.
func (v *verifier) resolveLocalPackage(name string, location string) (scanRoot, bool, error) {
	if location == "" {
		v.addManifestFinding(emptyLocalPackagePathMessage(name))
		return scanRoot{}, false, nil
	}
	path := location
	if !filepath.IsAbs(path) {
		path = filepath.Join(v.projectRoot, packagesDirectoryName, path)
	}
	path = filepath.Clean(path)
	if path == v.projectRoot || isWithin(v.projectRoot, path) {
		v.addManifestFinding(localPackageContainsProjectMessage(name, location))
		return scanRoot{}, false, nil
	}

	info, err := os.Stat(path)
	if errors.Is(err, fs.ErrNotExist) {
		v.addManifestFinding(localPackageMissingMessage(name, location))
		return scanRoot{}, false, nil
	}
	if err != nil {
		return scanRoot{}, false, fmt.Errorf("inspect local package %s at %s: %w", name, path, err)
	}
	// A file: path to a file is a package tarball, which has no folder to scan until Unity unpacks it.
	if !info.IsDir() {
		return scanRoot{}, false, nil
	}

	packageJSONPath := filepath.Join(path, packageManifestName)
	packageInfo, err := os.Stat(packageJSONPath)
	if err != nil && !errors.Is(err, fs.ErrNotExist) {
		return scanRoot{}, false, fmt.Errorf("inspect local package %s at %s: %w", name, packageJSONPath, err)
	}
	if err != nil || !packageInfo.Mode().IsRegular() {
		v.addManifestFinding(localPackageWithoutPackageJSONMessage(name, location))
		return scanRoot{}, false, nil
	}
	return scanRoot{path: path, display: v.displayPath(path)}, true, nil
}

// dropNestedRoots keeps each folder once: scanning a folder twice would count its .meta files
// twice and report every GUID in it as a duplicate. The outer folder wins, but only where its scan
// actually walks down to the inner one; a root below a hidden, plugin, or linked folder is never
// reached that way and stays a root of its own.
func dropNestedRoots(roots []scanRoot) ([]scanRoot, error) {
	byLength := append([]scanRoot{}, roots...)
	sort.SliceStable(byLength, func(i, j int) bool { return len(byLength[i].path) < len(byLength[j].path) })
	keptPaths := []string{}
	kept := map[string]bool{}
	for _, root := range byLength {
		covered, err := isCoveredByAny(root.path, keptPaths)
		if err != nil {
			return nil, err
		}
		if !covered {
			keptPaths = append(keptPaths, root.path)
			kept[root.path] = true
		}
	}

	result := []scanRoot{}
	seen := map[string]bool{}
	for _, root := range roots {
		if kept[root.path] && !seen[root.path] {
			result = append(result, root)
			seen[root.path] = true
		}
	}
	return result, nil
}

// isCoveredByAny reports whether path is one of the kept folders, or sits inside one whose scan
// reaches it.
func isCoveredByAny(path string, keptPaths []string) (bool, error) {
	for _, keptPath := range keptPaths {
		if path == keptPath {
			return true, nil
		}
		rel, below := relativeBelow(path, keptPath)
		if !below {
			continue
		}
		reached, err := scanReaches(keptPath, rel)
		if err != nil || reached {
			return reached, err
		}
	}
	return false, nil
}

// scanReaches reports whether scanning the root folder walks down to rel below it: every folder on
// the way, the last one included, must be one the scan descends into.
func scanReaches(root string, rel string) (bool, error) {
	current := root
	for _, name := range strings.Split(rel, string(filepath.Separator)) {
		current = filepath.Join(current, name)
		// Lstat, so that a linked folder on the way counts as the link the scan stops at.
		info, err := os.Lstat(current)
		if err != nil {
			return false, fmt.Errorf("inspect %s: %w", current, err)
		}
		if !scanDescendsInto(name, info.Mode()) {
			return false, nil
		}
	}
	return true, nil
}

// isWithin reports whether child sits below parent; the same folder is not within itself.
func isWithin(child string, parent string) bool {
	_, below := relativeBelow(child, parent)
	return below
}

// relativeBelow returns child's path relative to parent when child sits below parent.
func relativeBelow(child string, parent string) (string, bool) {
	rel, err := filepath.Rel(parent, child)
	if err != nil || rel == "." || rel == ".." || strings.HasPrefix(rel, ".."+string(filepath.Separator)) {
		return "", false
	}
	return rel, true
}

// displayPath names a folder the way findings do: relative to the project when inside it,
// absolute otherwise, with / separators either way.
func (v *verifier) displayPath(path string) string {
	if isWithin(path, v.projectRoot) {
		rel, _ := filepath.Rel(v.projectRoot, path)
		return filepath.ToSlash(rel)
	}
	return filepath.ToSlash(path)
}

// isUnityHiddenName reports whether Unity skips an entry by its name, per the manual's
// "Hidden assets": such entries are not imported and need no .meta file.
func isUnityHiddenName(name string, isDir bool) bool {
	if strings.HasPrefix(name, ".") || strings.HasSuffix(name, "~") || strings.EqualFold(name, "cvs") {
		return true
	}
	return !isDir && strings.EqualFold(filepath.Ext(name), ".tmp")
}

func (v *verifier) addManifestFinding(message string) {
	v.addFinding(CheckManifestInvalid, manifestDisplayPath, 0, message)
}

func manifestMissingMessage() string {
	return "Packages/manifest.json does not exist. Unity will recreate it with the default packages only, " +
		"dropping the packages this project uses. Restore it from version control."
}

func manifestNotJSONMessage() string {
	return "Packages/manifest.json is not valid JSON. Unity cannot resolve packages until it is fixed."
}

func manifestNotObjectMessage() string {
	return "Packages/manifest.json must contain a JSON object."
}

func dependenciesNotObjectMessage() string {
	return `"dependencies" in Packages/manifest.json must be a JSON object.`
}

func dependencyNotStringMessage(name string) string {
	return fmt.Sprintf("Dependency %s in Packages/manifest.json must have a string value.", name)
}

func emptyLocalPackagePathMessage(name string) string {
	return fmt.Sprintf("Dependency %s in Packages/manifest.json has an empty file: path.", name)
}

func localPackageMissingMessage(name string, location string) string {
	return fmt.Sprintf("Dependency %s in Packages/manifest.json points to %s, which does not exist. "+
		"Unity cannot resolve the package, so the project opens with errors. "+
		"Fix the path or remove the dependency.", name, location)
}

func localPackageWithoutPackageJSONMessage(name string, location string) string {
	return fmt.Sprintf("Dependency %s in Packages/manifest.json points to %s, which has no package.json. "+
		"Unity cannot load it as a package.", name, location)
}

func localPackageContainsProjectMessage(name string, location string) string {
	return fmt.Sprintf("Dependency %s in Packages/manifest.json points to %s, which is this project or contains it. "+
		"Point it at the package folder instead.", name, location)
}
