// push-dispatcher-pin stamps the Unity package pin from a published stable
// dispatcher release and pushes the stamp commit straight to the base branch.
package main

import (
	"context"
	"os"
	"time"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

// The command only stamps, verifies, and pushes one commit; a longer run means
// a hung network call rather than useful work.
const pushDispatcherPinTimeout = 15 * time.Minute

func main() {
	ctx, cancel := context.WithTimeout(context.Background(), pushDispatcherPinTimeout)
	defer cancel()
	os.Exit(automation.RunPushDispatcherPin(ctx, os.Stdout, os.Stderr, os.Args[1:]))
}
