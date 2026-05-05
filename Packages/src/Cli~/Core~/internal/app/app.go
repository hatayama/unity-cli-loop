package app

import (
	"context"
	"io"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Core/internal/presentation/cli"
)

func RunProjectLocal(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) int {
	return cli.RunProjectLocal(ctx, args, stdout, stderr)
}
