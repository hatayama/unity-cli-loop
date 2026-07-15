package main

import (
	"flag"
	"fmt"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	sarifPath := flag.String("sarif", "", "path to the CodeQL SARIF report")
	flag.Parse()
	if *sarifPath == "" {
		fmt.Fprintln(os.Stderr, "check-codeql-sarif: --sarif is required")
		os.Exit(1)
	}
	if err := automation.ValidateCodeQLSARIFFile(*sarifPath); err != nil {
		fmt.Fprintln(os.Stderr, "check-codeql-sarif:", err)
		os.Exit(1)
	}
	fmt.Println("CodeQL SARIF guard passed.")
}
