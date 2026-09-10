package compilecheck

import (
	"path/filepath"
)

// directoryOwner is the .asmdef or .asmref that decides which assembly a directory's C# files
// belong to.
type directoryOwner struct {
	AssemblyName string // the assembly the owner names; empty when an .asmdef owns the directory
	IsReference  bool   // an .asmref owns the directory, rather than an .asmdef
}

// assemblyOwnerIndex answers which assembly owns a directory's C# files. It is built once per run
// from the .asmdef and .asmref files of the project, because the answer is needed for every
// recorded source the re-glob no longer produces and walking the project per assembly would repeat
// the same walk for each of them.
type assemblyOwnerIndex struct {
	definitionDirectories map[string]bool
	referencesByDirectory map[string]string
}

// newAssemblyOwnerIndex maps every directory that starts an assembly to what it starts.
// Why an .asmref naming an assembly this build never produced is left out: Unity has nothing to
// attach the folder to, so it compiles the folder's sources into the enclosing assembly instead.
// A package shipping a sample .asmref for an optional render pipeline is the ordinary case - the
// pipeline is not installed, the GUID resolves to nothing, and the sample compiles into the package's
// own editor assembly.
func newAssemblyOwnerIndex(
	assemblyDefinitions map[string]AssemblyDefinition, references []AssemblyReference,
	context AssemblyContext,
) assemblyOwnerIndex {
	definitionDirectories := make(map[string]bool, len(assemblyDefinitions))
	for _, definition := range assemblyDefinitions {
		definitionDirectories[filepath.Clean(definition.Directory)] = true
	}

	referencesByDirectory := make(map[string]string, len(references))
	for _, reference := range references {
		name, resolved := resolveReferenceName(reference.Reference, context)
		if !resolved || !context.BuiltAssemblies[name] {
			continue
		}
		referencesByDirectory[filepath.Clean(reference.Directory)] = name
	}

	return assemblyOwnerIndex{
		definitionDirectories: definitionDirectories,
		referencesByDirectory: referencesByDirectory,
	}
}

// ownerOf walks up from a directory to the first one holding an .asmref or an .asmdef, which is the
// file that decides where the directory's sources are compiled. The second result is false when the
// walk leaves the project without meeting either, which means no assembly definition owns the
// directory at all and its sources belong to the predefined assemblies.
func (index assemblyOwnerIndex) ownerOf(directory string) (directoryOwner, bool) {
	current := filepath.Clean(directory)
	for {
		if name, isReference := index.referencesByDirectory[current]; isReference {
			return directoryOwner{AssemblyName: name, IsReference: true}, true
		}
		if index.definitionDirectories[current] {
			return directoryOwner{}, true
		}
		parent := filepath.Dir(current)
		if parent == current {
			return directoryOwner{}, false
		}
		current = parent
	}
}

// attachesTo reports whether an .asmref still hands the directory's sources to this assembly.
// Why an .asmdef owner never qualifies: the re-glob already produced every source the assembly's own
// directory holds, so a recorded source the glob missed can only belong here through an .asmref.
func (index assemblyOwnerIndex) attachesTo(directory string, assemblyName string) bool {
	owner, found := index.ownerOf(directory)
	if !found || !owner.IsReference {
		return false
	}

	return owner.AssemblyName == assemblyName
}

// startsAnotherAssembly reports whether a directory hands its sources to an assembly of its own, and
// so ends the source glob of the assembly above it. It is the same question ownerOf answers, asked
// of one directory rather than of a whole branch, so the glob and the rescue agree on where an
// assembly ends - a disagreement between them would compile a file into two assemblies or into none.
func (index assemblyOwnerIndex) startsAnotherAssembly(directory string) bool {
	current := filepath.Clean(directory)
	if _, isReference := index.referencesByDirectory[current]; isReference {
		return true
	}

	// Why the .asmdef is still looked for on disk rather than in the index: the index keeps one
	// directory per assembly name, so a stale duplicate .asmdef is absent from it while Unity still
	// stops there.
	return directoryHoldsAssemblyDefinition(current)
}
