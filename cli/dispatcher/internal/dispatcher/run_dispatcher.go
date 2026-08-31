package dispatcher

import (
	"context"
	"errors"
	"io"
	"os"
	"os/exec"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/project"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
)

const (
	dispatcherDisableSelfUpdateEnvName = "ULOOP_DISABLE_SELF_UPDATE"
	dispatcherVersionsDirectoryName    = "versions"
	dispatcherUpdateStateFileName      = "dispatcher-update.json"
	dispatcherProjectPinRelativePath   = ".uloop/project-runner-pin.json"
	dispatcherPackagePinFileName       = "project-runner-pin.json"
	dispatcherUnityPackageName         = "io.github.hatayama.uloopmcp"
	dispatcherRealCLIUnixFileName      = "uloop-project-runner"
	dispatcherRealCLIWindowsFileName   = "uloop-project-runner.exe"
	dispatcherReleaseRepository        = "hatayama/unity-cli-loop"
	dispatcherReleaseBaseURL           = "https://github.com/" + dispatcherReleaseRepository + "/releases/download"
	dispatcherSelfUpdateInterval       = 24 * time.Hour
)

type dispatcherUpdateState struct {
	LastChecked time.Time `json:"lastChecked"`
}

type dispatcherRunDeps struct {
	now        func() time.Time
	runRealCLI func(context.Context, string, []string, io.Writer, io.Writer) int
	runV2CLI   func(context.Context, string, []string, io.Writer, io.Writer) (int, error)
	runUpdate  func(context.Context) (bool, error)
	launch     launchDeps
}

func defaultDispatcherRunDeps() dispatcherRunDeps {
	return dispatcherRunDeps{
		now:        time.Now,
		runRealCLI: runRealCLICommand,
		runV2CLI:   runDispatcherV2CLI,
		runUpdate:  runDispatcherUpdateCommand,
		launch:     defaultLaunchDeps(),
	}
}

func RunDispatcher(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) int {
	return runDispatcherWithDeps(ctx, args, stdout, stderr, defaultDispatcherRunDeps())
}

func runDispatcherWithDeps(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer, deps dispatcherRunDeps) int {
	remainingArgs, projectPath, err := clicore.ParseGlobalProjectPath(args)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{})
		return 1
	}
	if handled, code := tryHandleDispatcherHelpOrVersion(ctx, args, remainingArgs, projectPath, stdout, stderr, deps); handled {
		return code
	}
	if shouldRunInDispatcherProcess(remainingArgs) {
		return runDispatcherProcessCommandWithDeps(ctx, args, remainingArgs, projectPath, stdout, stderr, deps)
	}
	return runPinnedDispatcherProjectRunner(ctx, args, remainingArgs, projectPath, stdout, stderr, deps)
}

func tryHandleDispatcherHelpOrVersion(
	ctx context.Context,
	args []string,
	remainingArgs []string,
	projectPath string,
	stdout io.Writer,
	stderr io.Writer,
	deps dispatcherRunDeps,
) (bool, int) {
	if len(args) != 0 && !clicore.IsHelpRequest(remainingArgs) && !clicore.IsVersionRequest(remainingArgs) && !clicore.IsVersionJSONRequest(remainingArgs) {
		return false, 0
	}
	if startPath, workingDirectoryErr := os.Getwd(); workingDirectoryErr == nil {
		if projectRoot, resolveErr := resolveDispatcherProjectRoot(startPath, projectPath, remainingArgs); resolveErr == nil {
			if handled, code := tryRunDetectedDispatcherV2Project(ctx, projectRoot, args, stdout, stderr, deps); handled {
				return true, code
			}
		}
	}
	if handled, code := tryHandleDispatcherInfoRequest(args, stdout); handled {
		return true, code
	}
	return false, 0
}

func runPinnedDispatcherProjectRunner(
	ctx context.Context,
	args []string,
	remainingArgs []string,
	projectPath string,
	stdout io.Writer,
	stderr io.Writer,
	deps dispatcherRunDeps,
) int {
	startPath, err := os.Getwd()
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{})
		return 1
	}

	projectRoot, err := resolveDispatcherProjectRoot(startPath, projectPath, remainingArgs)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{})
		return 1
	}
	if handled, code := tryRunDetectedDispatcherV2Project(ctx, projectRoot, args, stdout, stderr, deps); handled {
		return code
	}

	pin, err := loadDispatcherPin(projectRoot)
	if err != nil {
		clierrors.WriteErrorEnvelope(stderr, dispatcherPinResolutionError(projectRoot, err))
		return 1
	}

	if handled, code := enforceDispatcherFreshnessWithDeps(ctx, pin, stderr, deps); handled {
		return code
	}

	realCLIPath, err := resolveDispatcherRealCLI(ctx, pin, stderr)
	if err != nil {
		clierrors.WriteErrorEnvelope(stderr, dispatcherRealCLIResolutionError(projectRoot, pin, err))
		return 1
	}

	return deps.runRealCLI(ctx, realCLIPath, args, stdout, stderr)
}

