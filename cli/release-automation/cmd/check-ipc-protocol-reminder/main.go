package main

import (
	"context"
	"flag"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	baseRef := flag.String("base", "", "base git ref to compare from")
	headRef := flag.String("head", "HEAD", "head git ref to compare to")
	flag.Parse()

	os.Exit(automation.RunIPCProtocolReminder(
		context.Background(),
		os.Stdout,
		os.Stderr,
		automation.IPCProtocolReminderConfig{
			BaseRef:         *baseRef,
			HeadRef:         *headRef,
			StepSummaryPath: os.Getenv("GITHUB_STEP_SUMMARY"),
		}))
}
