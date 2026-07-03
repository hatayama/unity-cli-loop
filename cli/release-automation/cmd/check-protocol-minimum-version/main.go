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
	verifyRelease := flag.Bool("verify-release", false, "verify the minimum CLI release is published and advertises the required protocol")
	verifyReleaseRef := flag.String("ref", "", "git ref to read the minimum CLI release requirement from when --verify-release is set")
	flag.Parse()

	if *verifyRelease {
		os.Exit(automation.RunMinimumCliReleaseProtocolCheck(
			context.Background(),
			os.Stdout,
			os.Stderr,
			*verifyReleaseRef))
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
