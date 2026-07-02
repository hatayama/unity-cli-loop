package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/project-runner/internal/projectrunner"
)

func main() {
	os.Exit(projectrunner.Run(context.Background(), os.Args[1:], os.Stdout, os.Stderr))
}
