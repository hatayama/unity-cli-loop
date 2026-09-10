package compilecheck

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

const sampleDagDirectory = "Library/Bee/artifacts/aaaa.dag"

// sampleResponseFileLines is one Bee response file shrunk to one line per shape the parser handles.
func sampleResponseFileLines() []string {
	return []string{
		`-target:library`,
		`-out:"` + sampleDagDirectory + `/Sample.dll"`,
		`-refout:"` + sampleDagDirectory + `/Sample.ref.dll"`,
		`-define:UNITY_2022_3_62`,
		`-define:UNITY_EDITOR`,
		`-r:"/Applications/Unity/Editor/Data/Managed/UnityEngine.dll"`,
		`-r:"` + sampleDagDirectory + `/Other.ref.dll"`,
		`-r:"Library/ScriptAssemblies/External.dll"`,
		`-analyzer:"/Applications/Unity/Editor/Data/Tools/Unity.SourceGenerators.dll"`,
		`"Assets/Scripts/First.cs"`,
		`"Assets/Scripts/Second.cs"`,
		`-langversion:9.0`,
		`/deterministic`,
		`/nowarn:0169`,
		`/additionalfile:"` + sampleDagDirectory + `/Sample.UnityAdditionalFile.txt"`,
	}
}

// writeResponseFile writes the given lines joined by the given terminator, with no trailing newline.
func writeResponseFile(t *testing.T, lines []string, lineTerminator string) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), "Sample.rsp")
	if err := os.WriteFile(path, []byte(strings.Join(lines, lineTerminator)), 0o600); err != nil {
		t.Fatalf("failed to write the response file: %v", err)
	}

	return path
}

// assertSampleFields checks every field the parser fills from the sample response file.
func assertSampleFields(t *testing.T, parsed ResponseFile) {
	t.Helper()
	if parsed.AssemblyName != "Sample" {
		t.Errorf("assembly name = %s, want Sample", parsed.AssemblyName)
	}
	if parsed.OutputPath != filepath.FromSlash(sampleDagDirectory+"/Sample.dll") {
		t.Errorf("output path = %s", parsed.OutputPath)
	}
	if parsed.RefOutputPath != filepath.FromSlash(sampleDagDirectory+"/Sample.ref.dll") {
		t.Errorf("ref output path = %s", parsed.RefOutputPath)
	}
	assertStrings(t, "defines", parsed.Defines, []string{"UNITY_2022_3_62", "UNITY_EDITOR"})
	assertStrings(t, "references", parsed.References, []string{
		filepath.FromSlash("/Applications/Unity/Editor/Data/Managed/UnityEngine.dll"),
		filepath.FromSlash(sampleDagDirectory + "/Other.ref.dll"),
		filepath.FromSlash("Library/ScriptAssemblies/External.dll"),
	})
	assertStrings(t, "analyzers", parsed.Analyzers, []string{
		filepath.FromSlash("/Applications/Unity/Editor/Data/Tools/Unity.SourceGenerators.dll"),
	})
	assertStrings(t, "sources", parsed.Sources, []string{
		filepath.FromSlash("Assets/Scripts/First.cs"),
		filepath.FromSlash("Assets/Scripts/Second.cs"),
	})
	if parsed.AdditionalFile != filepath.FromSlash(sampleDagDirectory+"/Sample.UnityAdditionalFile.txt") {
		t.Errorf("additional file = %s", parsed.AdditionalFile)
	}
	assertStrings(t, "other flags", parsed.OtherFlags, []string{
		"-target:library", "-langversion:9.0", "/deterministic", "/nowarn:0169",
	})
}

// assertStrings reports the first difference between a parsed slice and what the sample expects.
func assertStrings(t *testing.T, label string, got []string, want []string) {
	t.Helper()
	if len(got) != len(want) {
		t.Errorf("%s = %v, want %v", label, got, want)

		return
	}
	for index := range want {
		if got[index] != want[index] {
			t.Errorf("%s[%d] = %s, want %s", label, index, got[index], want[index])
		}
	}
}

// Verifies every flag shape in a Bee response file lands in the field it belongs to.
func TestParseResponseFileSplitsEveryFlagShape(t *testing.T) {
	parsed, err := ParseResponseFile(writeResponseFile(t, sampleResponseFileLines(), "\n"))
	if err != nil {
		t.Fatalf("expected the sample to parse, got error: %v", err)
	}

	assertSampleFields(t, parsed)
}

