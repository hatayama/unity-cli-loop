package compilecheck

import (
	"context"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

// Verifies a non-positive job count still compiles every unit, one at a time.
func TestCompileUnitsTreatsANonPositiveJobCountAsOne(t *testing.T) {
	plan := newSchedulerPlan(nil, "A", "B")
	recorder := newSchedulerRecorder(5 * time.Millisecond)

	outcome, err := compileUnits(context.Background(), newSchedulerCompiler(t, recorder), plan, 0)
	if err != nil {
		t.Fatalf("expected the units to compile, got error: %v", err)
	}

	if len(compiledNames(outcome.Units)) != 2 || recorder.maxRunning != 1 {
		t.Fatalf("compiled=%v maxRunning=%d, want both units compiled one at a time", compiledNames(outcome.Units), recorder.maxRunning)
	}
}

// Verifies a run whose output directory cannot be created fails before compiling anything.
func TestCompileUnitsReportsAnOutputDirectoryThatCannotBeCreated(t *testing.T) {
	plan := newSchedulerPlan(nil, "A")
	recorder := newSchedulerRecorder(0)
	compiler := newSchedulerCompiler(t, recorder)
	writeFileAt(t, filepath.Join(compiler.ProjectRoot, "Library"), "not a directory")

	_, err := compileUnits(context.Background(), compiler, plan, 1)

	if err == nil || !strings.HasPrefix(err.Error(), "failed to create ") {
		t.Fatalf("err = %v", err)
	}
	if recorder.maxRunning != 0 {
		t.Fatalf("no unit may start, maxRunning = %d", recorder.maxRunning)
	}
}

// Verifies a plan reference to an assembly outside the plan does not hold the unit back.
func TestCompileUnitsIgnoresReferencesOutsideThePlan(t *testing.T) {
	plan := newSchedulerPlan(map[string][]string{"A": {"Outside"}}, "A", "B")

	outcome, err := compileUnits(context.Background(), newSchedulerCompiler(t, newSchedulerRecorder(0)), plan, 2)
	if err != nil {
		t.Fatalf("expected the units to compile, got error: %v", err)
	}

	assertStrings(t, "compiled", compiledNames(outcome.Units), []string{"A", "B"})
}

// Verifies a source the response file records but that is gone from disk is reported as an
// inspection failure naming the file.
func TestDetectSourceChangeReportsAMissingSource(t *testing.T) {
	projectRoot := newPlanProject(t)
	missing := "Assets/A/Gone.cs"
	rsp := ResponseFile{AssemblyName: "A", Sources: []string{missing}}

	_, err := DetectSourceChange(projectRoot, rsp, []string{missing}, planDagDirectory)

	if err == nil || !strings.HasPrefix(err.Error(), "failed to inspect ") || !strings.Contains(err.Error(), "Gone.cs") {
		t.Fatalf("err = %v", err)
	}
}

// Verifies an assembly definition that cannot be inspected is reported instead of being treated as
// unchanged.
func TestContractComparisonNeededReportsAMissingAssemblyDefinition(t *testing.T) {
	path := filepath.Join(t.TempDir(), "Gone.asmdef")

	_, err := contractComparisonNeeded(AssemblyDefinition{Path: path}, time.Now(), true)

	if err == nil || !strings.HasPrefix(err.Error(), "failed to inspect "+path) {
		t.Fatalf("err = %v", err)
	}
}

// Verifies a directory nobody owns is reported as unowned once the walk reaches the top.
func TestOwnerOfReportsADirectoryWithoutOwner(t *testing.T) {
	index := assemblyOwnerIndex{referencesByDirectory: map[string]string{}, definitionDirectories: map[string]bool{}}

	if _, found := index.ownerOf(filepath.Join(t.TempDir(), "Assets", "Loose")); found {
		t.Fatal("a directory without any .asmref or .asmdef above it must be unowned")
	}
}

// Verifies an .asmref that cannot be parsed fails the index with the file named.
func TestIndexAssemblyReferencesReportsAMalformedAsmref(t *testing.T) {
	projectRoot := t.TempDir()
	path := filepath.Join(projectRoot, "Assets", "Broken", "Broken.asmref")
	writeFileAt(t, path, "not json")

	_, err := IndexAssemblyReferences(projectRoot)

	if err == nil || !strings.HasPrefix(err.Error(), "failed to index assembly references under ") || !strings.Contains(err.Error(), "failed to read "+path) {
		t.Fatalf("err = %v", err)
	}
}

// Verifies response file lines that are not "-name:value" or "/name:value" flags are not read as
// flags, so neither a path-valued nor an untracked-input flag is found in them.
func TestSplitFlagRejectsLinesThatAreNotFlags(t *testing.T) {
	for _, line := range []string{"", "Assets/A.cs:3", "-nologo"} {
		if name, value, ok := splitFlag(line); ok {
			t.Fatalf("splitFlag(%q) = (%q, %q, true), want no flag", line, name, value)
		}
		if _, ok := pathValuedFlag(line); ok {
			t.Fatalf("pathValuedFlag(%q) found a path", line)
		}
		if untrackedFileInputFlag(line) {
			t.Fatalf("untrackedFileInputFlag(%q) = true", line)
		}
	}
	if name, value, ok := splitFlag("/RuleSet:a.ruleset"); !ok || name != "ruleset" || value != "a.ruleset" {
		t.Fatalf("splitFlag(/RuleSet:a.ruleset) = (%q, %q, %v)", name, value, ok)
	}
}

// Verifies an absolute response file path is used as is instead of being joined to the project root.
func TestProjectPathKeepsAbsolutePaths(t *testing.T) {
	absolutePath := filepath.Join(t.TempDir(), "Shared.dll")

	if got := projectPath(filepath.Join(t.TempDir(), "project"), absolutePath); got != absolutePath {
		t.Fatalf("projectPath = %q, want %q", got, absolutePath)
	}
}

// Verifies an .asmdef that cannot be parsed, or that declares no name, fails the index with the
// file named.
func TestIndexAssemblyDefinitionsReportsUnusableAsmdefs(t *testing.T) {
	cases := map[string]string{
		"not json":    "failed to read ",
		`{"name":""}`: " declares no assembly name",
	}
	for content, wantMessage := range cases {
		projectRoot := t.TempDir()
		path := filepath.Join(projectRoot, "Assets", "Broken", "Broken.asmdef")
		writeFileAt(t, path, content)

		_, err := IndexAssemblyDefinitions(projectRoot)

		if err == nil || !strings.HasPrefix(err.Error(), "failed to index ") || !strings.Contains(err.Error(), path) || !strings.Contains(err.Error(), wantMessage) {
			t.Fatalf("content %q: err = %v, want %q", content, err, wantMessage)
		}
	}
}

// Verifies listing the sources of a directory that does not exist is reported with the directory.
func TestGlobAssemblySourcesReportsAMissingDirectory(t *testing.T) {
	directory := filepath.Join(t.TempDir(), "Gone")
	index := assemblyOwnerIndex{referencesByDirectory: map[string]string{}, definitionDirectories: map[string]bool{}}

	_, err := globAssemblySources(t.TempDir(), directory, index)

	if err == nil || !strings.HasPrefix(err.Error(), "failed to list sources under "+directory) {
		t.Fatalf("err = %v", err)
	}
}
