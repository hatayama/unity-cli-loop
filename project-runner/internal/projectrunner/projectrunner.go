package projectrunner

import (
	"context"
	"io"
)

func Run(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) int {
	return RunProjectLocal(ctx, args, stdout, stderr)
}
