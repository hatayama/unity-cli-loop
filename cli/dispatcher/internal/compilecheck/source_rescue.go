package compilecheck

import (
	"path/filepath"
)

// directoryOwner is the .asmdef or .asmref that decides which assembly a directory's C# files
// belong to.
type directoryOwner struct {
	AssemblyName string // the assembly the owner names; empty when the .asmref cannot be resolved
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
		// An unresolvable reference is kept as an empty name rather than dropped: the directory is
		// still owned by an .asmref, and only the assembly it names is unknown.
		name, _ := resolveReferenceName(reference.Reference, context)
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

	return owner.AssemblyName == "" || owner.AssemblyName == assemblyName
}
