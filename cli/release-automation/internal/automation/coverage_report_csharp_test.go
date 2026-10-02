package automation

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

const csharpCoverageTestHeading = "## C# test coverage (EditMode, Unity 2022.3)\n\n"

const csharpCoverageTestGoMarkdown = "## Go test coverage\n\n" +
	"| Module | Coverage | Baseline | Note |\n|---|---|---|---|\n" +
	"| common | 40.0% | 40.0% |  |\n\n"

type csharpTestAssembly struct {
	Name      string `json:"name"`
	Covered   int    `json:"coveredlines"`
	Coverable int    `json:"coverablelines"`
}

// csharpTestAssemblies is listed out of report order on purpose: the two assemblies tied on
// uncovered lines come in reverse name order, so the name tie-break is visible in the output.
func csharpTestAssemblies() []csharpTestAssembly {
	return []csharpTestAssembly{
		{Name: "UnityCLILoop.Zeta", Covered: 0, Coverable: 5},
		{Name: "UnityCLILoop.Beta", Covered: 45, Coverable: 50},
		{Name: "UnityCLILoop.Empty", Covered: 0, Coverable: 0},
		{Name: "UnityCLILoop.Alpha", Covered: 10, Coverable: 100},
		{Name: "UnityCLILoop.Runtime", Covered: 1, Coverable: 4},
		{Name: "UnityCLILoop.Overlay", Covered: 0, Coverable: 9},
	}
}

// csharpTestScopedTables is the two tables for csharpTestAssemblies with Runtime and Overlay
// excluded; the scope is 55 of 155 lines, 35.48%, shown truncated as 35.4.
const csharpTestScopedTables = "| Assembly | Covered | Coverable | Coverage |\n|---|---|---|---|\n" +
	"| UnityCLILoop.Alpha | 10 | 100 | 10.0% |\n" +
	"| UnityCLILoop.Beta | 45 | 50 | 90.0% |\n" +
	"| UnityCLILoop.Zeta | 0 | 5 | 0.0% |\n" +
	"| UnityCLILoop.Empty | 0 | 0 | - |\n\n" +
	"### Excluded from the measured scope\n\n" +
	"| Assembly | Covered | Coverable | Coverage |\n|---|---|---|---|\n" +
	"| UnityCLILoop.Overlay | 0 | 9 | 0.0% |\n" +
	"| UnityCLILoop.Runtime | 1 | 4 | 25.0% |\n\n"

func csharpTestBaseline(lineCoverage float64) map[string]any {
	return map[string]any{
		"exclude":      []string{"UnityCLILoop.Runtime", "UnityCLILoop.Overlay"},
		"lineCoverage": lineCoverage,
	}
}

// writeBaselineWithCSharp rewrites the fixture baseline with one Go module at 40% and the given
// raw csharp section; a nil section leaves the key out.
func (fixture *coverageFixture) writeBaselineWithCSharp(csharp any) {
	fixture.t.Helper()
	baseline := map[string]any{
		"go": map[string]any{"exclude": []string{"/cmd/"}, "modules": map[string]float64{"common": 40.0}},
	}
	if csharp != nil {
		baseline["csharp"] = csharp
	}
	content, err := json.Marshal(baseline)
	if err != nil {
		fixture.t.Fatalf("marshal baseline: %v", err)
	}
	if err := os.WriteFile(fixture.baselinePath, content, 0o600); err != nil {
		fixture.t.Fatalf("write baseline: %v", err)
	}
}

func (fixture *coverageFixture) writeCSharpSummary(assemblies any) {
	fixture.t.Helper()
	content, err := json.Marshal(map[string]any{
		"summary":  map[string]any{"linecoverage": 99.9},
		"coverage": map[string]any{"assemblies": assemblies},
	})
	if err != nil {
		fixture.t.Fatalf("marshal summary: %v", err)
	}
	fixture.writeRawCSharpSummary(string(content))
}

func (fixture *coverageFixture) writeRawCSharpSummary(content string) {
	fixture.t.Helper()
	fixture.csharpSummaryPath = filepath.Join(fixture.dir, "Summary.json")
	if err := os.WriteFile(fixture.csharpSummaryPath, []byte(content), 0o600); err != nil {
		fixture.t.Fatalf("write summary: %v", err)
	}
}

