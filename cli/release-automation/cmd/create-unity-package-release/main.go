// create-unity-package-release creates and publishes the Unity package release
// with its signed tarball attached in one step, after creating its tag at the
// release commit. Every reason not to create it is found before the first
// write, and a rerun after the release is published does nothing.
package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	os.Exit(automation.RunCreateUnityPackageRelease(context.Background(), os.Stdout, os.Stderr, os.Args[1:]))
}
