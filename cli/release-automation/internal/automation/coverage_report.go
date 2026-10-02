package automation

import (
	"bufio"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math"
	"os"
	"path/filepath"
	"regexp"
	"sort"
	"strconv"
	"strings"
)

const (
	coverageModeGate   = "gate"
	coverageModeReport = "report"

	// coverageRoundingTolerance absorbs the one-decimal rounding of stored baselines, so an
	// unchanged tree never fails because its exact value sits just under the stored figure.
	coverageRoundingTolerance = 0.1
	// coverageRaiseHintMargin is how far above its baseline a module must be before the report
	// suggests raising the stored figure.
	coverageRaiseHintMargin = 1.0
)

// CoverageReportOptions configures one coverage report or gate run.
type CoverageReportOptions struct {
	BaselinePath string
	// GoProfiles maps a module name in the baseline to its `go test -coverprofile` output.
	GoProfiles  map[string]string
	Mode        string
	SummaryPath string
	// CSharpSummaryPath is a ReportGenerator Summary.json; when empty, C# is neither read nor
	// validated, so the pull-request Go gate does not depend on the csharp baseline section.
	CSharpSummaryPath string
	// MarkdownOutPath receives the step summary content without workflow commands, replacing any
	// existing file.
	MarkdownOutPath string
}

// GoCoverageTotals is the statement count of a profile after exclusions.
type GoCoverageTotals struct {
	Statements int
	Covered    int
}

type coverageBaseline struct {
	Go struct {
		Exclude []string `json:"exclude"`
		// Pointers so a null figure is told apart from 0 and rejected.
		Modules map[string]*float64 `json:"modules"`
	} `json:"go"`
	// CSharp stays raw until a C# summary is given, so a malformed section cannot fail a Go-only run.
	CSharp json.RawMessage `json:"csharp"`
}

type coverageModuleResult struct {
	Name     string
	Percent  float64
	Baseline float64
}

var goCoverModes = map[string]bool{"mode: set": true, "mode: count": true, "mode: atomic": true}

type goCoverBlock struct {
	statements int
	covered    bool
}

// RunCoverageReport compares each module's coverage with its stored baseline, prints a Markdown
// table, and in gate mode exits 1 when a module fell below its baseline.
func RunCoverageReport(stdout io.Writer, stderr io.Writer, options CoverageReportOptions) int {
	if options.Mode != coverageModeGate && options.Mode != coverageModeReport {
		_, _ = fmt.Fprintf(stderr, "unknown mode %q: use %s or %s\n", options.Mode, coverageModeGate, coverageModeReport)
		return 2
	}

	baseline, err := readCoverageBaseline(options.BaselinePath)
	if err != nil {
		_, _ = fmt.Fprintf(stderr, "read coverage baseline: %v\n", err)
		return 2
	}

	results, err := measureGoModules(baseline, options.GoProfiles)
	if err != nil {
		_, _ = fmt.Fprintf(stderr, "measure Go coverage: %v\n", err)
		return 2
	}

	csharp, err := measureOptionalCSharpCoverage(baseline, options.CSharpSummaryPath)
	if err != nil {
		_, _ = fmt.Fprintf(stderr, "measure C# coverage: %v\n", err)
		return 2
	}

	table := formatCoverageTable(results)
	below := modulesBelowBaseline(results)
	writeCoverageStdout(stdout, options.Mode, table, below, csharp)

	markdown := "## Go test coverage\n\n" + table + "\n" + formatOptionalCSharpSection(csharp)
	if err := appendCoverageSummary(options.SummaryPath, markdown); err != nil {
		_, _ = fmt.Fprintf(stderr, "write step summary: %v\n", err)
		return 2
	}
	if err := writeCoverageMarkdown(options.MarkdownOutPath, markdown); err != nil {
		_, _ = fmt.Fprintf(stderr, "write coverage markdown: %v\n", err)
		return 2
	}

	if len(below) == 0 || options.Mode == coverageModeReport {
		return 0
	}

	_, _ = fmt.Fprintf(stderr, "Coverage fell below the baseline in: %s. Add tests, or lower the figure in the baseline file with a reason in the pull request.\n", strings.Join(below, ", "))
	return 1
}

// measureOptionalCSharpCoverage returns nil when no C# summary was given.
func measureOptionalCSharpCoverage(baseline coverageBaseline, summaryPath string) (*csharpCoverageResult, error) {
	if summaryPath == "" {
		return nil, nil
	}
	result, err := measureCSharpCoverage(baseline.CSharp, summaryPath)
	if err != nil {
		return nil, err
	}
	return &result, nil
}