// Verifies a response file written with Windows line endings parses identically.
func TestParseResponseFileAcceptsCarriageReturnLineEndings(t *testing.T) {
	parsed, err := ParseResponseFile(writeResponseFile(t, sampleResponseFileLines(), "\r\n"))
	if err != nil {
		t.Fatalf("expected the sample to parse, got error: %v", err)
	}

	assertSampleFields(t, parsed)
}

// Verifies only the references that name a reference assembly in the dag directory become dependencies.
func TestParseResponseFileReportsProjectAssemblyReferences(t *testing.T) {
	parsed, err := ParseResponseFile(writeResponseFile(t, sampleResponseFileLines(), "\n"))
	if err != nil {
		t.Fatalf("expected the sample to parse, got error: %v", err)
	}

	assertStrings(t, "project references", parsed.ProjectAssemblyReferences(sampleDagDirectory), []string{"Other"})
}

// Verifies a line that is neither a flag nor a quoted source is rejected instead of silently dropped.
func TestParseResponseFileRejectsUnrecognizedLine(t *testing.T) {
	lines := append(sampleResponseFileLines(), "foo")

	_, err := ParseResponseFile(writeResponseFile(t, lines, "\n"))
	if err == nil {
		t.Fatal("expected an unrecognized line to fail the parse")
	}
	if !strings.Contains(err.Error(), `"foo"`) {
		t.Errorf("error should name the offending line, got: %v", err)
	}
}

// Verifies a response file with no -out flag is rejected rather than producing an unusable plan.
func TestParseResponseFileRequiresOutputFlag(t *testing.T) {
	lines := []string{`"Assets/Scripts/First.cs"`}

	_, err := ParseResponseFile(writeResponseFile(t, lines, "\n"))
	if err == nil {
		t.Fatal("expected a response file without -out to fail the parse")
	}
}

// Verifies a response file that lists no sources is rejected.
func TestParseResponseFileRequiresSources(t *testing.T) {
	lines := []string{`-out:"` + sampleDagDirectory + `/Sample.dll"`}

	_, err := ParseResponseFile(writeResponseFile(t, lines, "\n"))
	if err == nil {
		t.Fatal("expected a response file without sources to fail the parse")
	}
}

// writeCompanionResponseFile writes the .rsp2 Bee places beside the given response file.
func writeCompanionResponseFile(t *testing.T, responseFilePath string, content string) {
	t.Helper()
	path := strings.TrimSuffix(responseFilePath, ".rsp") + ".rsp2"
	if err := os.WriteFile(path, []byte(content), 0o600); err != nil {
		t.Fatalf("failed to write the companion response file: %v", err)
	}
}

// Verifies the flags Bee wrote in the second response file are carried over verbatim, including a
// file that ends without a newline, which is how Bee writes it.
func TestParseResponseFileReadsTheCompanionFlags(t *testing.T) {
	path := writeResponseFile(t, sampleResponseFileLines(), "\n")
	companionLine := `/pathmap:"/projects/Sample"=.`
	writeCompanionResponseFile(t, path, companionLine)

	parsed, err := ParseResponseFile(path)
	if err != nil {
		t.Fatalf("expected the sample to parse, got error: %v", err)
	}

	assertStrings(t, "companion flags", parsed.CompanionFlags, []string{companionLine})
}

// Verifies an empty second response file leaves no flags behind, since Bee writes an empty one for
// every assembly it builds without the extra flags.
func TestParseResponseFileAcceptsAnEmptyCompanionFile(t *testing.T) {
	path := writeResponseFile(t, sampleResponseFileLines(), "\n")
	writeCompanionResponseFile(t, path, "")

	parsed, err := ParseResponseFile(path)
	if err != nil {
		t.Fatalf("expected the sample to parse, got error: %v", err)
	}

	if parsed.CompanionFlags != nil {
		t.Errorf("companion flags = %v, want none", parsed.CompanionFlags)
	}
}

// Verifies a missing second response file is not an error: an older build may not have written one.
func TestParseResponseFileAcceptsAMissingCompanionFile(t *testing.T) {
	parsed, err := ParseResponseFile(writeResponseFile(t, sampleResponseFileLines(), "\n"))
	if err != nil {
		t.Fatalf("expected the sample to parse, got error: %v", err)
	}

	if parsed.CompanionFlags != nil {
		t.Errorf("companion flags = %v, want none", parsed.CompanionFlags)
	}
}
