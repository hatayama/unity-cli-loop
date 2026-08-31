package dispatcher

import (
	"context"
	"errors"
	"io"
	"os"
	"os/exec"
	"runtime"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

func tryRunDetectedDispatcherV2Project(
	ctx context.Context,
	projectRoot string,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
	deps dispatcherRunDeps,
) (bool, int) {
	if len(args) > 0 && args[0] == clicore.LaunchCommandName {
		return false, 0
	}
	v2Project, err := detectV2DispatcherProject(projectRoot)
	if err != nil || !v2Project.IsV2 {
		return false, 0
	}
	if v2Project.PackageVersion == "" {
		clierrors.WriteErrorEnvelope(stderr, dispatcherV2ProjectDetectedError(projectRoot, v2Project, nil))
		return true, 1
	}

	code, err := deps.runV2CLI(ctx, v2Project.PackageVersion, args, stdout, stderr)
	if err == nil {
		return true, code
	}
	clierrors.WriteErrorEnvelope(stderr, dispatcherV2ProjectDetectedError(projectRoot, v2Project, err))
	return true, 1
}

func dispatcherV2ProjectDetectedError(projectRoot string, v2Project dispatcherV2Project, executionErr error) clierrors.CLIError {
	if len(v2Project.PackageVersionCandidates) > 0 {
		nextActions := []string{
			"Open the Unity project once so Unity Package Manager can refresh `Packages/packages-lock.json`, then retry the command.",
			"As a last resort, run `npx uloop-cli@2 <command>` from this project.",
		}
		if v2Project.AmbiguousEmbedded {
			nextActions = []string{
				"Remove the duplicate embedded package directories under `Packages/` so only one copy of the V2 package remains, then retry the command.",
				"As a last resort, run `npx uloop-cli@2 <command>` from this project.",
			}
		}
		return clierrors.CLIError{
			ErrorCode:   clierrors.ErrorCodeV2ProjectDetected,
			Phase:       clierrors.ErrorPhaseProjectResolve,
			Message:     "This Unity project uses uloop V2, but its package version could not be resolved unambiguously.",
			Retryable:   true,
			SafeToRetry: true,
			ProjectRoot: projectRoot,
			NextActions: nextActions,
			Details: map[string]any{
				"V2PackageVersionCandidates": v2Project.PackageVersionCandidates,
			},
		}
	}

	details := map[string]any{
		"V2PackageVersion": v2Project.PackageVersion,
	}
	if executionErr != nil {
		details["Cause"] = executionErr.Error()
	}
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeV2ProjectDetected,
		Phase:       clierrors.ErrorPhaseProjectResolve,
		Message:     "This Unity project uses uloop V2 and requires Node.js 22 or later.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		NextActions: []string{
			"Install Node.js 22 or later, then retry the command.",
			"As a last resort, run `npx uloop-cli@" + v2Project.PackageVersion + " <command>` from this project.",
		},
		Details: details,
	}
}

// dispatcherV2ModeNotice builds the stderr notice announcing delegation to the V2 CLI.
// Why: delegation happens implicitly, so the executed CLI generation and version are otherwise invisible to the caller.
func dispatcherV2ModeNotice(version string) string {
	return "uloop: executing in V2 mode (" + dispatcherV2CLIPackageName + "@" + version + ")\n"
}

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
	_, err = io.WriteString(stderr, dispatcherV2ModeNotice(version))
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