// writeCoverageStdout prints the Go table and its report-mode warning exactly as before the C#
// report existed, then the C# section and its warning when C# was measured. The C# warning is
// printed in both modes because C# never gates.
func writeCoverageStdout(stdout io.Writer, mode string, table string, below []string, csharp *csharpCoverageResult) {
	_, _ = fmt.Fprint(stdout, table)
	if len(below) > 0 && mode == coverageModeReport {
		_, _ = fmt.Fprintf(stdout, "::warning::Coverage fell below the baseline in: %s\n", strings.Join(below, ", "))
	}
	if csharp == nil {
		return
	}
	_, _ = fmt.Fprint(stdout, formatCSharpCoverageSection(*csharp))
	if csharp.belowBaseline() {
		_, _ = fmt.Fprint(stdout, formatCSharpCoverageWarning(*csharp))
	}
}

func formatOptionalCSharpSection(csharp *csharpCoverageResult) string {
	if csharp == nil {
		return ""
	}
	return formatCSharpCoverageSection(*csharp)
}

// ParseGoCoverProfile totals the statements of a `go test -coverprofile` file, leaving out blocks
// whose file path contains any of the exclude patterns.
func ParseGoCoverProfile(reader io.Reader, exclude []string) (GoCoverageTotals, error) {
	scanner := bufio.NewScanner(reader)
	if !scanner.Scan() {
		if err := scanner.Err(); err != nil {
			return GoCoverageTotals{}, err
		}
		return GoCoverageTotals{}, errors.New("profile is empty")
	}
	if !goCoverModes[strings.TrimSpace(scanner.Text())] {
		return GoCoverageTotals{}, fmt.Errorf("profile does not start with a go test mode line: %q", scanner.Text())
	}

	blocks := map[string]goCoverBlock{}
	for scanner.Scan() {
		line := strings.TrimSpace(scanner.Text())
		if line == "" {
			continue
		}

		key, block, err := parseGoCoverLine(line)
		if err != nil {
			return GoCoverageTotals{}, err
		}
		if pathMatchesAny(key, exclude) {
			continue
		}

		// Why merge: a block appears once per test binary that loaded it (for example under
		// -coverpkg), and it is covered when any of those runs covered it.
		existing := blocks[key]
		blocks[key] = goCoverBlock{statements: block.statements, covered: existing.covered || block.covered}
	}
	if err := scanner.Err(); err != nil {
		return GoCoverageTotals{}, err
	}

	totals := GoCoverageTotals{}
	for _, block := range blocks {
		totals.Statements += block.statements
		if block.covered {
			totals.Covered += block.statements
		}
	}
	return totals, nil
}

// goCoverLinePattern is the block line go test -coverprofile writes:
// "file:startLine.startCol,endLine.endCol numStmts count", all counts non-negative.
var goCoverLinePattern = regexp.MustCompile(`^(.+:\d+\.\d+,\d+\.\d+) (\d+) (\d+)$`)

// parseGoCoverLine splits a block line into the block key and its counts, rejecting anything
// go test would not write so a damaged profile cannot inflate the figure the gate compares.
func parseGoCoverLine(line string) (string, goCoverBlock, error) {
	match := goCoverLinePattern.FindStringSubmatch(line)
	if match == nil {
		return "", goCoverBlock{}, fmt.Errorf("malformed profile line %q", line)
	}

	statements, err := strconv.Atoi(match[2])
	if err != nil {
		return "", goCoverBlock{}, fmt.Errorf("malformed statement count in %q", line)
	}
	count, err := strconv.Atoi(match[3])
	if err != nil {
		return "", goCoverBlock{}, fmt.Errorf("malformed hit count in %q", line)
	}
	return match[1], goCoverBlock{statements: statements, covered: count > 0}, nil
}

func pathMatchesAny(blockKey string, patterns []string) bool {
	path := blockKey[:strings.LastIndex(blockKey, ":")]
	for _, pattern := range patterns {
		if strings.Contains(path, pattern) {
			return true
		}
	}
	return false
}

func readCoverageBaseline(path string) (coverageBaseline, error) {
	content, err := os.ReadFile(path)
	if err != nil {
		return coverageBaseline{}, err
	}

	var baseline coverageBaseline
	if err := json.Unmarshal(content, &baseline); err != nil {
		return coverageBaseline{}, err
	}
	if len(baseline.Go.Modules) == 0 {
		return coverageBaseline{}, errors.New("baseline lists no Go modules")
	}
	for name, figure := range baseline.Go.Modules {
		if figure == nil {
			return coverageBaseline{}, fmt.Errorf("baseline for module %q has no figure", name)
		}
		if *figure < 0 || *figure > 100 {
			return coverageBaseline{}, fmt.Errorf("baseline for module %q is %v, outside 0 to 100", name, *figure)
		}
	}
	return baseline, nil
}

