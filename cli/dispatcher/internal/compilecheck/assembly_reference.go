package compilecheck

import (
	"fmt"
	"io/fs"
	"path/filepath"
	"sort"
)

const assemblyReferenceIndexError = "failed to index assembly references under %s: %w"

// AssemblyReference is one .asmref file: it attaches the C# files of its folder to the assembly it
// names, wherever that folder sits in the project.
type AssemblyReference struct {
	Path      string // absolute path of the .asmref file
	Directory string // absolute path of the folder whose sources it attaches
	Reference string // the assembly it names, in GUID or plain name form
}

// IndexAssemblyReferences lists every .asmref in the project, ordered by path.
func IndexAssemblyReferences(projectRoot string) ([]AssemblyReference, error) {
	references := []AssemblyReference{}
	for _, root := range projectIndexRoots(projectRoot) {
		if !directoryExists(root) {
			continue
		}
		found, err := indexAssemblyReferencesUnder(root)
		if err != nil {
			return nil, fmt.Errorf(assemblyReferenceIndexError, root, err)
		}
		references = append(references, found...)
	}
	sort.Slice(references, func(first int, second int) bool {
		return references[first].Path < references[second].Path
	})

	return references, nil
}

// indexAssemblyReferencesUnder collects every .asmref found below one project root.
func indexAssemblyReferencesUnder(root string) ([]AssemblyReference, error) {
	references := []AssemblyReference{}
	err := filepath.WalkDir(root, func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() {
			if path != root && isUnityIgnoredName(entry.Name()) {
				return filepath.SkipDir
			}

			return nil
		}
		if isUnityIgnoredName(entry.Name()) || filepath.Ext(path) != assemblyReferenceExtension {
			return nil
		}
		reference, readErr := readAssemblyReference(path)
		if readErr != nil {
			return readErr
		}
		references = append(references, reference)

		return nil
	})
	if err != nil {
		return nil, err
	}

	return references, nil
}

// readAssemblyReference reads the assembly one .asmref names.
func readAssemblyReference(path string) (AssemblyReference, error) {
	var content struct {
		Reference string `json:"reference"`
	}
	if err := readUnityJSONFile(path, &content); err != nil {
		return AssemblyReference{}, fmt.Errorf("failed to read %s: %w", path, err)
	}

	return AssemblyReference{
		Path:      path,
		Directory: filepath.Dir(path),
		Reference: content.Reference,
	}, nil
}

// DetectAssemblyReferenceChange refuses a run whose .asmref folders no longer match the last build.
// Why it cannot be replayed: an .asmref moves the sources of its folder from one assembly to
// another without touching a single .cs file or .asmdef, so the response files would compile both
// assemblies with the membership the project no longer has and report diagnostics for neither.
func DetectAssemblyReferenceChange(
	projectRoot string, dagDir string, graph assemblyGraph, context AssemblyContext,
) error {
	references, err := IndexAssemblyReferences(projectRoot)
	if err != nil {
		return err
	}

	for _, reference := range references {
		if changeErr := detectOneAssemblyReferenceChange(
			projectRoot, dagDir, graph, context, reference); changeErr != nil {
			return changeErr
		}
	}

	return nil
}

// detectOneAssemblyReferenceChange compares one .asmref against the response file of the assembly
// it names, once its timestamp says it may have appeared or moved.
// Why the timestamp is only a trigger: switching branches rewrites every .asmref without changing
// what it attaches, and the timestamp alone would refuse the project forever.
func detectOneAssemblyReferenceChange(
	projectRoot string, dagDir string, graph assemblyGraph, context AssemblyContext,
	reference AssemblyReference,
) error {
	name, resolved := resolveReferenceName(reference.Reference, context)
	if !resolved {
		return nil
	}
	// Why an assembly with no response file is skipped: an .asmref can name an assembly excluded by
	// platform or package settings, which the last build legitimately never compiled.
	rsp, built := graph.byName[name]
	if !built {
		return nil
	}

	baseline, err := lastBuildTime(projectRoot, rsp, dagDir)
	if err != nil {
		return err
	}
	written, err := modificationTime(reference.Path)
	if err != nil {
		return err
	}
	if !written.After(baseline) {
		return nil
	}

	attached, globErr := globAssemblySources(projectRoot, reference.Directory)
	if globErr != nil {
		return globErr
	}
	if len(attached) == 0 || recordsAnySource(rsp, attached) {
		return nil
	}

	return unityBuildRequired(
		"assembly reference %s attaches sources the last build did not compile into %s",
		filepath.Base(reference.Path), name)
}

// recordsAnySource reports whether a response file lists at least one of the given sources.
func recordsAnySource(rsp ResponseFile, sources []string) bool {
	recorded := make(map[string]bool, len(rsp.Sources))
	for _, source := range rsp.Sources {
		recorded[filepath.ToSlash(source)] = true
	}
	for _, source := range sources {
		if recorded[filepath.ToSlash(source)] {
			return true
		}
	}

	return false
}
