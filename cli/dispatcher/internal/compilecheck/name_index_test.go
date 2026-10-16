package compilecheck

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func identifiersOf(content string) identifierSet {
	set := identifierSet{}
	scanIdentifiers([]byte(content), set)

	return set
}

func assertHasIdentifiers(t *testing.T, set identifierSet, present []string, absent []string) {
	t.Helper()
	for _, name := range present {
		if _, found := set[name]; !found {
			t.Errorf("expected %q among the identifiers, got %v", name, set)
		}
	}
	for _, name := range absent {
		if _, found := set[name]; found {
			t.Errorf("expected %q not to be among the identifiers, got %v", name, set)
		}
	}
}

// Verifies identifiers are cut by the characters C# allows in them: a spacing mark, a combining mark
// and a connector stay inside one identifier, a letter number starts one, and a formatting character
// is left out of the name, as the compiler leaves it out.
func TestScanIdentifiersCutsIdentifiersByTheCharactersCSharpAllows(t *testing.T) {
	set := identifiersOf("नाम cafe\u0301 a\u203fb \u216bth Fo\u200do")

	assertHasIdentifiers(t, set,
		[]string{"नाम", "cafe\u0301", "a\u203fb", "\u216bth", "Foo"},
		[]string{"न", "म", "cafe", "a", "b", "th", "Fo", "o"})
}

// Verifies a target-typed new is found with any white space before its parenthesis, and only then.
func TestScanIdentifiersFindsATargetTypedNewOnlyBeforeAParenthesis(t *testing.T) {
	cases := map[string]bool{
		"new(1)": true, "new   (1)": true, "new\n(1)": true, "new x(1)": false, "x = new": false, "new  ": false,
	}
	for content, want := range cases {
		if _, found := identifiersOf(content)[targetTypedNewToken]; found != want {
			t.Errorf("%q: target-typed new found = %v, want %v", content, found, want)
		}
	}
}

// BenchmarkScanIdentifiers reads a file holding many "new" expressions.
func BenchmarkScanIdentifiers(b *testing.B) {
	content := []byte(strings.Repeat("var x = new Thing(); ", 40_000))
	for b.Loop() {
		scanIdentifiers(content, identifierSet{})
	}
}

// Verifies identifiers are cut the way C# spells them: a verbatim '@' left off, Unicode letters
// kept, words in strings and comments read too - a \u escape in a literal decoded like one in an
// identifier - a target-typed new found with or without a space, and an identifier that only starts
// with "new" not taken for one.
func TestScanIdentifiersCutsIdentifiersTheWayCSharpSpellsThem(t *testing.T) {
	set := identifiersOf("var @class = new (1); Point p = new(2); newFoo(3); // Commented\n" +
		"string s = \"Quoted\"; int 名前 = _under9; char c = '\\u0041';")

	assertHasIdentifiers(t, set,
		[]string{"class", "new(", "newFoo", "Commented", "Quoted", "名前", "_under9", "Point", "A"},
		[]string{"@class", "u0041", "9"})
}

// Verifies "newFoo(" alone is not read as a target-typed new.
func TestScanIdentifiersDoesNotTakeAnIdentifierStartingWithNewForANew(t *testing.T) {
	assertHasIdentifiers(t, identifiersOf("newFoo(1); renew (2);"), []string{"newFoo", "renew"}, []string{"new("})
}

// writeUnitSources writes the given project-relative files and returns a unit compiling them.
func writeUnitSources(t *testing.T, root string, files map[string]string) CompileUnit {
	t.Helper()
	unit := CompileUnit{Assembly: ResponseFile{AssemblyName: "Unit"}}
	for path, content := range files {
		absolute := filepath.Join(root, filepath.FromSlash(path))
		if err := os.MkdirAll(filepath.Dir(absolute), 0o755); err != nil {
			t.Fatalf("failed to create %s: %v", path, err)
		}
		if err := os.WriteFile(absolute, []byte(content), 0o600); err != nil {
			t.Fatalf("failed to write %s: %v", path, err)
		}
		unit.Sources = append(unit.Sources, path)
	}

	return unit
}

