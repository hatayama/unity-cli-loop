// stage-signed-unity-package checks the tarball `upm pack` produced and renames
// it to the release asset name OpenUPM republishes, refusing tarballs that are
// unsigned, mislabeled, or missing package contents.
package main

import (
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	os.Exit(automation.RunStageSignedUnityPackage(os.Stdout, os.Stderr, os.Args[1:]))
}
