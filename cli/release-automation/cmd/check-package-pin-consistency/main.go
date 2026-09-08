// check-package-pin-consistency fails when a ref would release the Unity
// package while its project-runner-pin.json still records a different
// dispatcher than the ref's release manifest publishes, which is what makes a
// released package install a one-generation-old dispatcher.
package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	os.Exit(automation.RunPackagePinConsistencyCheck(context.Background(), os.Stdout, os.Stderr, os.Args[1:]))
}
