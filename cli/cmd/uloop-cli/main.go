package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/cli/internal/projectcli"
)

func main() {
	os.Exit(projectcli.Run(context.Background(), os.Args[1:], os.Stdout, os.Stderr))
}
