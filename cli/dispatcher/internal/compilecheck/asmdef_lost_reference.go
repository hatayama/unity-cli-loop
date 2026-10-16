package compilecheck

// unityInjectedProjectReferences are the project references Unity adds to a response file without
// an .asmdef line: the two UI assemblies come from its auto-referenced package assemblies and the two
// test-runner assemblies from its test-runner rules.
var unityInjectedProjectReferences = map[string]bool{
	"UnityEngine.UI":         true,
	"UnityEditor.UI":         true,
	"UnityEngine.TestRunner": true,
	"UnityEditor.TestRunner": true,
}

// lostProjectReference names a project assembly the response file references that the .asmdef no
// longer declares and Unity does not add on its own.
// Why every declared reference is resolved before the response file is read: one reference that
// cannot be resolved leaves the declared set incomplete, and then a response-file reference outside
// it may be a lost one or merely the unresolved one. Such an .asmdef is not judged, which prefers a
// miss over refusing a healthy project, the same way missingProjectReference skips what it cannot
// resolve.
func lostProjectReference(
	contract assemblyDefinitionContract, rsp ResponseFile, dagDir string, context AssemblyContext,
) (string, bool) {
	declared := make(map[string]bool, len(contract.References))
	for _, reference := range contract.References {
		name, resolved := resolveReferenceName(reference, context)
		if !resolved {
			return "", false
		}
		declared[name] = true
	}

	for _, name := range rsp.ProjectAssemblyReferences(dagDir) {
		if declared[name] || unityInjectedProjectReferences[name] {
			continue
		}

		return name, true
	}

	return "", false
}