// measureGoModules requires the profiles and the baseline to name the same modules, so a module
// whose tests were not wired into the run fails instead of silently dropping out of the gate.
func measureGoModules(baseline coverageBaseline, profiles map[string]string) ([]coverageModuleResult, error) {
	for name := range profiles {
		if _, ok := baseline.Go.Modules[name]; !ok {
			return nil, fmt.Errorf("profile given for module %q, which the baseline does not list", name)
		}
	}

	names := make([]string, 0, len(baseline.Go.Modules))
	for name := range baseline.Go.Modules {
		names = append(names, name)
	}
	sort.Strings(names)

	results := make([]coverageModuleResult, 0, len(names))
	for _, name := range names {
		path, ok := profiles[name]
		if !ok {
			return nil, fmt.Errorf("no profile given for module %q", name)
		}

		totals, err := readGoCoverProfile(path, baseline.Go.Exclude)
		if err != nil {
			return nil, fmt.Errorf("module %q: %w", name, err)
		}
		if totals.Statements == 0 {
			return nil, fmt.Errorf("module %q has no statements left to measure", name)
		}

		results = append(results, coverageModuleResult{
			Name:     name,
			Percent:  float64(totals.Covered) * 100 / float64(totals.Statements),
			Baseline: *baseline.Go.Modules[name],
		})
	}
	return results, nil
}

// CollectGoCoverProfiles maps each <module>.out file in dir to its module name. Reading the whole
// directory, rather than a list kept elsewhere, makes a module newly wired into the test script
// reach the baseline comparison and fail until the baseline lists it.
func CollectGoCoverProfiles(dir string) (map[string]string, error) {
	entries, err := os.ReadDir(dir)
	if err != nil {
		return nil, err
	}

	profiles := map[string]string{}
	for _, entry := range entries {
		name, ok := strings.CutSuffix(entry.Name(), ".out")
		if !ok || entry.IsDir() {
			continue
		}
		profiles[name] = filepath.Join(dir, entry.Name())
	}
	return profiles, nil
}

func readGoCoverProfile(path string, exclude []string) (GoCoverageTotals, error) {
	file, err := os.Open(path)
	if err != nil {
		return GoCoverageTotals{}, err
	}
	defer func() { _ = file.Close() }()
	return ParseGoCoverProfile(file, exclude)
}

func modulesBelowBaseline(results []coverageModuleResult) []string {
	below := []string{}
	for _, result := range results {
		if result.Percent < result.Baseline-coverageRoundingTolerance {
			below = append(below, result.Name)
		}
	}
	return below
}

func formatCoverageTable(results []coverageModuleResult) string {
	var builder strings.Builder
	builder.WriteString("| Module | Coverage | Baseline | Note |\n|---|---|---|---|\n")
	for _, result := range results {
		_, _ = fmt.Fprintf(
			&builder,
			"| %s | %s%% | %s%% | %s |\n",
			result.Name,
			formatCoveragePercent(floorToOneDecimal(result.Percent)),
			formatCoveragePercent(result.Baseline),
			coverageNote(result))
	}
	return builder.String()
}

func coverageNote(result coverageModuleResult) string {
	if result.Percent < result.Baseline-coverageRoundingTolerance {
		return "below baseline"
	}
	if result.Percent >= result.Baseline+coverageRaiseHintMargin {
		return "raise baseline to " + formatCoveragePercent(floorToOneDecimal(result.Percent))
	}
	return ""
}

func formatCoveragePercent(value float64) string {
	return strconv.FormatFloat(value, 'f', 1, 64)
}

// floorToOneDecimal truncates rather than rounds, so a shown or suggested figure never exceeds
// what was measured and a baseline raised to it cannot fail the same tree. The epsilon keeps a
// value such as 72.5 from flooring to 72.4 through float error.
func floorToOneDecimal(value float64) float64 {
	return math.Floor(value*10+1e-9) / 10
}

func appendCoverageSummary(path string, markdown string) error {
	if path == "" {
		return nil
	}

	file, err := os.OpenFile(path, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o600)
	if err != nil {
		return err
	}
	_, writeErr := io.WriteString(file, markdown)
	return errors.Join(writeErr, file.Close())
}

func writeCoverageMarkdown(path string, markdown string) error {
	if path == "" {
		return nil
	}
	return os.WriteFile(path, []byte(markdown), 0o600)
}
