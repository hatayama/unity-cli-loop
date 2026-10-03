package automation

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"sort"
	"strings"
)

const csharpCoverageHeading = "## C# test coverage (EditMode, Unity 2022.3)"

// csharpCoverageBaseline is the csharp section of the baseline file. Pointers tell a missing or
// null key apart from an empty list or a 0% figure, both of which are valid.
type csharpCoverageBaseline struct {
	Exclude      *[]string `json:"exclude"`
	LineCoverage *float64  `json:"lineCoverage"`
}

// csharpCoverageSummary is the part of a ReportGenerator Summary.json the report reads. The
// per-assembly percentage and the overall summary are ignored because the measured scope is
// recomputed from line counts after exclusions.
type csharpCoverageSummary struct {
	Coverage struct {
		Assemblies []csharpCoverageSummaryAssembly `json:"assemblies"`
	} `json:"coverage"`
}

// csharpCoverageSummaryAssembly uses pointers so a missing line count is rejected instead of read
// as 0, which would silently lower the scope figure. ReportGenerator always writes these keys, so
// a missing one means its format changed.
type csharpCoverageSummaryAssembly struct {
	Name      string `json:"name"`
	Covered   *int   `json:"coveredlines"`
	Coverable *int   `json:"coverablelines"`
}

type csharpCoverageAssembly struct {
	Name      string
	Covered   int
	Coverable int
}

// csharpCoverageResult is the measured C# scope against its baseline, with the assemblies split
// into the measured scope and the excluded ones, each sorted for the report.
type csharpCoverageResult struct {
	Scoped    []csharpCoverageAssembly
	Excluded  []csharpCoverageAssembly
	Covered   int
	Coverable int
	Percent   float64
	Baseline  float64
}

func (result csharpCoverageResult) belowBaseline() bool {
	return result.Percent < result.Baseline-coverageRoundingTolerance
}

// measureCSharpCoverage validates the csharp baseline section and the Summary.json, then totals the
// assemblies left after exclusions. The section is parsed here rather than with the Go baseline so
// a broken C# entry cannot fail a run that measures Go alone.
func measureCSharpCoverage(section json.RawMessage, summaryPath string) (csharpCoverageResult, error) {
	baseline, err := parseCSharpCoverageBaseline(section)
	if err != nil {
		return csharpCoverageResult{}, err
	}
	assemblies, err := readCSharpCoverageSummary(summaryPath)
	if err != nil {
		return csharpCoverageResult{}, err
	}

	result, err := splitCSharpAssemblies(assemblies, *baseline.Exclude)
	if err != nil {
		return csharpCoverageResult{}, err
	}
	if result.Coverable == 0 {
		return csharpCoverageResult{}, errors.New("measured C# scope has no coverable lines")
	}
	result.Percent = float64(result.Covered) * 100 / float64(result.Coverable)
	result.Baseline = *baseline.LineCoverage
	return result, nil
}

func parseCSharpCoverageBaseline(section json.RawMessage) (csharpCoverageBaseline, error) {
	if len(section) == 0 || string(section) == "null" {
		return csharpCoverageBaseline{}, errors.New("baseline has no csharp section")
	}

	var baseline csharpCoverageBaseline
	if err := json.Unmarshal(section, &baseline); err != nil {
		return csharpCoverageBaseline{}, fmt.Errorf("parse csharp baseline: %w", err)
	}
	if baseline.Exclude == nil {
		return csharpCoverageBaseline{}, errors.New("csharp baseline has no exclude list")
	}
	if baseline.LineCoverage == nil {
		return csharpCoverageBaseline{}, errors.New("csharp baseline has no lineCoverage")
	}
	if *baseline.LineCoverage < 0 || *baseline.LineCoverage > 100 {
		return csharpCoverageBaseline{}, fmt.Errorf("csharp lineCoverage is %v, outside 0 to 100", *baseline.LineCoverage)
	}
	return baseline, nil
}

func readCSharpCoverageSummary(path string) ([]csharpCoverageAssembly, error) {
	content, err := os.ReadFile(path)
	if err != nil {
		return nil, fmt.Errorf("read C# coverage summary: %w", err)
	}

	var summary csharpCoverageSummary
	if err := json.Unmarshal(content, &summary); err != nil {
		return nil, fmt.Errorf("parse C# coverage summary: %w", err)
	}
	if len(summary.Coverage.Assemblies) == 0 {
		return nil, errors.New("coverage summary lists no C# assemblies")
	}

	assemblies := make([]csharpCoverageAssembly, 0, len(summary.Coverage.Assemblies))
	for index, entry := range summary.Coverage.Assemblies {
		assembly, err := toCSharpCoverageAssembly(index, entry)
		if err != nil {
			return nil, err
		}
		if err := validateCSharpAssembly(assembly); err != nil {
			return nil, err
		}
		assemblies = append(assemblies, assembly)
	}
	return assemblies, nil
}

