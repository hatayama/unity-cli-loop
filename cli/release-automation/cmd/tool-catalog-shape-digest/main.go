// tool-catalog-shape-digest prints the description-stripped SHA-256 of an
// embedded tool catalog so stamp scripts can ignore wording-only regenerations.
package main

import (
	"flag"
	"fmt"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	catalogPath := flag.String("path", "", "path to the embedded tool catalog JSON")
	flag.Parse()

	if *catalogPath == "" {
		fmt.Fprintln(os.Stderr, "-path is required")
		os.Exit(1)
	}

	content, err := os.ReadFile(*catalogPath)
	if err != nil {
		fmt.Fprintf(os.Stderr, "failed to read %s: %v\n", *catalogPath, err)
		os.Exit(1)
	}

	digest, err := automation.ToolCatalogShapeDigest(content)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}

	fmt.Println(digest)
}
