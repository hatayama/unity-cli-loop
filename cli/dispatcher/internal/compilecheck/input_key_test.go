package compilecheck

import (
	"os"
	"path/filepath"
	"testing"
	"time"
)

// inputKeyProject is a project tree holding every kind of input one unit's key is built from, so a
// test can move exactly one of them and watch what the key does.
type inputKeyProject struct {
	root  string
	paths EditorCompilerPaths
	plan  BuildPlan
}

// newInputKeyProject writes the files of a two-assembly project: Alpha, whose key the tests read,
// and Beta, which Alpha references and this run compiles. Gamma is referenced but not compiled, so
// it stands for an assembly the last Unity build left behind.
func newInputKeyProject(t *testing.T) *inputKeyProject {
	t.Helper()
	root := t.TempDir()
	outputDir := filepath.Join("Library", "uloop", "compile-check", "aaaa.dag")

	writeProjectFile(t, root, filepath.Join("Editor", "csc.dll"), "compiler")
	writeProjectFile(t, root, filepath.Join("Editor", "dotnet"), "host")
	writeProjectFile(t, root, filepath.Join(planDagDirectory, "Alpha.rsp"), "-out:\"Alpha.dll\"\n")
	writeProjectFile(t, root, filepath.Join("Assets", "Alpha", "Alpha.cs"), "class Alpha {}")
	writeProjectFile(t, root, filepath.Join("Assets", "Analyzers", "Banned.txt"), "T:System.DateTime")
	writeProjectFile(t, root, filepath.Join("Packages", "Analyzer.dll"), "analyzer")
	writeProjectFile(t, root, filepath.Join(planDagDirectory, "Gamma.ref.dll"), "gamma surface")
	writeProjectFile(t, root, filepath.Join(outputDir, "Beta.ref.dll"), "beta surface")

	alpha := ResponseFile{
		AssemblyName:  "Alpha",
		Path:          filepath.Join(root, planDagDirectory, "Alpha.rsp"),
		OutputPath:    filepath.Join(planDagDirectory, "Alpha.dll"),
		RefOutputPath: filepath.Join(planDagDirectory, "Alpha"+referenceAssemblyExtension),
		References: []string{
			filepath.Join(planDagDirectory, "Beta"+referenceAssemblyExtension),
			filepath.Join(planDagDirectory, "Gamma"+referenceAssemblyExtension),
		},
		Analyzers:      []string{filepath.Join("Packages", "Analyzer.dll")},
		AdditionalFile: filepath.Join("Assets", "Analyzers", "Banned.txt"),
	}
	beta := ResponseFile{
		AssemblyName:  "Beta",
		Path:          filepath.Join(root, planDagDirectory, "Beta.rsp"),
		OutputPath:    filepath.Join(planDagDirectory, "Beta.dll"),
		RefOutputPath: filepath.Join(planDagDirectory, "Beta"+referenceAssemblyExtension),
	}

	return &inputKeyProject{
		root: root,
		paths: EditorCompilerPaths{
			DotnetHostPath:  filepath.Join(root, "Editor", "dotnet"),
			CompilerDllPath: filepath.Join(root, "Editor", "csc.dll"),
		},
		plan: BuildPlan{
			DagDir:    planDagDirectory,
			OutputDir: outputDir,
			Units: []CompileUnit{
				{Assembly: alpha, Sources: []string{filepath.Join("Assets", "Alpha", "Alpha.cs")}},
				{Assembly: beta},
			},
		},
	}
}

// key computes the key of the unit under test.
func (project *inputKeyProject) key(t *testing.T) string {
	t.Helper()
	key, err := computeInputKey(project.root, project.paths, project.plan, project.plan.Units[0])
	if err != nil {
		t.Fatalf("expected a key, got error: %v", err)
	}
	if key == "" {
		t.Fatal("expected a non-empty key")
	}

	return key
}

// writeProjectFile writes one file of the fixture, creating the directories above it.
func writeProjectFile(t *testing.T, root string, relativePath string, content string) {
	t.Helper()
	path := filepath.Join(root, relativePath)
	if err := os.MkdirAll(filepath.Dir(path), outputDirPermissions); err != nil {
		t.Fatalf("failed to create the directory of %s: %v", relativePath, err)
	}
	if err := os.WriteFile(path, []byte(content), 0o600); err != nil {
		t.Fatalf("failed to write %s: %v", relativePath, err)
	}
}

// touchProjectFile gives a file a newer modification time without changing what it holds.
func touchProjectFile(t *testing.T, root string, relativePath string) {
	t.Helper()
	path := filepath.Join(root, relativePath)
	newer := time.Now().Add(time.Hour)
	if err := os.Chtimes(path, newer, newer); err != nil {
		t.Fatalf("failed to touch %s: %v", relativePath, err)
	}
}

// Verifies the same inputs produce the same key, so an unchanged assembly is recognized as one.
func TestComputeInputKeyIsStableForUnchangedInputs(t *testing.T) {
	project := newInputKeyProject(t)

	if first, second := project.key(t), project.key(t); first != second {
		t.Errorf("the key should not move on its own, got %s then %s", first, second)
	}
}