// Verifies a unit's identifiers come from every source, and its generator output's from every C# file
// under the directory, nested directories included, with reading the latter reported.
func TestReadIdentifiersReadsSourcesAndGeneratedCode(t *testing.T) {
	root := t.TempDir()
	unit := writeUnitSources(t, root, map[string]string{"Assets/A.cs": "class First { }", "Assets/B.cs": "class Second { }"})
	writeUnitSources(t, root, map[string]string{"Generated/sub/a.CS": "class Third { }", "Generated/notes.txt": "Fourth"})

	set, withGenerated, err := readGeneratedIdentifiers(filepath.Join(root, "Generated"))
	if err != nil {
		t.Fatalf("expected the generated identifiers to be read, got %v", err)
	}
	if err := readSourceIdentifiers(root, unit, set); err != nil {
		t.Fatalf("expected the source identifiers to be read, got %v", err)
	}

	assertHasIdentifiers(t, set, []string{"First", "Second", "Third"}, []string{"Fourth"})
	if !withGenerated {
		t.Error("expected the generated code to be reported as read")
	}
}

// Verifies a missing generator output directory reads as no generated code rather than an error.
func TestReadGeneratedIdentifiersWithoutGeneratedCode(t *testing.T) {
	set, withGenerated, err := readGeneratedIdentifiers(filepath.Join(t.TempDir(), "Missing"))
	if err != nil {
		t.Fatalf("expected no error, got %v", err)
	}
	if withGenerated || len(set) != 0 {
		t.Errorf("expected no generated code, got %v (reported read: %v)", set, withGenerated)
	}
}

// Verifies a source given by absolute path, as a local package's is, is read from that path.
func TestReadSourceIdentifiersReadsAnAbsoluteSource(t *testing.T) {
	elsewhere := t.TempDir()
	writeUnitSources(t, elsewhere, map[string]string{"Package/P.cs": "class Packaged { }"})
	unit := CompileUnit{
		Assembly: ResponseFile{AssemblyName: "Unit"},
		Sources:  []string{filepath.ToSlash(filepath.Join(elsewhere, "Package", "P.cs"))},
	}
	set := identifierSet{}

	if err := readSourceIdentifiers(t.TempDir(), unit, set); err != nil {
		t.Fatalf("expected the identifiers to be read, got %v", err)
	}

	assertHasIdentifiers(t, set, []string{"Packaged"}, nil)
}

// Verifies a unit without sources, or with one that cannot be read, is refused rather than read as
// an empty set, so it is never read as writing no changed name.
func TestReadSourceIdentifiersRefusesAUnitItCannotRead(t *testing.T) {
	root := t.TempDir()
	missing := CompileUnit{Assembly: ResponseFile{AssemblyName: "Unit"}, Sources: []string{"Assets/Missing.cs"}}
	for label, unit := range map[string]CompileUnit{"no sources": {Assembly: ResponseFile{AssemblyName: "Unit"}}, "missing": missing} {
		if err := readSourceIdentifiers(root, unit, identifierSet{}); err == nil {
			t.Errorf("%s: expected an error", label)
		}
	}
}

// Verifies the background read of the sources is over once wait returns after it was asked for, and
// that lookup answers only for the units it was asked to read.
func TestNameIndexIsOverOnceWaitReturns(t *testing.T) {
	root := t.TempDir()
	unit := writeUnitSources(t, root, map[string]string{"Assets/A.cs": "class First { }"})
	index := nameIndex{}
	index.start(root, []CompileUnit{unit, unit}, []string{filepath.Join(root, "Missing"), ""}, []bool{true, false})
	index.readSources()

	index.wait()

	select {
	case <-index.done:
	default:
		t.Fatal("expected the read to be over once wait returned")
	}
	if _, _, err := index.lookup(0); err != nil {
		t.Errorf("expected the identifiers of the read unit, got %v", err)
	}
	if _, _, err := index.lookup(1); err == nil {
		t.Error("expected no identifiers for a unit that was not read")
	}
	if _, _, err := (&nameIndex{}).lookup(0); err == nil {
		t.Error("expected no identifiers from an index that never started")
	}
}