func newCSharpCoverageFixture(t *testing.T, csharp any) *coverageFixture {
	t.Helper()
	fixture := newCoverageFixture(t, map[string]float64{"common": 40.0})
	fixture.writeProfile("common", 4, 10)
	fixture.writeBaselineWithCSharp(csharp)
	fixture.writeCSharpSummary(csharpTestAssemblies())
	fixture.summaryPath = filepath.Join(fixture.dir, "step-summary.md")
	fixture.markdownOutPath = filepath.Join(fixture.dir, "coverage.md")
	return fixture
}

func readCoverageTestFile(t *testing.T, path string) string {
	t.Helper()
	content, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("read %s: %v", path, err)
	}
	return string(content)
}

func TestRunCoverageReportAddsTheCSharpSectionToEveryOutput(t *testing.T) {
	// Verifies the C# section sums only the scoped assemblies, sorts both tables by uncovered lines
	// then name, shows "-" for an assembly with no coverable lines, and reaches stdout, the step
	// summary, and the Markdown file after the Go table.
	fixture := newCSharpCoverageFixture(t, csharpTestBaseline(30.0))

	code, stdout, stderr := fixture.run(coverageModeReport)

	if code != 0 || stderr != "" {
		t.Fatalf("expected exit 0 with no stderr, got %d: %s", code, stderr)
	}
	section := csharpCoverageTestHeading +
		"Measured scope: 35.4% (55 / 155 lines), baseline 30.0% (+5.4)\n\n" +
		csharpTestScopedTables
	wantMarkdown := csharpCoverageTestGoMarkdown + section
	if got := readCoverageTestFile(t, fixture.markdownOutPath); got != wantMarkdown {
		t.Fatalf("markdown file:\nwant %q\ngot  %q", wantMarkdown, got)
	}
	if got := readCoverageTestFile(t, fixture.summaryPath); got != wantMarkdown {
		t.Fatalf("step summary:\nwant %q\ngot  %q", wantMarkdown, got)
	}
	wantStdout := "| Module | Coverage | Baseline | Note |\n|---|---|---|---|\n| common | 40.0% | 40.0% |  |\n" + section
	if stdout != wantStdout {
		t.Fatalf("stdout:\nwant %q\ngot  %q", wantStdout, stdout)
	}
}

func TestRunCoverageReportWarnsOnStdoutOnlyWhenCSharpFallsBelowItsBaseline(t *testing.T) {
	// Verifies a scope under its baseline adds a ::warning:: line to stdout, marks the section
	// "below baseline" in the Markdown file without the warning line, and never changes the exit
	// code in either mode.
	for _, mode := range []string{coverageModeGate, coverageModeReport} {
		t.Run(mode, func(t *testing.T) {
			fixture := newCSharpCoverageFixture(t, csharpTestBaseline(35.6))

			code, stdout, stderr := fixture.run(mode)

			if code != 0 {
				t.Fatalf("expected C# to warn without gating, got %d: %s", code, stderr)
			}
			wantWarning := "::warning::C# coverage fell below the baseline: 35.4% against 35.6%\n"
			if !strings.HasSuffix(stdout, csharpTestScopedTables+wantWarning) {
				t.Fatalf("expected the C# warning after the section:\n%s", stdout)
			}
			markdown := readCoverageTestFile(t, fixture.markdownOutPath)
			wantLine := csharpCoverageTestHeading +
				"Measured scope: 35.4% (55 / 155 lines), baseline 35.6% (-0.2), below baseline\n\n"
			if !strings.Contains(markdown, wantLine) || strings.Contains(markdown, "::warning::") {
				t.Fatalf("expected a below-baseline line and no warning command:\n%s", markdown)
			}
		})
	}
}

func TestRunCoverageReportToleratesCSharpRoundingJustUnderBaseline(t *testing.T) {
	// Verifies a scope within 0.1 point under its baseline (35.48% against 35.5%) is not reported
	// as below, matching the Go tolerance for one-decimal baselines.
	fixture := newCSharpCoverageFixture(t, csharpTestBaseline(35.5))

	_, stdout, _ := fixture.run(coverageModeReport)

	if strings.Contains(stdout, "::warning::") || strings.Contains(stdout, "below baseline") {
		t.Fatalf("expected no below-baseline report:\n%s", stdout)
	}
}

