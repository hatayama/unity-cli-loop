package automation

import (
	"bufio"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math"
	"os"
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
}

// GoCoverageTotals is the statement count of a profile after exclusions.
type GoCoverageTotals struct {
	Statements int
	Covered    int
}

type coverageBaseline struct {
	Go struct {
		Exclude []string           `json:"exclude"`
		Modules map[string]float64 `json:"modules"`
	} `json:"go"`
}

type coverageModuleResult struct {
	Name     string
	Percent  float64
	Baseline float64
}

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

	table := formatCoverageTable(results)
	_, _ = fmt.Fprint(stdout, table)
	if err := appendCoverageSummary(options.SummaryPath, table); err != nil {
		_, _ = fmt.Fprintf(stderr, "write step summary: %v\n", err)
		return 2
	}

	below := modulesBelowBaseline(results)
	if len(below) == 0 {
		return 0
	}

	if options.Mode == coverageModeReport {
		_, _ = fmt.Fprintf(stdout, "::warning::Coverage fell below the baseline in: %s\n", strings.Join(below, ", "))
		return 0
	}

	_, _ = fmt.Fprintf(stderr, "Coverage fell below the baseline in: %s. Add tests, or lower the figure in the baseline file with a reason in the pull request.\n", strings.Join(below, ", "))
	return 1
}

// ParseGoCoverProfile totals the statements of a `go test -coverprofile` file, leaving out blocks
// whose file path contains any of the exclude patterns.
func ParseGoCoverProfile(reader io.Reader, exclude []string) (GoCoverageTotals, error) {
	scanner := bufio.NewScanner(reader)
	if !scanner.Scan() || !strings.HasPrefix(scanner.Text(), "mode:") {
		if err := scanner.Err(); err != nil {
			return GoCoverageTotals{}, err
		}
		return GoCoverageTotals{}, errors.New("profile does not start with a mode line")
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

// parseGoCoverLine splits "file:start,end numStmts count" into the block key and its counts.
func parseGoCoverLine(line string) (string, goCoverBlock, error) {
	fields := strings.Fields(line)
	if len(fields) != 3 || !strings.Contains(fields[0], ":") {
		return "", goCoverBlock{}, fmt.Errorf("malformed profile line %q", line)
	}

	statements, err := strconv.Atoi(fields[1])
	if err != nil {
		return "", goCoverBlock{}, fmt.Errorf("malformed statement count in %q", line)
	}
	count, err := strconv.Atoi(fields[2])
	if err != nil {
		return "", goCoverBlock{}, fmt.Errorf("malformed hit count in %q", line)
	}
	return fields[0], goCoverBlock{statements: statements, covered: count > 0}, nil
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
			Baseline: baseline.Go.Modules[name],
		})
	}
	return results, nil
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

func appendCoverageSummary(path string, table string) error {
	if path == "" {
		return nil
	}

	file, err := os.OpenFile(path, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o600)
	if err != nil {
		return err
	}
	_, writeErr := io.WriteString(file, "## Go test coverage\n\n"+table+"\n")
	return errors.Join(writeErr, file.Close())
}
