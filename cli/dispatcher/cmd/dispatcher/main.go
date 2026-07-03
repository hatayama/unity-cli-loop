package main

import (
	"context"
	"os"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/dispatcher"
)

func main() {
	os.Exit(dispatcher.RunDispatcher(context.Background(), os.Args[1:], os.Stdout, os.Stderr))
}
