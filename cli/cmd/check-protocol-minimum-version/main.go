package main

import (
	"context"
	"flag"
	"os"

	"github.com/hatayama/unity-cli-loop/cli/internal/automation"
)

func main() {
	baseRef := flag.String("base", "", "base git ref to compare from")
	headRef := flag.String("head", "HEAD", "head git ref to compare to")
	verifyRelease := flag.Bool("verify-release", false, "verify the minimum CLI release tag advertises the required protocol")
	flag.Parse()

	if *verifyRelease {
		os.Exit(automation.RunMinimumCliReleaseProtocolCheck(
			context.Background(),
			os.Stdout,
			os.Stderr))
	}

	os.Exit(automation.RunProtocolMinimumVersionGuard(
		context.Background(),
		os.Stdout,
		os.Stderr,
		automation.ProtocolMinimumVersionGuardConfig{
			BaseRef: *baseRef,
			HeadRef: *headRef,
		}))
}
