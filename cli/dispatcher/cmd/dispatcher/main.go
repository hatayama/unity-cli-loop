package main

import (
	"context"
	"os"
	"os/signal"
	"syscall"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/dispatcher"
)

func main() {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	os.Exit(dispatcher.RunDispatcher(ctx, os.Args[1:], os.Stdout, os.Stderr))
}
