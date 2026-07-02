package dispatcher

import (
	"context"
	"io"
)

func Run(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) int {
	return RunDispatcher(ctx, args, stdout, stderr)
}
