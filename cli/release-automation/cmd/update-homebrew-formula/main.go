package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	os.Exit(automation.RunUpdateHomebrewFormula(context.Background(), os.Stdout, os.Stderr, os.Args[1:]))
}
