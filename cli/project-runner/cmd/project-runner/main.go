package main

import (
	"context"
	"os"
	"os/signal"
	"syscall"

	"github.com/hatayama/unity-cli-loop/project-runner/internal/projectrunner"
)

func main() {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	context.AfterFunc(ctx, stop)
	os.Exit(projectrunner.RunProjectLocal(ctx, os.Args[1:], os.Stdout, os.Stderr))
}
