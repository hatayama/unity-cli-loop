package app

import (
	"context"
	"io"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/presentation"
)

func RunProjectLocal(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) int {
	return presentation.RunProjectLocal(ctx, args, stdout, stderr)
}