func TestRunCoverageReportCSharpFailsClosedOnBadInputs(t *testing.T) {
	// Verifies each malformed C# input fails both modes with a message only that check produces.
	cases := map[string]struct {
		arrange func(fixture *coverageFixture)
		message string
	}{
		"summary file missing": {
			arrange: func(fixture *coverageFixture) { fixture.csharpSummaryPath = filepath.Join(fixture.dir, "absent.json") },
			message: "read C# coverage summary",
		},
		"summary not JSON": {
			arrange: func(fixture *coverageFixture) { fixture.writeRawCSharpSummary("{not json") },
			message: "parse C# coverage summary",
		},
		"no assemblies": {
			arrange: func(fixture *coverageFixture) { fixture.writeCSharpSummary([]csharpTestAssembly{}) },
			message: "coverage summary lists no C# assemblies",
		},
		"assemblies key missing": {
			arrange: func(fixture *coverageFixture) { fixture.writeRawCSharpSummary(`{"coverage":{}}`) },
			message: "coverage summary lists no C# assemblies",
		},
		"negative coverable lines": {
			arrange: func(fixture *coverageFixture) {
				fixture.writeCSharpSummary(withCSharpTestAssembly(csharpTestAssembly{Name: "UnityCLILoop.Bad", Covered: 0, Coverable: -1}))
			},
			message: `assembly "UnityCLILoop.Bad" has negative coverable lines`,
		},
		"negative covered lines": {
			arrange: func(fixture *coverageFixture) {
				fixture.writeCSharpSummary(withCSharpTestAssembly(csharpTestAssembly{Name: "UnityCLILoop.Bad", Covered: -1, Coverable: 3}))
			},
			message: `assembly "UnityCLILoop.Bad" has negative covered lines`,
		},
		"covered above coverable": {
			arrange: func(fixture *coverageFixture) {
				fixture.writeCSharpSummary(withCSharpTestAssembly(csharpTestAssembly{Name: "UnityCLILoop.Bad", Covered: 4, Coverable: 3}))
			},
			message: `assembly "UnityCLILoop.Bad" covers 4 of 3 coverable lines`,
		},
		"baseline without csharp section": {
			arrange: func(fixture *coverageFixture) { fixture.writeBaselineWithCSharp(nil) },
			message: "baseline has no csharp section",
		},
		"exclude names an absent assembly": {
			arrange: func(fixture *coverageFixture) {
				fixture.writeBaselineWithCSharp(map[string]any{"exclude": []string{"UnityCLILoop.Runtime", "UnityCLILoop.Overlay", "UnityCLILoop.Typo"}, "lineCoverage": 30.0})
			},
			message: `csharp exclude names "UnityCLILoop.Typo", which the summary does not list`,
		},
		"no coverable lines in scope": {
			arrange: func(fixture *coverageFixture) {
				fixture.writeCSharpSummary([]csharpTestAssembly{{Name: "UnityCLILoop.Empty"}, {Name: "UnityCLILoop.Runtime", Covered: 1, Coverable: 4}, {Name: "UnityCLILoop.Overlay", Coverable: 9}})
			},
			message: "measured C# scope has no coverable lines",
		},
		"lineCoverage above 100": {
			arrange: func(fixture *coverageFixture) { fixture.writeBaselineWithCSharp(csharpTestBaseline(100.1)) },
			message: "csharp lineCoverage is 100.1, outside 0 to 100",
		},
		"lineCoverage below 0": {
			arrange: func(fixture *coverageFixture) { fixture.writeBaselineWithCSharp(csharpTestBaseline(-0.1)) },
			message: "csharp lineCoverage is -0.1, outside 0 to 100",
		},
		"lineCoverage missing": {
			arrange: func(fixture *coverageFixture) {
				fixture.writeBaselineWithCSharp(map[string]any{"exclude": []string{"UnityCLILoop.Runtime"}})
			},
			message: "csharp baseline has no lineCoverage",
		},
		"lineCoverage null": {
			arrange: func(fixture *coverageFixture) {
				fixture.writeBaselineWithCSharp(map[string]any{"exclude": []string{"UnityCLILoop.Runtime"}, "lineCoverage": nil})
			},
			message: "csharp baseline has no lineCoverage",
		},
		"exclude missing": {
			arrange: func(fixture *coverageFixture) { fixture.writeBaselineWithCSharp(map[string]any{"lineCoverage": 30.0}) },
			message: "csharp baseline has no exclude list",
		},
	}
	for name, testCase := range cases {
		for _, mode := range []string{coverageModeGate, coverageModeReport} {
			t.Run(name+"/"+mode, func(t *testing.T) {
				fixture := newCSharpCoverageFixture(t, csharpTestBaseline(30.0))
				testCase.arrange(fixture)

				code, _, stderr := fixture.run(mode)

				if code == 0 || !strings.Contains(stderr, testCase.message) {
					t.Fatalf("expected a non-zero exit with %q, got %d: %s", testCase.message, code, stderr)
				}
			})
		}
	}
}

