package main

import (
	"context"
	"os"
	"time"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

const dispatcherMinimumVersionCheckTimeout = 2 * time.Minute

func main() {
	ctx, cancel := context.WithTimeout(context.Background(), dispatcherMinimumVersionCheckTimeout)
	exitCode := automation.RunDispatcherMinimumVersionCheck(
		ctx,
		os.Stdout,
		os.Stderr,
		"")
	cancel()
	os.Exit(exitCode)
}
