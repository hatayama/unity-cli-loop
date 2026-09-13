package compilecheck

import (
	"path/filepath"
	"strings"
	"testing"
)

// writeBeeLog writes the Bee log of the last build, one JSON object per line.
func writeBeeLog(t *testing.T, projectRoot string, lines ...string) {
	t.Helper()

	writeFileAt(t,
		filepath.Join(projectRoot, libraryDirectoryName, beeDirectoryName, tundraLogFileName),
		strings.Join(lines, "\n")+"\n")
}

// beeInitLine is the line Bee always writes first, which names the dag file of the run.
func beeInitLine(dagName string) string {
	return `{"msg":"init","dagFile":"Library/Bee/` + dagName + `"}`
}

// beeNodeResultLine spells one finished Bee node the way the real log spells it.
func beeNodeResultLine(annotation string, outputFile string, exitCode string) string {
	return `{"msg":"noderesult","annotation":"` + annotation +
		`","outputfile":"` + outputFile + `","exitcode":` + exitCode + `}`
}

// Verifies only the assemblies whose compiler step exited non-zero are reported.
func TestReadFailedUnityCompilesListsAssembliesWhoseCompilerStepFailed(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag")
	dagDir := relativeDagDir("aaaa.dag")
	writeBeeLog(t, projectRoot,
		beeInitLine("aaaa.dag"),
		beeNodeResultLine("Csc Library/Bee/artifacts/aaaa.dag/Clean.dll",
			"Library/Bee/artifacts/aaaa.dag/Clean.dll", "0"),
		beeNodeResultLine("Csc Library/Bee/artifacts/aaaa.dag/Broken.dll (+2 others)",
			"Library/Bee/artifacts/aaaa.dag/Broken.dll", "1"),
		// A failure of a step that is not the compiler says nothing about an assembly's sources.
		beeNodeResultLine("WriteText Library/Bee/artifacts/aaaa.dag/Notes.dll",
			"Library/Bee/artifacts/aaaa.dag/Notes.dll", "1"),
		// Bee writes a null exit code for a node it never ran to completion.
		beeNodeResultLine("Csc Library/Bee/artifacts/aaaa.dag/Unknown.dll",
			"Library/Bee/artifacts/aaaa.dag/Unknown.dll", "null"),
	)

	failed := ReadFailedUnityCompiles(projectRoot, dagDir)

	if len(failed) != 1 || !failed["Broken"] {
		t.Errorf("expected only Broken to be reported as failed, got %v", failed)
	}
}

// Verifies a node whose exitcode field is absent is not read as a failure.
func TestReadFailedUnityCompilesIgnoresANodeWithoutAnExitCode(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag")
	dagDir := relativeDagDir("aaaa.dag")
	writeBeeLog(t, projectRoot,
		beeInitLine("aaaa.dag"),
		`{"msg":"noderesult","annotation":"Csc Library/Bee/artifacts/aaaa.dag/Quiet.dll",`+
			`"outputfile":"Library/Bee/artifacts/aaaa.dag/Quiet.dll"}`,
	)

	failed := ReadFailedUnityCompiles(projectRoot, dagDir)

	if len(failed) != 0 {
		t.Errorf("expected no assembly to be reported as failed, got %v", failed)
	}
}

// Verifies a failure recorded against another dag directory is not attributed to this one.
func TestReadFailedUnityCompilesIgnoresOtherDagDirectories(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag", "aaaaDbg.dag")
	dagDir := relativeDagDir("aaaa.dag")
	writeBeeLog(t, projectRoot,
		beeInitLine("aaaaDbg.dag"),
		beeNodeResultLine("Csc Library/Bee/artifacts/aaaaDbg.dag/Broken.dll",
			"Library/Bee/artifacts/aaaaDbg.dag/Broken.dll", "1"),
	)

	failed := ReadFailedUnityCompiles(projectRoot, dagDir)

	if len(failed) != 0 {
		t.Errorf("expected the other dag's failure to be ignored, got %v", failed)
	}
}

// Verifies a project with no Bee log reports no failures rather than failing the run.
func TestReadFailedUnityCompilesReturnsNothingWithoutABeeLog(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag")

	failed := ReadFailedUnityCompiles(projectRoot, relativeDagDir("aaaa.dag"))

	if len(failed) != 0 {
		t.Errorf("expected no failures without a log, got %v", failed)
	}
}

// Verifies a log that stops making sense partway keeps what it said before that point.
func TestReadFailedUnityCompilesStopsAtACorruptLine(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag")
	dagDir := relativeDagDir("aaaa.dag")
	writeBeeLog(t, projectRoot,
		beeInitLine("aaaa.dag"),
		beeNodeResultLine("Csc Library/Bee/artifacts/aaaa.dag/Broken.dll",
			"Library/Bee/artifacts/aaaa.dag/Broken.dll", "1"),
		`{"msg":"noderesult","annotation":`,
		beeNodeResultLine("Csc Library/Bee/artifacts/aaaa.dag/Later.dll",
			"Library/Bee/artifacts/aaaa.dag/Later.dll", "1"),
	)

	failed := ReadFailedUnityCompiles(projectRoot, dagDir)

	if len(failed) != 1 || !failed["Broken"] {
		t.Errorf("expected the failures read before the corrupt line, got %v", failed)
	}
}

// Verifies a failure recorded after a node whose captured output is longer than a line scanner's
// default buffer is still found.
func TestReadFailedUnityCompilesHandlesLinesLongerThanTheScannerDefault(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag")
	dagDir := relativeDagDir("aaaa.dag")
	writeBeeLog(t, projectRoot,
		beeInitLine("aaaa.dag"),
		`{"msg":"noderesult","annotation":"Csc Library/Bee/artifacts/aaaa.dag/Loud.dll",`+
			`"outputfile":"Library/Bee/artifacts/aaaa.dag/Loud.dll","exitcode":0,"stdout":"`+
			strings.Repeat("w", 70*1024)+`"}`,
		beeNodeResultLine("Csc Library/Bee/artifacts/aaaa.dag/Broken.dll",
			"Library/Bee/artifacts/aaaa.dag/Broken.dll", "1"),
	)

	failed := ReadFailedUnityCompiles(projectRoot, dagDir)

	if len(failed) != 1 || !failed["Broken"] {
		t.Errorf("expected the failure after the long line to be found, got %v", failed)
	}
}
