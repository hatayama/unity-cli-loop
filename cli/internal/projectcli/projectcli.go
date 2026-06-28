package projectcli

import (
	"context"
	"io"

	"github.com/hatayama/unity-cli-loop/cli/internal/cli"
)

func Run(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) int {
	return cli.RunProjectLocal(ctx, args, stdout, stderr)
}