// Verifies a source edit moves the key, and that restoring the content brings the original key
// back even though the file is now newer than it was.
func TestComputeInputKeyFollowsSourceContentRatherThanItsTimestamp(t *testing.T) {
	project := newInputKeyProject(t)
	source := filepath.Join("Assets", "Alpha", "Alpha.cs")
	original := project.key(t)

	writeProjectFile(t, project.root, source, "class Alpha { void Added() {} }")
	edited := project.key(t)
	if edited == original {
		t.Error("editing a source should move the key")
	}

	writeProjectFile(t, project.root, source, "class Alpha {}")
	touchProjectFile(t, project.root, source)
	if restored := project.key(t); restored != original {
		t.Error("restoring a source's content should bring its key back")
	}
}

// Verifies a reference this run produced is keyed by content: recompiling the assembly it belongs
// to writes the same bytes with a new timestamp, and that must not count as a changed input.
func TestComputeInputKeyKeysAProducedReferenceByContent(t *testing.T) {
	project := newInputKeyProject(t)
	reference := filepath.Join(project.plan.OutputDir, "Beta"+referenceAssemblyExtension)
	original := project.key(t)

	writeProjectFile(t, project.root, reference, "beta surface")
	touchProjectFile(t, project.root, reference)
	if rewritten := project.key(t); rewritten != original {
		t.Error("rewriting a produced reference with the same bytes should leave the key alone")
	}

	writeProjectFile(t, project.root, reference, "beta surface with more")
	if changed := project.key(t); changed == original {
		t.Error("a produced reference with different bytes should move the key")
	}
}

// Verifies a reference this run does not produce is keyed by its timestamp, so a Unity build that
// rewrote it counts as a changed input whatever it wrote.
func TestComputeInputKeyKeysAnExternalReferenceByItsTimestamp(t *testing.T) {
	project := newInputKeyProject(t)
	original := project.key(t)

	touchProjectFile(t, project.root, filepath.Join(planDagDirectory, "Gamma"+referenceAssemblyExtension))

	if touched := project.key(t); touched == original {
		t.Error("a reference from the last Unity build should be keyed by its timestamp")
	}
}

// Verifies the response file Bee wrote, and the companion file beside it, are part of the key: they
// carry the defines, the analyzer list and the path mapping csc compiles with.
func TestComputeInputKeyFollowsTheResponseFiles(t *testing.T) {
	project := newInputKeyProject(t)
	responseFile := filepath.Join(planDagDirectory, "Alpha.rsp")
	companionFile := filepath.Join(planDagDirectory, "Alpha"+companionFileExtension)
	original := project.key(t)

	writeProjectFile(t, project.root, responseFile, "-out:\"Alpha.dll\"\n-define:EXTRA\n")
	edited := project.key(t)
	if edited == original {
		t.Error("a changed response file should move the key")
	}

	writeProjectFile(t, project.root, companionFile, "/pathmap:\"X\"=.\n")
	withCompanion := project.key(t)
	if withCompanion == edited {
		t.Error("a companion response file that appeared should move the key")
	}

	writeProjectFile(t, project.root, companionFile, "/pathmap:\"Y\"=.\n")
	if changedCompanion := project.key(t); changedCompanion == withCompanion {
		t.Error("a changed companion response file should move the key")
	}
}

// Verifies the files an analyzer reads are keyed by content: a banned-symbol list or a ruleset the
// project edits changes which diagnostics csc reports, so reusing the previous ones would lie.
func TestComputeInputKeyFollowsTheContentOfAnalyzerInputFiles(t *testing.T) {
	ruleset := filepath.Join("Assets", "Default.ruleset")
	bannedSymbols := filepath.Join("Analyzers", "Banned.txt")
	project := newInputKeyProject(t)
	writeProjectFile(t, project.root, ruleset, "<RuleSet/>")
	writeProjectFile(t, project.root, bannedSymbols, "T:System.Console")
	project.plan.Units[0].Assembly.OtherFlags = []string{
		`-ruleset:"Assets/Default.ruleset"`,
		"-additionalfile:Analyzers/Banned.txt",
	}
	original := project.key(t)

	touchProjectFile(t, project.root, ruleset)
	if touched := project.key(t); touched != original {
		t.Error("touching a ruleset without editing it should leave the key alone")
	}

	writeProjectFile(t, project.root, ruleset, "<RuleSet Name=\"Other\"/>")
	editedRuleset := project.key(t)
	if editedRuleset == original {
		t.Error("an edited ruleset should move the key")
	}

	writeProjectFile(t, project.root, bannedSymbols, "T:System.Console\nT:System.DateTime")
	if editedList := project.key(t); editedList == editedRuleset {
		t.Error("an edited analyzer input file should move the key")
	}
}

// Verifies the /additionalfile csc reads is keyed by content as well.
func TestComputeInputKeyFollowsTheContentOfTheAdditionalFile(t *testing.T) {
	project := newInputKeyProject(t)
	original := project.key(t)

	writeProjectFile(t, project.root, filepath.Join("Assets", "Analyzers", "Banned.txt"), "T:System.IO.File")

	if edited := project.key(t); edited == original {
		t.Error("an edited additional file should move the key")
	}
}

