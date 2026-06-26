package main

import (
	"context"
	"flag"
	"os"

	"github.com/hatayama/unity-cli-loop/cli/internal/automation"
)

func main() {
	ref := flag.String("ref", "", "git ref to read dispatcher minimum metadata from")
	flag.Parse()

	os.Exit(automation.RunDispatcherMinimumVersionCheck(
		context.Background(),
		os.Stdout,
		os.Stderr,
		*ref))
}