func withCSharpTestAssembly(extra csharpTestAssembly) []csharpTestAssembly {
	return append(csharpTestAssemblies(), extra)
}

func TestRunCoverageReportIgnoresABrokenCSharpBaselineWithoutASummary(t *testing.T) {
	// Verifies the Go gate neither reads nor validates the csharp section when no C# summary is
	// given, so a broken C# baseline cannot change a pull request's Go result.
	for name, csharp := range map[string]any{
		"wrong types":   map[string]any{"exclude": "UnityCLILoop.Runtime", "lineCoverage": "high"},
		"missing keys":  map[string]any{},
		"out of range":  csharpTestBaseline(250),
		"not an object": []int{1, 2},
	} {
		t.Run(name, func(t *testing.T) {
			fixture := newCoverageFixture(t, map[string]float64{"common": 40.0})
			fixture.writeProfile("common", 4, 10)
			fixture.writeBaselineWithCSharp(csharp)

			code, stdout, stderr := fixture.run(coverageModeGate)

			want := "| Module | Coverage | Baseline | Note |\n|---|---|---|---|\n| common | 40.0% | 40.0% |  |\n"
			if code != 0 || stdout != want || stderr != "" {
				t.Fatalf("expected the plain Go result, got %d:\n%s%s", code, stdout, stderr)
			}
		})
	}
}

func TestRunCoverageReportWritesGoOnlyMarkdownWithoutACSharpSummary(t *testing.T) {
	// Verifies the Markdown file holds just the Go section when no C# summary is given, and that an
	// existing file is replaced rather than appended to.
	fixture := newCoverageFixture(t, map[string]float64{"common": 40.0})
	fixture.writeProfile("common", 4, 10)
	fixture.markdownOutPath = filepath.Join(fixture.dir, "coverage.md")
	if err := os.WriteFile(fixture.markdownOutPath, []byte("stale content\n"), 0o600); err != nil {
		t.Fatalf("write stale file: %v", err)
	}

	if code, stdout, stderr := fixture.run(coverageModeReport); code != 0 {
		t.Fatalf("expected exit 0, got %d\n%s%s", code, stdout, stderr)
	}

	if got := readCoverageTestFile(t, fixture.markdownOutPath); got != csharpCoverageTestGoMarkdown {
		t.Fatalf("markdown file:\nwant %q\ngot  %q", csharpCoverageTestGoMarkdown, got)
	}
}

func TestRunCoverageReportFailsWhenTheMarkdownFileCannotBeWritten(t *testing.T) {
	// Verifies a Markdown path that cannot be written fails instead of leaving the trend comment
	// without content; a directory stands in for the path so the failure does not depend on the user.
	fixture := newCoverageFixture(t, map[string]float64{"common": 40.0})
	fixture.writeProfile("common", 4, 10)
	fixture.markdownOutPath = t.TempDir()

	code, _, stderr := fixture.run(coverageModeReport)

	if code == 0 || !strings.Contains(stderr, "write coverage markdown") {
		t.Fatalf("expected a markdown write failure, got %d: %s", code, stderr)
	}
}

func TestRunCoverageReportPrintsTheCSharpSectionEvenWhenGoFailsTheGate(t *testing.T) {
	// Verifies a Go module under its baseline still fails the gate with exit 1 while the C# section
	// is reported, so the C# report neither hides nor overrides the Go result.
	fixture := newCSharpCoverageFixture(t, csharpTestBaseline(30.0))
	fixture.writeProfile("common", 3, 10)

	code, stdout, stderr := fixture.run(coverageModeGate)

	if code != 1 || !strings.Contains(stderr, "Coverage fell below the baseline in: common") {
		t.Fatalf("expected the Go gate to fail, got %d: %s", code, stderr)
	}
	if !strings.Contains(stdout, csharpCoverageTestHeading) {
		t.Fatalf("expected the C# section on stdout:\n%s", stdout)
	}
}
