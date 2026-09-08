// merge-package-release-pr merges the open Unity package release pull request
// once its head records the dispatcher release that was just published, which
// is the first commit from which the package can ship the current dispatcher.
package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	os.Exit(automation.RunMergePackageReleasePR(context.Background(), os.Stdout, os.Stderr, os.Args[1:]))
}
