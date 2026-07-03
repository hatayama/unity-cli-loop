package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/project-runner/internal/projectrunner"
)

func main() {
	os.Exit(projectrunner.RunProjectLocal(context.Background(), os.Args[1:], os.Stdout, os.Stderr))
}
