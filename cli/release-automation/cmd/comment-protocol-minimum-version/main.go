package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	os.Exit(automation.RunProtocolMinimumVersionComment(
		context.Background(),
		os.Stdout,
		os.Stderr))
}
