package automation

import (
	"bytes"
	"encoding/json"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
)

const coverageTestModulePath = "github.com/hatayama/unity-cli-loop/common"

func TestParseGoCoverProfileCountsCoveredStatementsOutsideExcludedPaths(t *testing.T) {
	// Verifies statements are totalled per block and blocks under an excluded path are left out.
	profile := "mode: set\n" +
		coverageTestModulePath + "/a/a.go:1.1,3.2 4 1\n" +
		coverageTestModulePath + "/a/a.go:4.1,6.2 6 0\n" +
		coverageTestModulePath + "/cmd/tool/main.go:1.1,9.2 10 0\n"

	totals, err := ParseGoCoverProfile(strings.NewReader(profile), []string{"/cmd/"})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if totals.Statements != 10 || totals.Covered != 4 {
		t.Fatalf("expected 4 of 10 statements covered, got %d of %d", totals.Covered, totals.Statements)
	}
}

func TestParseGoCoverProfileCountsARepeatedBlockOnceAsCoveredWhenAnyRunCoveredIt(t *testing.T) {
	// Verifies a block reported by several test binaries counts once, covered if any run covered it.
	profile := "mode: set\n" +
		coverageTestModulePath + "/a/a.go:1.1,3.2 5 0\n" +
		coverageTestModulePath + "/a/a.go:1.1,3.2 5 1\n"

	totals, err := ParseGoCoverProfile(strings.NewReader(profile), nil)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if totals.Statements != 5 || totals.Covered != 5 {
		t.Fatalf("expected 5 of 5 statements covered, got %d of %d", totals.Covered, totals.Statements)
	}
}

func TestParseGoCoverProfileRejectsMalformedInput(t *testing.T) {
	// Verifies input go test would not write is an error rather than a figure, including a negative
	// statement count that would otherwise push coverage past 100%.
	cases := map[string]string{
		"empty":          "",
		"no mode header": coverageTestModulePath + "/a/a.go:1.1,3.2 5 1\n",
		"malformed line": "mode: set\n" + coverageTestModulePath + "/a/a.go 5 1\n",
		"unknown mode":   "mode: bogus\n" + coverageTestModulePath + "/a/a.go:1.1,3.2 5 1\n",
		"bad range":      "mode: set\n" + coverageTestModulePath + "/a/a.go:whatever 5 1\n",
		"negative count": "mode: set\n" + coverageTestModulePath + "/a/a.go:1.1,3.2 -6 0\n",
	}
	for name, profile := range cases {
		t.Run(name, func(t *testing.T) {
			if _, err := ParseGoCoverProfile(strings.NewReader(profile), nil); err == nil {
				t.Fatalf("expected an error for %q", profile)
			}
		})
	}
}

func TestRunCoverageReportGatePassesAtOrAboveBaseline(t *testing.T) {
	// Verifies gate mode exits 0 and prints a table row when every module meets its baseline.
	fixture := newCoverageFixture(t, map[string]float64{"common": 40.0})
	fixture.writeProfile("common", 4, 10)

	code, stdout, _ := fixture.run(coverageModeGate)

	if code != 0 {
		t.Fatalf("expected exit 0, got %d\n%s", code, stdout)
	}
	if !strings.Contains(stdout, "| common | 40.0% | 40.0% |") {
		t.Fatalf("expected a table row for common:\n%s", stdout)
	}
}

func TestRunCoverageReportGateFailsBelowBaselineButReportDoesNot(t *testing.T) {
	// Verifies a module under its baseline fails gate mode and only warns in report mode.
	fixture := newCoverageFixture(t, map[string]float64{"common": 50.0})
	fixture.writeProfile("common", 4, 10)

	gateCode, _, gateStderr := fixture.run(coverageModeGate)
	reportCode, reportStdout, _ := fixture.run(coverageModeReport)

	if gateCode != 1 || !strings.Contains(gateStderr, "common") {
		t.Fatalf("expected gate to fail naming common, got %d: %s", gateCode, gateStderr)
	}
	if reportCode != 0 || !strings.Contains(reportStdout, "::warning::") {
		t.Fatalf("expected report to exit 0 with a warning, got %d:\n%s", reportCode, reportStdout)
	}
}

