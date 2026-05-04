package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/app"
)

func main() {
	os.Exit(app.RunLauncher(context.Background(), os.Args[1:], os.Stdout, os.Stderr))
}
