package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/cli"
)

func main() {
	os.Exit(cli.RunLauncher(context.Background(), os.Args[1:], os.Stdout, os.Stderr))
}