func TestRunCoverageReportToleratesRoundingJustUnderBaseline(t *testing.T) {
	// Verifies a value within 0.1 point under the baseline still passes, so one-digit rounding of
	// the stored baseline cannot fail an unchanged tree.
	fixture := newCoverageFixture(t, map[string]float64{"common": 33.4})
	fixture.writeProfile("common", 1, 3)

	code, stdout, stderr := fixture.run(coverageModeGate)

	if code != 0 {
		t.Fatalf("expected 33.33%% to pass a 33.4%% baseline, got %d\n%s%s", code, stdout, stderr)
	}
}

func TestRunCoverageReportShowsAndSuggestsTheSameTruncatedFigure(t *testing.T) {
	// Verifies a measured 66.66% is shown and suggested as 66.6, never rounded up past what was
	// measured, so raising the baseline to the shown figure cannot fail the same tree.
	fixture := newCoverageFixture(t, map[string]float64{"common": 10.0})
	fixture.writeProfile("common", 2, 3)

	_, stdout, _ := fixture.run(coverageModeReport)

	if !strings.Contains(stdout, "| common | 66.6% | 10.0% | raise baseline to 66.6 |") {
		t.Fatalf("expected the truncated figure in both columns:\n%s", stdout)
	}
}

func TestRunCoverageReportGateFailsJustPastTheRoundingTolerance(t *testing.T) {
	// Verifies a module 0.2 point under its baseline fails, so the tolerance cannot widen past
	// the one-decimal rounding it exists for without a test noticing.
	fixture := newCoverageFixture(t, map[string]float64{"common": 40.2})
	fixture.writeProfile("common", 4, 10)

	code, stdout, _ := fixture.run(coverageModeGate)

	if code != 1 {
		t.Fatalf("expected 40.0%% to fail a 40.2%% baseline, got %d\n%s", code, stdout)
	}
}

func TestCollectGoCoverProfilesNamesEachProfileAfterItsModule(t *testing.T) {
	// Verifies every <module>.out in the directory is collected under its module name, so a
	// module whose profile appears without a baseline entry reaches the mismatch check.
	dir := t.TempDir()
	for _, name := range []string{"common.out", "new-module.out", "notes.txt"} {
		if err := os.WriteFile(filepath.Join(dir, name), []byte("mode: set\n"), 0o600); err != nil {
			t.Fatalf("write %s: %v", name, err)
		}
	}

	profiles, err := CollectGoCoverProfiles(dir)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(profiles) != 2 ||
		profiles["common"] != filepath.Join(dir, "common.out") ||
		profiles["new-module"] != filepath.Join(dir, "new-module.out") {
		t.Fatalf("expected common and new-module profiles, got %v", profiles)
	}
}

func TestCollectGoCoverProfilesRejectsAMissingDirectory(t *testing.T) {
	// Verifies a coverage directory that was never written fails instead of yielding no modules.
	if _, err := CollectGoCoverProfiles(filepath.Join(t.TempDir(), "absent")); err == nil {
		t.Fatal("expected an error for a missing directory")
	}
}

func TestRunCoverageReportHintsWhenTheBaselineCanBeRaised(t *testing.T) {
	// Verifies a module a full point above its baseline is reported as ready to raise.
	fixture := newCoverageFixture(t, map[string]float64{"common": 30.0})
	fixture.writeProfile("common", 4, 10)

	code, stdout, _ := fixture.run(coverageModeGate)

	if code != 0 || !strings.Contains(stdout, "raise") {
		t.Fatalf("expected a raise hint, got %d:\n%s", code, stdout)
	}
}

