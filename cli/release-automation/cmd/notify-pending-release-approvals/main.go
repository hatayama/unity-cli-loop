package main

import (
	"context"
	"fmt"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	repository, isSet := os.LookupEnv("GITHUB_REPOSITORY")
	if !isSet || repository == "" {
		fmt.Fprintln(os.Stderr, "GITHUB_REPOSITORY is not set")
		os.Exit(1)
	}

	os.Exit(automation.RunNotifyPendingReleaseApprovals(context.Background(), os.Stdout, os.Stderr, repository))
}
