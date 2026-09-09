package compilecheck

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

const (
	editorPlatformName  = "Editor"
	guidReferencePrefix = "GUID:"
	unsafeFlagName      = "unsafe"
	unsafeOnFlagName    = "unsafe+"
)

// AssemblyContext is what the contract check needs to know about the rest of the project.
type AssemblyContext struct {
	BuiltAssemblies map[string]bool   // assembly names Bee wrote a response file for
	AssemblyByGUID  map[string]string // .asmdef GUID -> the assembly name it declares
}

// NewAssemblyContext collects the project-wide lookups the contract check compares against.
func NewAssemblyContext(
	graph assemblyGraph, assemblyDefinitions map[string]AssemblyDefinition,
) AssemblyContext {
	built := make(map[string]bool, len(graph.byName))
	for name := range graph.byName {
		built[name] = true
	}

	byGUID := make(map[string]string, len(assemblyDefinitions))
	for name, definition := range assemblyDefinitions {
		if definition.GUID != "" {
			byGUID[definition.GUID] = name
		}
	}

	return AssemblyContext{BuiltAssemblies: built, AssemblyByGUID: byGUID}
}

// assemblyDefinitionContract is the part of an .asmdef the response file has to agree with.
type assemblyDefinitionContract struct {
	References            []string `json:"references"`
	AllowUnsafeCode       bool     `json:"allowUnsafeCode"`
	OverrideReferences    bool     `json:"overrideReferences"`
	PrecompiledReferences []string `json:"precompiledReferences"`
	IncludePlatforms      []string `json:"includePlatforms"`
	ExcludePlatforms      []string `json:"excludePlatforms"`
}

// readAssemblyDefinitionContract reads the settings of one .asmdef that reach the compiler.
func readAssemblyDefinitionContract(path string) (assemblyDefinitionContract, error) {
	content, err := os.ReadFile(path)
	if err != nil {
		return assemblyDefinitionContract{}, fmt.Errorf("failed to read %s: %w", path, err)
	}

	contract := assemblyDefinitionContract{}
	if err := json.Unmarshal(content, &contract); err != nil {
		return assemblyDefinitionContract{}, fmt.Errorf("failed to read %s: %w", path, err)
	}

	return contract, nil
}

// buildsForEditor reports whether Unity still compiles this assembly for the Editor.
func (c assemblyDefinitionContract) buildsForEditor() bool {
	for _, platform := range c.ExcludePlatforms {
		if platform == editorPlatformName {
			return false
		}
	}
	if len(c.IncludePlatforms) == 0 {
		return true
	}
	for _, platform := range c.IncludePlatforms {
		if platform == editorPlatformName {
			return true
		}
	}

	return false
}

// detectContractDisagreement names the first .asmdef setting the response file can no longer serve.
// Why only these four: they are the settings that reach csc and are readable without evaluating
// defines or package versions, so a disagreement here is certain rather than guessed.
func detectContractDisagreement(
	contract assemblyDefinitionContract, rsp ResponseFile, dagDir string, context AssemblyContext,
) string {
	if !contract.buildsForEditor() {
		return "no longer builds for the Editor"
	}
	if reference, found := missingProjectReference(contract, rsp, dagDir, context); found {
		return fmt.Sprintf("now references %s, which the last build did not compile against", reference)
	}
	if contract.AllowUnsafeCode && !hasUnsafeFlag(rsp.OtherFlags) {
		return "now allows unsafe code"
	}
	if name, found := missingPrecompiledReference(contract, rsp); found {
		return fmt.Sprintf("now requires the precompiled reference %s", name)
	}

	return ""
}

// missingProjectReference names an assembly the .asmdef references that the response file lacks.
// Why the check is one-directional: Unity injects references of its own (the UI and test-runner
// assemblies) that no .asmdef declares, so demanding equality would reject healthy projects. Only a
// reference the .asmdef gained is detectable; a removed one is documented as undetectable instead.
func missingProjectReference(
	contract assemblyDefinitionContract, rsp ResponseFile, dagDir string, context AssemblyContext,
) (string, bool) {
	compiled := map[string]bool{}
	for _, name := range rsp.ProjectAssemblyReferences(dagDir) {
		compiled[name] = true
	}

	for _, reference := range contract.References {
		name, resolved := resolveReferenceName(reference, context)
		// Why an unresolvable reference is skipped: healthy projects carry GUIDs pointing at
		// assemblies excluded by platform or package settings, which own no .asmdef we can index.
		if !resolved || !context.BuiltAssemblies[name] || compiled[name] {
			continue
		}

		return name, true
	}

	return "", false
}

// resolveReferenceName turns one .asmdef reference, in GUID or name form, into an assembly name.
func resolveReferenceName(reference string, context AssemblyContext) (string, bool) {
	if !strings.HasPrefix(reference, guidReferencePrefix) {
		return reference, reference != ""
	}

	name, found := context.AssemblyByGUID[strings.TrimPrefix(reference, guidReferencePrefix)]

	return name, found
}

// missingPrecompiledReference names a listed .dll that the response file does not reference.
// Why only under overrideReferences: without it Unity references every precompiled assembly it
// finds, and the list in the .asmdef says nothing about what the compiler was given.
func missingPrecompiledReference(
	contract assemblyDefinitionContract, rsp ResponseFile,
) (string, bool) {
	if !contract.OverrideReferences {
		return "", false
	}

	referenced := map[string]bool{}
	for _, reference := range rsp.References {
		referenced[filepath.Base(reference)] = true
	}
	for _, name := range contract.PrecompiledReferences {
		if !referenced[name] {
			return name, true
		}
	}

	return "", false
}

// hasUnsafeFlag reports whether the response file lets the compiler accept unsafe code.
// Why several spellings: Bee writes /unsafe+, and csc accepts the - prefix and the bare form too.
func hasUnsafeFlag(flags []string) bool {
	for _, flag := range flags {
		name := strings.TrimPrefix(strings.TrimPrefix(flag, "/"), "-")
		if name == unsafeFlagName || name == unsafeOnFlagName {
			return true
		}
	}

	return false
}