func TestRunCoverageReportFailsClosedOnMismatchedInputs(t *testing.T) {
	// Verifies a missing profile, an unknown module, a profile with no counted statements, and a
	// missing profile file each fail both modes instead of silently passing the gate.
	cases := map[string]func(fixture *coverageFixture){
		"baseline module without profile": func(fixture *coverageFixture) {
			fixture.writeProfile("common", 4, 10)
			delete(fixture.profiles, "dispatcher")
		},
		"profile for module not in baseline": func(fixture *coverageFixture) {
			fixture.writeProfile("common", 4, 10)
			fixture.writeProfile("dispatcher", 4, 10)
			fixture.writeProfile("unknown", 4, 10)
		},
		"no counted statements": func(fixture *coverageFixture) {
			fixture.writeProfile("common", 4, 10)
			fixture.writeRawProfile("dispatcher", "mode: set\n")
		},
		"profile file missing": func(fixture *coverageFixture) {
			fixture.writeProfile("common", 4, 10)
			fixture.profiles["dispatcher"] = filepath.Join(fixture.dir, "absent.out")
		},
	}
	for name, arrange := range cases {
		for _, mode := range []string{coverageModeGate, coverageModeReport} {
			t.Run(name+"/"+mode, func(t *testing.T) {
				fixture := newCoverageFixture(t, map[string]float64{"common": 10.0, "dispatcher": 10.0})
				fixture.writeProfile("dispatcher", 4, 10)
				arrange(fixture)

				code, stdout, stderr := fixture.run(mode)

				if code == 0 {
					t.Fatalf("expected a non-zero exit, got 0\n%s%s", stdout, stderr)
				}
			})
		}
	}
}

func TestRunCoverageReportRejectsABaselineOutsideZeroToHundred(t *testing.T) {
	// Verifies a negative baseline, which no coverage could fall below, fails instead of disabling
	// the gate for that module.
	fixture := newCoverageFixture(t, map[string]float64{"common": -1})
	fixture.writeProfile("common", 0, 10)

	if code, stdout, stderr := fixture.run(coverageModeGate); code == 0 {
		t.Fatalf("expected a non-zero exit, got 0\n%s%s", stdout, stderr)
	}
}

func TestRunCoverageReportRejectsANullBaseline(t *testing.T) {
	// Verifies a null figure is not read as 0%, which would pass the gate at any coverage.
	fixture := newCoverageFixture(t, map[string]float64{"common": 10})
	fixture.writeProfile("common", 0, 10)
	if err := os.WriteFile(fixture.baselinePath, []byte(`{"go":{"modules":{"common":null}}}`), 0o600); err != nil {
		t.Fatalf("write baseline: %v", err)
	}

	if code, stdout, stderr := fixture.run(coverageModeGate); code == 0 {
		t.Fatalf("expected a non-zero exit, got 0\n%s%s", stdout, stderr)
	}
}

func TestRunCoverageReportWritesTheTableToTheStepSummary(t *testing.T) {
	// Verifies the same table is appended to the step summary file when one is given.
	fixture := newCoverageFixture(t, map[string]float64{"common": 40.0})
	fixture.writeProfile("common", 4, 10)
	summaryPath := filepath.Join(fixture.dir, "summary.md")
	fixture.summaryPath = summaryPath

	if code, stdout, stderr := fixture.run(coverageModeReport); code != 0 {
		t.Fatalf("expected exit 0, got %d\n%s%s", code, stdout, stderr)
	}

	summary, err := os.ReadFile(summaryPath)
	if err != nil {
		t.Fatalf("read summary: %v", err)
	}
	if !strings.Contains(string(summary), "| common | 40.0% | 40.0% |") {
		t.Fatalf("expected the table in the step summary:\n%s", summary)
	}
}

