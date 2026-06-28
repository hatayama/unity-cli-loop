package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/cli/internal/dispatcher"
)

func main() {
	os.Exit(dispatcher.Run(context.Background(), os.Args[1:], os.Stdout, os.Stderr))
}
