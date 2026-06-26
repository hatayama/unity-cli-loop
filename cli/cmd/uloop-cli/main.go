package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/cli/internal/cli"
)

func main() {
	os.Exit(cli.RunProjectLocal(context.Background(), os.Args[1:], os.Stdout, os.Stderr))
}