type coverageFixture struct {
	t            *testing.T
	dir          string
	baselinePath string
	profiles     map[string]string
	summaryPath  string

	csharpSummaryPath string
	markdownOutPath   string
}

func newCoverageFixture(t *testing.T, modules map[string]float64) *coverageFixture {
	t.Helper()
	dir := t.TempDir()
	content, err := json.Marshal(map[string]any{
		"go": map[string]any{"exclude": []string{"/cmd/"}, "modules": modules},
	})
	if err != nil {
		t.Fatalf("marshal baseline: %v", err)
	}
	baselinePath := filepath.Join(dir, "coverage-baseline.json")
	if err := os.WriteFile(baselinePath, content, 0o600); err != nil {
		t.Fatalf("write baseline: %v", err)
	}
	return &coverageFixture{t: t, dir: dir, baselinePath: baselinePath, profiles: map[string]string{}}
}

// writeProfile writes a profile with one covered block and one uncovered block, plus an excluded
// block that must not change the result.
func (fixture *coverageFixture) writeProfile(module string, covered int, total int) {
	fixture.t.Helper()
	content := "mode: set\n" +
		coverageTestModulePath + "/a/a.go:1.1,3.2 " + strconv.Itoa(covered) + " 1\n" +
		coverageTestModulePath + "/a/a.go:4.1,6.2 " + strconv.Itoa(total-covered) + " 0\n" +
		coverageTestModulePath + "/cmd/tool/main.go:1.1,9.2 7 0\n"
	fixture.writeRawProfile(module, content)
}

func (fixture *coverageFixture) writeRawProfile(module string, content string) {
	fixture.t.Helper()
	path := filepath.Join(fixture.dir, module+".out")
	if err := os.WriteFile(path, []byte(content), 0o600); err != nil {
		fixture.t.Fatalf("write profile: %v", err)
	}
	fixture.profiles[module] = path
}

func (fixture *coverageFixture) run(mode string) (int, string, string) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunCoverageReport(&stdout, &stderr, CoverageReportOptions{
		BaselinePath: fixture.baselinePath,
		GoProfiles:   fixture.profiles,
		Mode:         mode,
		SummaryPath:  fixture.summaryPath,

		CSharpSummaryPath: fixture.csharpSummaryPath,
		MarkdownOutPath:   fixture.markdownOutPath,
	})
	return code, stdout.String(), stderr.String()
}

func TestRunCoverageReportGoOnlyOutputIsUnchangedByteForByte(t *testing.T) {
	// Verifies stdout and the step summary keep their exact Go-only content when no C# summary is
	// given, so the pull-request gate output is not disturbed by the C# report.
	fixture := newCoverageFixture(t, map[string]float64{"common": 50.0, "dispatcher": 10.0})
	fixture.writeProfile("common", 4, 10)
	fixture.writeProfile("dispatcher", 2, 3)
	fixture.summaryPath = filepath.Join(fixture.dir, "summary.md")

	code, stdout, stderr := fixture.run(coverageModeReport)

	table := "| Module | Coverage | Baseline | Note |\n|---|---|---|---|\n" +
		"| common | 40.0% | 50.0% | below baseline |\n" +
		"| dispatcher | 66.6% | 10.0% | raise baseline to 66.6 |\n"
	if code != 0 || stderr != "" {
		t.Fatalf("expected exit 0 with no stderr, got %d: %s", code, stderr)
	}
	wantStdout := table + "::warning::Coverage fell below the baseline in: common\n"
	if stdout != wantStdout {
		t.Fatalf("stdout changed:\nwant %q\ngot  %q", wantStdout, stdout)
	}
	summary, err := os.ReadFile(fixture.summaryPath)
	if err != nil {
		t.Fatalf("read summary: %v", err)
	}
	if wantSummary := "## Go test coverage\n\n" + table + "\n"; string(summary) != wantSummary {
		t.Fatalf("step summary changed:\nwant %q\ngot  %q", wantSummary, summary)
	}
}