func toCSharpCoverageAssembly(index int, entry csharpCoverageSummaryAssembly) (csharpCoverageAssembly, error) {
	if entry.Name == "" {
		return csharpCoverageAssembly{}, fmt.Errorf("assembly %d in the coverage summary has no name", index)
	}
	if entry.Covered == nil {
		return csharpCoverageAssembly{}, fmt.Errorf("assembly %q has no coveredlines", entry.Name)
	}
	if entry.Coverable == nil {
		return csharpCoverageAssembly{}, fmt.Errorf("assembly %q has no coverablelines", entry.Name)
	}
	return csharpCoverageAssembly{Name: entry.Name, Covered: *entry.Covered, Coverable: *entry.Coverable}, nil
}

func validateCSharpAssembly(assembly csharpCoverageAssembly) error {
	if assembly.Coverable < 0 {
		return fmt.Errorf("assembly %q has negative coverable lines", assembly.Name)
	}
	if assembly.Covered < 0 {
		return fmt.Errorf("assembly %q has negative covered lines", assembly.Name)
	}
	if assembly.Covered > assembly.Coverable {
		return fmt.Errorf("assembly %q covers %d of %d coverable lines", assembly.Name, assembly.Covered, assembly.Coverable)
	}
	return nil
}

// splitCSharpAssemblies requires every excluded name to match an assembly, so a misspelled entry
// fails instead of silently leaving the assembly it meant to drop inside the measured scope.
func splitCSharpAssemblies(assemblies []csharpCoverageAssembly, exclude []string) (csharpCoverageResult, error) {
	excluded := map[string]bool{}
	for _, name := range exclude {
		excluded[name] = true
	}

	result := csharpCoverageResult{}
	matched := map[string]bool{}
	for _, assembly := range assemblies {
		if excluded[assembly.Name] {
			matched[assembly.Name] = true
			result.Excluded = append(result.Excluded, assembly)
			continue
		}
		result.Scoped = append(result.Scoped, assembly)
		result.Covered += assembly.Covered
		result.Coverable += assembly.Coverable
	}
	for _, name := range exclude {
		if !matched[name] {
			return csharpCoverageResult{}, fmt.Errorf("csharp exclude names %q, which the summary does not list", name)
		}
	}

	sortCSharpAssembliesByUncovered(result.Scoped)
	sortCSharpAssembliesByUncovered(result.Excluded)
	return result, nil
}

func sortCSharpAssembliesByUncovered(assemblies []csharpCoverageAssembly) {
	sort.SliceStable(assemblies, func(left int, right int) bool {
		leftUncovered := assemblies[left].Coverable - assemblies[left].Covered
		rightUncovered := assemblies[right].Coverable - assemblies[right].Covered
		if leftUncovered != rightUncovered {
			return leftUncovered > rightUncovered
		}
		return assemblies[left].Name < assemblies[right].Name
	})
}

// formatCSharpCoverageSection renders the C# Markdown section. It carries no ::warning:: command
// because the same text becomes the trend issue comment; falling below shows in the first line.
func formatCSharpCoverageSection(result csharpCoverageResult) string {
	shown := floorToOneDecimal(result.Percent)
	var builder strings.Builder
	builder.WriteString(csharpCoverageHeading + "\n\n")
	_, _ = fmt.Fprintf(
		&builder,
		"Measured scope: %s%% (%d / %d lines), baseline %s%% (%s)",
		formatCoveragePercent(shown),
		result.Covered,
		result.Coverable,
		formatCoveragePercent(result.Baseline),
		fmt.Sprintf("%+.1f", shown-result.Baseline))
	if result.belowBaseline() {
		builder.WriteString(", below baseline")
	}
	builder.WriteString("\n\n")
	builder.WriteString(formatCSharpAssemblyTable(result.Scoped))
	builder.WriteString("\n### Excluded from the measured scope\n\n")
	builder.WriteString(formatCSharpAssemblyTable(result.Excluded))
	builder.WriteString("\n")
	return builder.String()
}

func formatCSharpAssemblyTable(assemblies []csharpCoverageAssembly) string {
	var builder strings.Builder
	builder.WriteString("| Assembly | Covered | Coverable | Coverage |\n|---|---|---|---|\n")
	for _, assembly := range assemblies {
		_, _ = fmt.Fprintf(&builder, "| %s | %d | %d | %s |\n", assembly.Name, assembly.Covered, assembly.Coverable, formatCSharpAssemblyPercent(assembly))
	}
	return builder.String()
}

// formatCSharpAssemblyPercent shows "-" for an assembly with no coverable lines rather than
// dividing zero by zero.
func formatCSharpAssemblyPercent(assembly csharpCoverageAssembly) string {
	if assembly.Coverable == 0 {
		return "-"
	}
	return formatCoveragePercent(floorToOneDecimal(float64(assembly.Covered)*100/float64(assembly.Coverable))) + "%"
}

func formatCSharpCoverageWarning(result csharpCoverageResult) string {
	return fmt.Sprintf(
		"::warning::C# coverage fell below the baseline: %s%% against %s%%\n",
		formatCoveragePercent(floorToOneDecimal(result.Percent)),
		formatCoveragePercent(result.Baseline))
}