// Verifies the reported duration counts only the time spent reading, not the time the run spent
// before it asked for the sources.
// Why the bounds hold without flaking: the test waits 300 ms between start and readSources, so a
// duration measured from start is at least 300 ms, while reading one small file takes well under a
// millisecond; 150 ms leaves room on both sides for a slow machine.
func TestNameIndexReportCountsOnlyTheTimeSpentReading(t *testing.T) {
	root := t.TempDir()
	unit := writeUnitSources(t, root, map[string]string{"Assets/A.cs": "class First { }"})
	index := nameIndex{}
	index.start(root, []CompileUnit{unit}, []string{filepath.Join(root, "Missing")}, []bool{true})
	time.Sleep(300 * time.Millisecond)

	index.readSources()
	index.wait()

	if report := index.report(); !report.SourcesRead || report.Duration >= 150*time.Millisecond {
		t.Errorf("report = %+v, want the sources read in well under 150 ms", report)
	}
}

// escapeOf spells a \u or \U escape: the backslash followed by the given text.
func escapeOf(text string) string {
	return string(rune(0x5C)) + text
}

// Verifies \u and \U escapes are decoded into the identifier they spell, at its start or inside it.
func TestScanIdentifiersDecodesUnicodeEscapes(t *testing.T) {
	cases := map[string]string{
		escapeOf("u0041") + "bc":    "Abc",
		"A" + escapeOf("u0042"):     "AB",
		escapeOf("U00000041"):       "A",
		"A" + escapeOf("U00000042"): "AB",
		escapeOf("uFF21") + "oo":    "\uFF21oo",
	}
	for content, want := range cases {
		set := identifiersOf(content + " ")
		if _, found := set[want]; !found || len(set) != 1 {
			t.Errorf("%q: identifiers = %v, want only %q", content, set, want)
		}
	}
}

// Verifies an escape with a digit that is not hexadecimal, or with too few digits, is left as text.
func TestScanIdentifiersLeavesAMalformedEscapeAlone(t *testing.T) {
	assertHasIdentifiers(t, identifiersOf(`\u00G1 `), []string{"u00G1"}, []string{"A"})
	assertHasIdentifiers(t, identifiersOf(`\u004 `), []string{"u004"}, []string{"\x04"})
}

// Verifies an escape spelling a surrogate, at either end of the range, is not decoded, while the
// first value past the range is.
func TestDecodeUnicodeEscapeRejectsASurrogate(t *testing.T) {
	for _, content := range []string{escapeOf("uD800"), escapeOf("uDFFF")} {
		if character, _, ok := decodeUnicodeEscape([]byte(content), 0); ok {
			t.Errorf("%q: expected a surrogate escape to be rejected, got %q", content, character)
		}
	}
	if character, _, ok := decodeUnicodeEscape([]byte(escapeOf("uE000")), 0); !ok || character != 0xE000 {
		t.Errorf("expected the first value past the surrogates to decode, got %q (ok %v)", character, ok)
	}
}

// Verifies a file ending in a lone backslash is read to its end without failing.
func TestScanIdentifiersReadsAFileEndingInABackslash(t *testing.T) {
	assertHasIdentifiers(t, identifiersOf("x "+escapeOf("")), []string{"x"}, []string{})
}

// Verifies an escape cut short by the end of the file - down to a lone backslash - or spelling a
// value past the last code point, even one that overflows a signed 32-bit value, is not decoded.
func TestDecodeUnicodeEscapeRejectsATruncatedOrOutOfRangeEscape(t *testing.T) {
	for _, content := range []string{
		escapeOf(""), escapeOf("u004"), escapeOf("U0000004"), escapeOf("U00110000"),
		escapeOf("U80000000"), escapeOf("UFFFFFFFF"),
	} {
		if character, size, ok := decodeUnicodeEscape([]byte(content), 0); ok {
			t.Errorf("%q: decoded %q (size %d), want it rejected", content, character, size)
		}
	}
}
