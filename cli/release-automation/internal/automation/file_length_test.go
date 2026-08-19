package automation

import (
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestCountSLOCIgnoresCommentMarkersInsideCSharpStrings(t *testing.T) {
	// Verifies // and /* inside C# quoted strings count as code, not comments.
	source := "string url = \"http://example.com/*path*/\";\n"
	assertSLOC(t, source, LanguageCSharp, 1)
}

func TestCountSLOCIgnoresCommentMarkersInsideGoStrings(t *testing.T) {
	// Verifies // and /* inside Go interpreted strings count as code, not comments.
	source := "url := \"http://example.com/*path*/\"\n"
	assertSLOC(t, source, LanguageGo, 1)
}

func TestCountSLOCCountsVerbatimStringCommentLookalikes(t *testing.T) {
	// Verifies a verbatim string line that looks like a comment still counts as SLOC.
	source := "string s = @\"\n// not a comment\n/* also not */\n\";"
	assertSLOC(t, source, LanguageCSharp, 4)
}

func TestCountSLOCCountsGoRawStringCommentLookalikes(t *testing.T) {
	// Verifies a Go raw-string line that looks like a comment still counts as SLOC.
	source := "s := `\n// not a comment\n/* also not */\n`"
	assertSLOC(t, source, LanguageGo, 4)
}

func TestCountSLOCCountsTrailingLineCommentWithCode(t *testing.T) {
	// Verifies `code(); // note` is one SLOC because the statement remains.
	source := "code(); // note\n"
	assertSLOC(t, source, LanguageCSharp, 1)
	assertSLOC(t, source, LanguageGo, 1)
}

func TestCountSLOCCountsBlockCommentMixedWithCodeOnSameLine(t *testing.T) {
	// Verifies a line that mixes a block comment with statements still counts once.
	source := "int x = 1; /* comment */ int y = 2;\n"
	assertSLOC(t, source, LanguageCSharp, 1)
	assertSLOC(t, source, LanguageGo, 1)
}

func TestCountSLOCTreatsCRLFAndLFAsTheSameCount(t *testing.T) {
	// Verifies Windows CRLF and Unix LF produce the same SLOC for the same statements.
	lfSource := "int x = 1;\n\nint y = 2;\n"
	crlfSource := "int x = 1;\r\n\r\nint y = 2;\r\n"
	lfCount := CountSLOC([]byte(lfSource), LanguageCSharp)
	crlfCount := CountSLOC([]byte(crlfSource), LanguageCSharp)
	if lfCount != 2 || crlfCount != lfCount {
		t.Fatalf("expected CRLF and LF to both count 2 SLOC, got LF=%d CRLF=%d", lfCount, crlfCount)
	}
}

func TestCountSLOCIgnoresUTF8BOM(t *testing.T) {
	// Verifies a UTF-8 BOM on a comment-only line is not itself SLOC.
	// why not pair BOM with a code line: unicode.IsSpace(U+FEFF) is false, so a
	// missing skipBOM would still pass if that line is counted for other code.
	withBOM := append([]byte{0xEF, 0xBB, 0xBF}, []byte("// comment only\n")...)
	if CountSLOC(withBOM, LanguageCSharp) != 0 {
		t.Fatal("expected BOM followed by a comment-only line to count 0 SLOC")
	}
}

func TestCountSLOCDoesNotTreatGoRuneLiteralQuoteAsStringOpener(t *testing.T) {
	// Verifies a Go rune containing a double-quote does not swallow a later comment line.
	source := "q := '\\\"'\n// comment only\nx := 1\n"
	assertSLOC(t, source, LanguageGo, 2)
}

func TestCountSLOCCountsGoRuneLiteralWithEscapedApostrophe(t *testing.T) {
	// Verifies an escaped apostrophe inside a Go rune literal still closes correctly.
	source := "q := '\\''\n"
	assertSLOC(t, source, LanguageGo, 1)
}

func TestCountSLOCExcludesBlankAndCommentOnlyLines(t *testing.T) {
	// Verifies blank lines, // comments, /// docs, and block-comment-only lines are not SLOC.
	source := "// line comment\n" +
		"/// xml doc\n" +
		"\n" +
		"/*\n" +
		" block\n" +
		" */\n" +
		"int x = 1;\n"
	assertSLOC(t, source, LanguageCSharp, 1)
}

func TestCountSLOCCountsCSharpInterpolatedStringHolesAsCode(t *testing.T) {
	// Verifies interpolation holes are scanned as code so nested strings stay intact.
	source := "string s = $\"http://example.com/{id}\";\n"
	assertSLOC(t, source, LanguageCSharp, 1)
}

func TestCountSLOCCountsCSharpVerbatimInterpolationCommentLookalikes(t *testing.T) {
	// Verifies $@ verbatim interpolation still treats // inside the string as code.
	source := "string s = $@\"\n// not a comment {id}\n\";"
	assertSLOC(t, source, LanguageCSharp, 3)
}

func TestCountSLOCCountsCSharpCharacterLiteralWithEscapedQuote(t *testing.T) {
	// Verifies an escaped quote in a char literal does not end the literal early.
	source := "char quote = '\\''; // note\n"
	assertSLOC(t, source, LanguageCSharp, 1)
}

func TestScanFileLengthsReportsOnlyProductionSourcesOverTheLimit(t *testing.T) {
	// Verifies the centralized exclusion list drops tests, testdata, and Assets.
	root := t.TempDir()
	writeRepoFile(t, root, "Packages/src/OverLimit.cs", csharpLines(3))
	writeRepoFile(t, root, "Packages/src/Tests/OverLimitTest.cs", csharpLines(3))
	writeRepoFile(t, root, "cli/over_limit.go", goLines(3))
	writeRepoFile(t, root, "cli/over_limit_test.go", goLines(3))
	writeRepoFile(t, root, "cli/internal/testdata/sample.go", goLines(3))
	writeRepoFile(t, root, "tools/OverLimit.cs", csharpLines(3))
	writeRepoFile(t, root, "Assets/OverLimit.cs", csharpLines(3))
	writeRepoFile(t, root, "Packages/src/UnderLimit.cs", csharpLines(1))

	findings, err := ScanFileLengths(root, 2)
	if err != nil {
		t.Fatalf("ScanFileLengths returned %v", err)
	}
	got := findingPaths(findings)
	want := []string{
		"Packages/src/OverLimit.cs",
		"cli/over_limit.go",
		"tools/OverLimit.cs",
	}
	if strings.Join(got, ",") != strings.Join(want, ",") {
		t.Fatalf("unexpected findings: got %v want %v", got, want)
	}
}

func TestRunFileLengthCheckWarnsWithoutFailingWhenFindingsExist(t *testing.T) {
	// Verifies warning mode prints findings and still exits 0.
	root := t.TempDir()
	writeRepoFile(t, root, "Packages/src/OverLimit.cs", csharpLines(3))

	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	exitCode := RunFileLengthCheck(stdout, stderr, FileLengthCheckOptions{
		Root:           root,
		MaxLength:      2,
		FailOnExceeded: false,
	})
	if exitCode != 0 {
		t.Fatalf("expected warning mode to exit 0, got %d", exitCode)
	}
	if !strings.Contains(stdout.String(), "Packages/src/OverLimit.cs: 3 SLOC (limit 2)") {
		t.Fatalf("expected finding in stdout, got %q", stdout.String())
	}
	if !strings.Contains(stdout.String(), "CODE_FILE_LENGTH_FAIL_ON_EXCEEDED=true") {
		t.Fatalf("expected warning-mode hint, got %q", stdout.String())
	}
}

func TestRunFileLengthCheckFailsWhenFailOnExceeded(t *testing.T) {
	// Verifies fail mode exits 1 when any file exceeds the limit.
	root := t.TempDir()
	writeRepoFile(t, root, "cli/over_limit.go", goLines(3))

	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	exitCode := RunFileLengthCheck(stdout, stderr, FileLengthCheckOptions{
		Root:           root,
		MaxLength:      2,
		FailOnExceeded: true,
	})
	if exitCode != 1 {
		t.Fatalf("expected fail mode to exit 1, got %d", exitCode)
	}
	if stderr.Len() != 0 {
		t.Fatalf("expected no stderr on findings, got %q", stderr.String())
	}
}

func TestDefaultMaxFileLengthIsFiveHundred(t *testing.T) {
	// Verifies the Go default stays at the documented 500-line threshold.
	if DefaultMaxFileLength != 500 {
		t.Fatalf("DefaultMaxFileLength = %d, want 500", DefaultMaxFileLength)
	}
}

func assertSLOC(t *testing.T, source string, language SourceLanguage, want int) {
	t.Helper()
	got := CountSLOC([]byte(source), language)
	if got != want {
		t.Fatalf("CountSLOC() = %d, want %d for %q", got, want, source)
	}
}

func writeRepoFile(t *testing.T, root string, relativePath string, contents string) {
	t.Helper()
	absolutePath := filepath.Join(root, filepath.FromSlash(relativePath))
	if err := os.MkdirAll(filepath.Dir(absolutePath), 0o755); err != nil {
		t.Fatalf("mkdir %s: %v", filepath.Dir(absolutePath), err)
	}
	if err := os.WriteFile(absolutePath, []byte(contents), 0o644); err != nil {
		t.Fatalf("write %s: %v", relativePath, err)
	}
}

func csharpLines(count int) string {
	builder := strings.Builder{}
	for index := 0; index < count; index++ {
		builder.WriteString("int value = 1;\n")
	}
	return builder.String()
}

func goLines(count int) string {
	builder := strings.Builder{}
	for index := 0; index < count; index++ {
		builder.WriteString("x := 1\n")
	}
	return builder.String()
}

func findingPaths(findings []FileLengthFinding) []string {
	paths := make([]string, 0, len(findings))
	for _, finding := range findings {
		paths = append(paths, finding.Path)
	}
	return paths
}
