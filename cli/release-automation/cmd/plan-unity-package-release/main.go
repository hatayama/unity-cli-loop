// plan-unity-package-release prints GITHUB_OUTPUT lines that tell the Unity
// package release workflow whether to sign the manifest version of the package
// and whether to create its release afterwards. A release is created only when
// none is published yet and the readiness check names its release commit; a
// published release is never changed, because it is immutable.
package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	os.Exit(automation.RunPlanUnityPackageRelease(context.Background(), os.Stdout, os.Stderr, os.Args[1:]))
}
