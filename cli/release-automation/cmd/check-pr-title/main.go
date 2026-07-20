package main

import (
	"fmt"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	prTitle, isSet := os.LookupEnv("PR_TITLE")
	if !isSet || prTitle == "" {
		fmt.Fprintln(os.Stderr, "PR_TITLE is not set")
		os.Exit(1)
	}

	os.Exit(automation.RunPRTitleGuard(os.Stdout, os.Stderr, prTitle))
}
