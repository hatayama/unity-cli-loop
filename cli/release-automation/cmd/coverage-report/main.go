package main

import (
	"flag"
	"fmt"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	baseline := flag.String("baseline", "coverage-baseline.json", "path to the coverage baseline file")
	mode := flag.String("mode", "report", "gate exits 1 when a module falls below its baseline; report only warns")
	coverageDir := flag.String("go-coverage-dir", "", "directory of <module>.out go test -coverprofile outputs")
	csharpSummary := flag.String("csharp-summary", "", "ReportGenerator Summary.json to report C# coverage from; C# warns but never gates")
	markdownOut := flag.String("markdown-out", "", "file to write the coverage Markdown to, without workflow commands")
	flag.Parse()

	profiles, err := automation.CollectGoCoverProfiles(*coverageDir)
	if err != nil {
		_, _ = fmt.Fprintf(os.Stderr, "read Go coverage profiles: %v\n", err)
		os.Exit(2)
	}

	os.Exit(automation.RunCoverageReport(os.Stdout, os.Stderr, automation.CoverageReportOptions{
		BaselinePath: *baseline,
		GoProfiles:   profiles,
		Mode:         *mode,
		SummaryPath:  os.Getenv("GITHUB_STEP_SUMMARY"),

		CSharpSummaryPath: *csharpSummary,
		MarkdownOutPath:   *markdownOut,
	}))
}
