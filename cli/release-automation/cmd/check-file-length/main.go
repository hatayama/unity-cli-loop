package main

import (
	"flag"
	"os"
	"strings"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	root := flag.String("root", ".", "repository root to scan")
	maxLength := flag.Int("max-length", automation.DefaultMaxFileLength, "maximum allowed SLOC per file")
	// Parsed as a string so the POSIX wrapper can pass `--fail-on-exceeded false`.
	// Go's bool flags treat a bare `--fail-on-exceeded` as true and would ignore the
	// following `false` token, which would flip report-only runs into fail mode.
	failOnExceeded := flag.String("fail-on-exceeded", "false", "exit 1 when any file exceeds the limit (true/false)")
	flag.Parse()

	os.Exit(automation.RunFileLengthCheck(os.Stdout, os.Stderr, automation.FileLengthCheckOptions{
		Root:           *root,
		MaxLength:      *maxLength,
		FailOnExceeded: failOnExceededEnabled(*failOnExceeded),
	}))
}

func failOnExceededEnabled(value string) bool {
	return strings.EqualFold(strings.TrimSpace(value), "true")
}
