package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/automation"
)

func main() {
	os.Exit(automation.RunReleasePleasePRChecks(context.Background(), os.Stdout, os.Stderr))
}
