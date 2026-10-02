package main

import (
	"flag"
	"fmt"
	"os"
	"strings"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

// goProfileFlags collects repeated --go-profile <module>=<path> values.
type goProfileFlags map[string]string

func (profiles goProfileFlags) String() string {
	return fmt.Sprint(map[string]string(profiles))
}

func (profiles goProfileFlags) Set(value string) error {
	name, path, ok := strings.Cut(value, "=")
	if !ok || name == "" || path == "" {
		return fmt.Errorf("expected <module>=<path>, got %q", value)
	}
	if _, exists := profiles[name]; exists {
		return fmt.Errorf("module %q given twice", name)
	}
	profiles[name] = path
	return nil
}

func main() {
	profiles := goProfileFlags{}
	baseline := flag.String("baseline", "coverage-baseline.json", "path to the coverage baseline file")
	mode := flag.String("mode", "report", "gate exits 1 when a module falls below its baseline; report only warns")
	flag.Var(profiles, "go-profile", "<module>=<path> of a go test -coverprofile output (repeatable)")
	flag.Parse()

	os.Exit(automation.RunCoverageReport(os.Stdout, os.Stderr, automation.CoverageReportOptions{
		BaselinePath: *baseline,
		GoProfiles:   profiles,
		Mode:         *mode,
		SummaryPath:  os.Getenv("GITHUB_STEP_SUMMARY"),
	}))
}
