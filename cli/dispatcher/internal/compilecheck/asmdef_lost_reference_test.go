package compilecheck

import (
	"testing"
)

// contractDisagreementOf reads A's assembly definition from a contract project and returns what the
// contract check says about it against the given response file.
func contractDisagreementOf(
	t *testing.T, rsp ResponseFile, asmdef AssemblyDefinition, context AssemblyContext,
) string {
	t.Helper()
	contract, err := readAssemblyDefinitionContract(asmdef.Path)
	if err != nil {
		t.Fatalf("failed to read the assembly definition contract: %v", err)
	}

	return detectContractDisagreement(contract, rsp, planDagDirectory, context)
}

// Verifies the lost-reference check is skipped when a declared GUID resolves to no assembly
// definition, since the response-file reference may be that unresolved one rather than a lost one.
func TestDetectContractDisagreementSkipsTheLostReferenceCheckWhenAReferenceGUIDIsUnresolvable(t *testing.T) {
	_, rsp, asmdef, context := newContractProject(
		t, `{"name":"A","references":["GUID:00000000000000000000000000000000"]}`)

	if reason := contractDisagreementOf(t, rsp, asmdef, context); reason != "" {
		t.Fatalf("expected an unresolvable GUID to skip the lost-reference check, got %q", reason)
	}
}

// Verifies the lost-reference check is skipped when a declared name matches no assembly definition,
// such as an old render-pipeline name Unity rewrites to the one the response file carries.
func TestDetectContractDisagreementSkipsTheLostReferenceCheckWhenAReferenceNamesNoAssemblyDefinition(t *testing.T) {
	_, rsp, asmdef, context := newContractProject(
		t, `{"name":"A","references":["B","Unity.RenderPipelines.Lightweight.Runtime"]}`)
	rsp.References = append(rsp.References,
		planDagDirectory+"/Unity.RenderPipelines.Universal.Runtime.ref.dll")

	if reason := contractDisagreementOf(t, rsp, asmdef, context); reason != "" {
		t.Fatalf("expected a name no assembly definition declares to skip the lost-reference check, got %q", reason)
	}
}

// Verifies a response-file reference the assembly definition still declares, by name or by GUID, is
// not reported as lost.
func TestDetectContractDisagreementAcceptsAReferenceTheAssemblyDefinitionStillDeclares(t *testing.T) {
	cases := map[string]string{
		"by name": `{"name":"A","references":["B"]}`,
		"by GUID": `{"name":"A","references":["GUID:` + referencedGUID + `"]}`,
	}
	for name, body := range cases {
		t.Run(name, func(t *testing.T) {
			_, rsp, asmdef, context := newContractProject(t, body)

			if reason := contractDisagreementOf(t, rsp, asmdef, context); reason != "" {
				t.Fatalf("expected a still-declared reference to be accepted, got %q", reason)
			}
		})
	}
}

// Verifies each project reference Unity adds without an .asmdef line is not reported as lost.
func TestDetectContractDisagreementAcceptsTheReferencesUnityInjects(t *testing.T) {
	for _, injected := range []string{
		"UnityEngine.UI", "UnityEditor.UI", "UnityEngine.TestRunner", "UnityEditor.TestRunner",
	} {
		t.Run(injected, func(t *testing.T) {
			_, rsp, asmdef, context := newContractProject(t, `{"name":"A","references":["B"]}`)
			rsp.References = append(rsp.References, planDagDirectory+"/"+injected+".ref.dll")

			if reason := contractDisagreementOf(t, rsp, asmdef, context); reason != "" {
				t.Fatalf("expected the Unity-injected %s to be accepted, got %q", injected, reason)
			}
		})
	}
}

// Verifies a project reference the response file still carries but the assembly definition no
// longer declares is refused, naming the lost assembly.
func TestDetectContractDisagreementRefusesAReferenceTheAssemblyDefinitionLost(t *testing.T) {
	_, rsp, asmdef, context := newContractProject(t, `{"name":"A"}`)

	reason := contractDisagreementOf(t, rsp, asmdef, context)
	if reason != "no longer references B" {
		t.Fatalf("expected the lost reference B to be refused, got %q", reason)
	}
}

// Verifies a reference the assembly definition gained is reported before one it lost, so the
// existing order of the contract checks is kept.
func TestDetectContractDisagreementReportsAGainedReferenceBeforeALostOne(t *testing.T) {
	_, rsp, asmdef, context := newContractProject(t, `{"name":"A","references":["C"]}`)

	reason := contractDisagreementOf(t, rsp, asmdef, context)
	if reason != "now references C, which the last build did not compile against" {
		t.Fatalf("expected the gained reference C to be reported first, got %q", reason)
	}
}
