// plan-unity-package-signing prints GITHUB_OUTPUT lines that tell the signing
// workflow whether the Unity package release still lacks its signed tarball,
// and which tag, version, and release asset name the signing run works with.
package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	os.Exit(automation.RunPlanUnityPackageSigning(context.Background(), os.Stdout, os.Stderr, os.Args[1:]))
}
