// open-dispatcher-pin-pr stamps the Unity package pin from a published stable
// dispatcher release and opens the pull request that carries the stamp.
package main

import (
	"context"
	"os"
	"time"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

// The command only stamps, pushes, and dispatches workflows; a longer run means
// a hung network call rather than useful work.
const openDispatcherPinPRTimeout = 15 * time.Minute

func main() {
	ctx, cancel := context.WithTimeout(context.Background(), openDispatcherPinPRTimeout)
	defer cancel()
	os.Exit(automation.RunOpenDispatcherPinPR(ctx, os.Stdout, os.Stderr, os.Args[1:]))
}