// Verifies a flag naming a file this check does not follow refuses to produce a key at all, so the
// assembly compiles every time rather than being reused against an input nothing watches.
func TestComputeInputKeyRefusesAFlagWhoseFileItDoesNotFollow(t *testing.T) {
	for _, flag := range []string{`/keyfile:"Keys/Sign.snk"`, "-keyfile:Keys/Sign.snk", "/LinkResource:x.res"} {
		project := newInputKeyProject(t)
		project.plan.Units[0].Assembly.OtherFlags = []string{flag}

		key, err := computeInputKey(project.root, project.paths, project.plan, project.plan.Units[0])
		if err == nil {
			t.Errorf("%s names a file that is not followed, so it should refuse a key", flag)
		}
		if key != "" {
			t.Errorf("%s should produce no key, got %q", flag, key)
		}
	}
}

// Verifies a flag that only names where csc writes does not stop a key from being built.
func TestComputeInputKeyAcceptsFlagsThatOnlyNameOutputs(t *testing.T) {
	project := newInputKeyProject(t)
	project.plan.Units[0].Assembly.OtherFlags = []string{"-doc:Alpha.xml", "/errorlog:Alpha.json", "-nowarn:0649"}

	project.key(t)
}

// Verifies the compiler itself is part of the key: an Editor upgrade compiles the same sources into
// different diagnostics.
func TestComputeInputKeyFollowsTheCompiler(t *testing.T) {
	project := newInputKeyProject(t)
	original := project.key(t)

	touchProjectFile(t, project.root, filepath.Join("Editor", "csc.dll"))

	if touched := project.key(t); touched == original {
		t.Error("a compiler with a new timestamp should move the key")
	}
}

// Verifies a missing input refuses a key rather than keying the assembly on what is left, which
// would reuse diagnostics for a compile that cannot even run.
func TestComputeInputKeyRefusesAKeyWhenAnInputIsMissing(t *testing.T) {
	project := newInputKeyProject(t)
	if err := os.Remove(
		filepath.Join(project.root, planDagDirectory, "Gamma"+referenceAssemblyExtension)); err != nil {
		t.Fatalf("failed to remove the reference: %v", err)
	}

	if _, err := computeInputKey(
		project.root, project.paths, project.plan, project.plan.Units[0]); err == nil {
		t.Error("expected a missing reference to refuse a key")
	}
}

// Verifies a flag placed in Bee's second response file is read exactly like one in the first: those
// lines reach csc verbatim, so an analyzer input named there decides the diagnostics just the same.
func TestComputeInputKeyFollowsAnalyzerInputsNamedInTheCompanionResponseFile(t *testing.T) {
	ruleset := filepath.Join("Assets", "Companion.ruleset")
	project := newInputKeyProject(t)
	writeProjectFile(t, project.root, ruleset, "<RuleSet/>")
	project.plan.Units[0].Assembly.CompanionFlags = []string{`-ruleset:"Assets/Companion.ruleset"`}
	original := project.key(t)

	touchProjectFile(t, project.root, ruleset)
	if touched := project.key(t); touched != original {
		t.Error("touching a ruleset named in the companion file should leave the key alone")
	}

	writeProjectFile(t, project.root, ruleset, "<RuleSet Name=\"Other\"/>")
	if edited := project.key(t); edited == original {
		t.Error("editing a ruleset named in the companion file should move the key")
	}
}

// Verifies a flag naming an unfollowed file refuses a key from the companion response file too,
// rather than letting the assembly be reused against an input nothing watches.
func TestComputeInputKeyRefusesAnUnfollowedFlagInTheCompanionResponseFile(t *testing.T) {
	project := newInputKeyProject(t)
	project.plan.Units[0].Assembly.CompanionFlags = []string{`/keyfile:"Keys/Sign.snk"`}

	key, err := computeInputKey(project.root, project.paths, project.plan, project.plan.Units[0])
	if err == nil {
		t.Error("a keyfile in the companion response file should refuse a key")
	}
	if key != "" {
		t.Errorf("expected no key, got %q", key)
	}
}

// Verifies a missing primary response file refuses a key rather than being keyed on its absence.
// Bee writes one for every assembly it builds, so a run that cannot find it is looking at a state
// this check cannot describe - unlike the companion file, which Bee legitimately leaves out.
func TestComputeInputKeyRefusesAKeyWhenTheResponseFileIsGone(t *testing.T) {
	project := newInputKeyProject(t)
	if _, err := computeInputKey(
		project.root, project.paths, project.plan, project.plan.Units[0]); err != nil {
		t.Fatalf("the fixture should produce a key, got %v", err)
	}

	if err := os.Remove(project.plan.Units[0].Assembly.Path); err != nil {
		t.Fatal(err)
	}

	key, err := computeInputKey(project.root, project.paths, project.plan, project.plan.Units[0])
	if err == nil {
		t.Error("a missing response file should refuse a key")
	}
	if key != "" {
		t.Errorf("expected no key, got %q", key)
	}
}
