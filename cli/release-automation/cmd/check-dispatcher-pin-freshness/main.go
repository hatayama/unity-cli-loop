package main

import (
	"context"
	"os"
	"time"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

const dispatcherPinFreshnessCheckTimeout = 2 * time.Minute

func main() {
	ctx, cancel := context.WithTimeout(context.Background(), dispatcherPinFreshnessCheckTimeout)
	exitCode := automation.RunDispatcherPinFreshnessCheck(ctx, os.Stdout, os.Stderr, os.Args[1:])
	cancel()
	os.Exit(exitCode)
}
