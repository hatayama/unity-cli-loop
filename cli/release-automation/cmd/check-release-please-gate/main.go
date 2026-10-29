package main

import (
	"context"
	"os"
	"time"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

const releasePleaseGateCheckTimeout = 2 * time.Minute

func main() {
	ctx, cancel := context.WithTimeout(context.Background(), releasePleaseGateCheckTimeout)
	exitCode := automation.RunReleasePleaseGateCheck(ctx, os.Stdout, os.Stderr, os.Args[1:])
	cancel()
	os.Exit(exitCode)
}