// runDispatcherProcessCommand executes dispatcher-owned commands without
// delegating to the shared runner entrypoint. The dispatcher binary is being
// slimmed down to the forwarding machinery plus bootstrap commands, so its
// execution path must not run through RunProjectLocal.
func runDispatcherProcessCommandWithDeps(
	ctx context.Context,
	args []string,
	remainingArgs []string,
	projectPath string,
	stdout io.Writer,
	stderr io.Writer,
	deps dispatcherRunDeps,
) int {
	startPath, err := os.Getwd()
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{})
		return 1
	}
	if !shouldKeepDispatcherProcessCommand(remainingArgs) {
		if projectRoot, resolveErr := resolveDispatcherProjectRoot(startPath, projectPath, remainingArgs); resolveErr == nil {
			if handled, code := tryRunDetectedDispatcherV2Project(ctx, projectRoot, args, stdout, stderr, deps); handled {
				return code
			}
		}
	}
	if handled, code := tryHandleProjectScopeHelpRequest(remainingArgs, projectPath, stdout); handled {
		return code
	}

	command := remainingArgs[0]
	commandArgs := remainingArgs[1:]

	if handled, code := tryHandlePreConnectionRequestWithDeps(
		ctx,
		remainingArgs,
		command,
		commandArgs,
		startPath,
		projectPath,
		stdout,
		stderr,
		deps,
	); handled {
		return code
	}

	// Precondition violated: shouldRunInDispatcherProcess routed a command here
	// that no dispatcher-process handler accepts. Fail fast instead of guessing.
	clierrors.WriteErrorEnvelope(stderr, clierrors.InternalCLIError(
		"Dispatcher routing bug: no dispatcher-process handler accepted command: "+command,
		clierrors.ErrorContext{Command: command},
	))
	return 1
}

func shouldKeepDispatcherProcessCommand(args []string) bool {
	if len(args) == 0 {
		return true
	}
	switch args[0] {
	case clicore.InstallCommandName, clicore.UpdateCommandName, clicore.UninstallCommandName, clicore.LaunchCommandName, clicore.PackageCommandName:
		return true
	default:
		return false
	}
}

func shouldRunInDispatcherProcess(args []string) bool {
	if len(args) == 0 || clicore.IsHelpRequest(args) {
		return true
	}
	if clicore.ContainsHelpRequest(args) {
		// Runner-owned command help is answered by the pinned runner itself
		// (see command_help.go), not by a dispatcher-side table, so it must be
		// forwarded through the normal dispatch path instead of staying here.
		return !clicore.IsRunnerOwnedCommandName(args[0])
	}
	// Why version requests must be forwarded: bare --version is answered by
	// tryHandleDispatcherInfoRequest before global-argument parsing, so a
	// version request seen here is project-scoped and the pinned runner must
	// report its own version, which can differ from the one embedded here.
	if clicore.IsVersionRequest(args) || clicore.IsVersionJSONRequest(args) {
		return false
	}
	if clicore.IsUnknownLeadingOption(args[0]) {
		return true
	}

	return clicore.IsDispatcherOwnedCommandName(args[0])
}

func resolveDispatcherProjectRoot(startPath string, explicitProjectPath string, args []string) (string, error) {
	if len(args) > 0 && args[0] == clicore.LaunchCommandName {
		options, err := parseLaunchOptions(args[1:], explicitProjectPath)
		if err != nil {
			return "", err
		}
		return resolveLaunchProjectRoot(startPath, options)
	}

	connection, err := project.ResolveConnection(startPath, explicitProjectPath)
	if err != nil {
		return "", err
	}
	return connection.ProjectRoot, nil
}

func runRealCLICommand(ctx context.Context, realCLIPath string, args []string, stdout io.Writer, stderr io.Writer) int {
	command := exec.CommandContext(ctx, realCLIPath, args...)
	command.Stdout = stdout
	command.Stderr = stderr
	command.Stdin = os.Stdin
	command.Env = os.Environ()
	if err := command.Run(); err != nil {
		var exitErr *exec.ExitError
		if errors.As(err, &exitErr) {
			return exitErr.ExitCode()
		}
		clierrors.WriteErrorEnvelope(stderr, clierrors.CLIError{
			ErrorCode:   clierrors.ErrorCodeInternalError,
			Phase:       clierrors.ErrorPhaseExecution,
			Message:     "Failed to run resolved uloop CLI: " + err.Error(),
			Retryable:   true,
			SafeToRetry: true,
			NextActions: []string{"Retry after checking the cached uloop CLI file permissions."},
			Details: map[string]any{
				"ExecutablePath": realCLIPath,
			},
		})
		return 1
	}
	return 0
}

func dispatcherPinResolutionError(projectRoot string, cause error) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeInternalError,
		Phase:       clierrors.ErrorPhaseProjectResolve,
		Message:     "Could not resolve the required uloop project runner for this Unity project.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		NextActions: []string{
			"Open the Unity project once so Unity CLI Loop can write `.uloop/project-runner-pin.json`.",
			"Run the CLI setup from Unity CLI Loop Settings if the pin file is still missing.",
		},
		Details: map[string]any{
			"Cause": cause.Error(),
		},
	}
}

func dispatcherRealCLIResolutionError(projectRoot string, pin dispatcherPin, cause error) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeInternalError,
		Phase:       clierrors.ErrorPhaseExecution,
		Message:     "Could not prepare the pinned uloop project runner version.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		NextActions: []string{
			"Check network access to GitHub releases, then retry the command.",
			"For dogfooding checkouts with an unpublished pin, set " +
				nativepath.ProjectRunnerPathEnvName +
				" to a locally built uloop-project-runner binary to bypass the download.",
		},
		Details: map[string]any{
			"Cause":                cause.Error(),
			"ProjectRunnerVersion": pin.ProjectRunnerVersion,
			"PinSource":            pin.SourcePath,
		},
	}
}
