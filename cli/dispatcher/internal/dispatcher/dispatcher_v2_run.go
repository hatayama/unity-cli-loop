package dispatcher

import (
	"context"
	"errors"
	"io"
	"os"
	"os/exec"
	"runtime"
)

// runDispatcherV2CLI installs and executes the V2 CLI while preserving the original command arguments.
// Why: the dispatcher must select the CLI generation before command parsing can change user-supplied arguments.
func runDispatcherV2CLI(ctx context.Context, version string, args []string, stdout io.Writer, stderr io.Writer) (int, error) {
	cacheRoot, err := dispatcherCacheRoot(runtime.GOOS)
	if err != nil {
		return 0, err
	}
	installPath, err := installDispatcherV2CLI(ctx, cacheRoot, version, runtime.GOOS, stderr, defaultDispatcherV2InstallDeps())
	if err != nil {
		return 0, err
	}
	entrypoint, err := resolveDispatcherV2CLIEntrypoint(installPath)
	if err != nil {
		return 0, err
	}
	nodePath, err := defaultDispatcherV2NodePath()
	if err != nil {
		return 0, err
	}
	_, err = io.WriteString(stderr, "uloop: executing in V2 mode\n")
	if err != nil {
		return 0, err
	}
	command := exec.CommandContext(ctx, nodePath, append([]string{entrypoint}, args...)...)
	command.Stdout = stdout
	command.Stderr = stderr
	command.Stdin = os.Stdin
	command.Env = os.Environ()
	if err := command.Run(); err != nil {
		var exitError *exec.ExitError
		if errors.As(err, &exitError) {
			return exitError.ExitCode(), nil
		}
		return 0, err
	}
	return 0, nil
}
