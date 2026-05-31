package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/cli/internal/automation"
)

func main() {
	os.Exit(automation.RunMinimumVersionWarning(context.Background(), os.Stdout, os.Stderr))
}
