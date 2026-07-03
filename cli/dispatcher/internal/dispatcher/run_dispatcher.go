package dispatcher

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/project"
	sharedversion "github.com/hatayama/unity-cli-loop/common/version"
	sharedupdate "github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
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

var (
	dispatcherNow        = time.Now
	dispatcherRunRealCLI = runRealCLICommand
	dispatcherRunUpdate  = runDispatcherUpdateCommand
)

type dispatcherUpdateState struct {
	LastChecked time.Time `json:"lastChecked"`
}

func RunDispatcher(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) int {
	if handled, code := tryHandleDispatcherInfoRequest(args, stdout); handled {
		return code
	}

	remainingArgs, projectPath, err := clicore.ParseGlobalProjectPath(args)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{})
		return 1
	}

	if shouldRunInDispatcherProcess(remainingArgs) {
		return runDispatcherProcessCommand(ctx, remainingArgs, projectPath, stdout, stderr)
	}

	startPath, err := os.Getwd()
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{})
		return 1
	}

	projectRoot, err := resolveDispatcherProjectRoot(startPath, projectPath, remainingArgs)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{})
		return 1
	}

	pin, err := loadDispatcherPin(projectRoot)
	if err != nil {
		clicore.WriteErrorEnvelope(stderr, dispatcherPinResolutionError(projectRoot, err))
		return 1
	}

	if handled, code := enforceDispatcherFreshness(ctx, pin, stderr); handled {
		return code
	}

	realCLIPath, err := resolveDispatcherRealCLI(ctx, pin, stderr)
	if err != nil {
		clicore.WriteErrorEnvelope(stderr, dispatcherRealCLIResolutionError(projectRoot, pin, err))
		return 1
	}

	return dispatcherRunRealCLI(ctx, realCLIPath, args, stdout, stderr)
}

// runDispatcherProcessCommand executes dispatcher-owned commands without
// delegating to the shared runner entrypoint. The dispatcher binary is being
// slimmed down to the forwarding machinery plus bootstrap commands, so its
// execution path must not run through RunProjectLocal.
func runDispatcherProcessCommand(
	ctx context.Context,
	remainingArgs []string,
	projectPath string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	if handled, code := tryHandleProjectScopeHelpRequest(remainingArgs, projectPath, stdout); handled {
		return code
	}

	command := remainingArgs[0]
	commandArgs := remainingArgs[1:]

	startPath, err := os.Getwd()
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{Command: command})
		return 1
	}

	if handled, code := tryHandlePreConnectionRequest(
		ctx,
		remainingArgs,
		command,
		commandArgs,
		startPath,
		projectPath,
		stdout,
		stderr,
	); handled {
		return code
	}

	// Precondition violated: shouldRunInDispatcherProcess routed a command here
	// that no dispatcher-process handler accepts. Fail fast instead of guessing.
	clicore.WriteErrorEnvelope(stderr, clicore.InternalCLIError(
		"Dispatcher routing bug: no dispatcher-process handler accepted command: "+command,
		clicore.ErrorContext{Command: command},
	))
	return 1
}

func shouldRunInDispatcherProcess(args []string) bool {
	if len(args) == 0 || clicore.IsHelpRequest(args) || clicore.ContainsHelpRequest(args) {
		return true
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
	if clicore.ShouldHandleCompletionRequest(args) {
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

func enforceDispatcherFreshness(ctx context.Context, pin dispatcherPin, stderr io.Writer) (bool, int) {
	minimumVersion := strings.TrimSpace(pin.MinimumDispatcherVersion)
	if minimumVersion == "" {
		return false, 0
	}
	updateRequired := sharedversion.IsLessThan(dispatcherVersion, minimumVersion)
	if updateRequired && dispatcherSelfUpdateDisabled() {
		writeDispatcherManualUpdateRequiredError(stderr, minimumVersion, "Automatic update is disabled.")
		return true, 1
	}
	if !updateRequired {
		if _, ok := dispatcherSiblingRealCLIPath(pin); ok {
			return false, 0
		}
	}

	updateDue := !dispatcherSelfUpdateDisabled() && dispatcherSelfUpdateDue()
	if !updateRequired && !updateDue {
		return false, 0
	}

	err := dispatcherRunUpdate(ctx)
	if err == nil {
		markDispatcherSelfUpdateChecked()
		updatedVersion := dispatcherInstalledVersionOrEmpty(ctx)
		if updateRequired {
			writeDispatcherSelfUpdateRequiredError(stderr, updatedVersion)
			return true, 1
		}
		writeOptionalDispatcherUpdateCompletion(stderr, dispatcherVersion, updatedVersion)
		return false, 0
	}

	if updateRequired {
		writeDispatcherManualUpdateRequiredError(stderr, minimumVersion, "Automatic update failed: "+err.Error())
		return true, 1
	}

	// Why: optional update failures should not retry and redraw installer progress on every command.
	markDispatcherSelfUpdateChecked()
	clicore.WriteFormat(stderr, "warning: dispatcher self-update skipped: %v\n", err)
	return false, 0
}

func writeDispatcherSelfUpdateRequiredError(stderr io.Writer, updatedVersion string) {
	currentVersion, nextVersion, changed := normalizedDispatcherUpdateVersions(dispatcherVersion, updatedVersion)
	message := "Dispatcher update completed. Retry the command so the updated dispatcher can run."
	if changed {
		message = "Dispatcher updated from " + currentVersion + " to " + nextVersion + ". Retry the command so the updated dispatcher can run."
	}
	details := map[string]any{
		"CurrentDispatcherVersion": currentVersion,
	}
	if nextVersion != "" {
		details["UpdatedDispatcherVersion"] = nextVersion
	}
	clicore.WriteErrorEnvelope(stderr, clicore.CLIError{
		ErrorCode:   clicore.ErrorCodeCLIUpdateRequired,
		Phase:       clicore.ErrorPhaseExecution,
		Message:     message,
		Retryable:   true,
		SafeToRetry: true,
		NextActions: []string{"Retry the same uloop command."},
		Details:     details,
	})
}

func writeDispatcherManualUpdateRequiredError(stderr io.Writer, minimumVersion string, reason string) {
	clicore.WriteErrorEnvelope(stderr, clicore.CLIError{
		ErrorCode:   clicore.ErrorCodeCLIUpdateRequired,
		Phase:       clicore.ErrorPhaseExecution,
		Message:     "This project requires uloop dispatcher >= " + minimumVersion + ". " + reason,
		Retryable:   true,
		SafeToRetry: true,
		NextActions: []string{"Run `uloop update` and retry the command."},
		Details: map[string]any{
			"CurrentDispatcherVersion": dispatcherVersion,
			"MinimumDispatcherVersion": minimumVersion,
		},
	})
}

func dispatcherSelfUpdateDisabled() bool {
	value := strings.TrimSpace(os.Getenv(dispatcherDisableSelfUpdateEnvName))
	return value == "1" || strings.EqualFold(value, "true")
}

func dispatcherSelfUpdateDue() bool {
	cacheRoot, err := dispatcherCacheRoot(runtime.GOOS)
	if err != nil {
		return false
	}
	statePath := filepath.Join(cacheRoot, dispatcherUpdateStateFileName)
	content, err := os.ReadFile(statePath)
	if err != nil {
		return true
	}
	state := dispatcherUpdateState{}
	if err := json.Unmarshal(content, &state); err != nil {
		return true
	}
	return dispatcherNow().Sub(state.LastChecked) >= dispatcherSelfUpdateInterval
}

func markDispatcherSelfUpdateChecked() {
	cacheRoot, err := dispatcherCacheRoot(runtime.GOOS)
	if err != nil {
		return
	}
	if err := os.MkdirAll(cacheRoot, 0o755); err != nil {
		return
	}
	content, err := json.Marshal(dispatcherUpdateState{LastChecked: dispatcherNow().UTC()})
	if err != nil {
		return
	}
	_ = os.WriteFile(filepath.Join(cacheRoot, dispatcherUpdateStateFileName), content, 0o644)
}

func runDispatcherUpdateCommand(ctx context.Context) error {
	command, err := sharedupdate.CommandForOS(runtime.GOOS, sharedupdate.Options{
		CurrentVersion: dispatcherVersion,
	})
	if err != nil {
		return err
	}
	updateCommand := exec.CommandContext(ctx, command.Name, command.Args...)
	updateCommand.Stdout = io.Discard
	updateCommand.Stderr = io.Discard
	return updateCommand.Run()
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
		clicore.WriteErrorEnvelope(stderr, clicore.CLIError{
			ErrorCode:   clicore.ErrorCodeInternalError,
			Phase:       clicore.ErrorPhaseExecution,
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

func dispatcherPinResolutionError(projectRoot string, cause error) clicore.CLIError {
	return clicore.CLIError{
		ErrorCode:   clicore.ErrorCodeInternalError,
		Phase:       clicore.ErrorPhaseProjectResolve,
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

func dispatcherRealCLIResolutionError(projectRoot string, pin dispatcherPin, cause error) clicore.CLIError {
	return clicore.CLIError{
		ErrorCode:   clicore.ErrorCodeInternalError,
		Phase:       clicore.ErrorPhaseExecution,
		Message:     "Could not prepare the pinned uloop project runner version.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		NextActions: []string{"Check network access to GitHub releases, then retry the command."},
		Details: map[string]any{
			"Cause":                cause.Error(),
			"ProjectRunnerVersion": pin.ProjectRunnerVersion,
			"PinSource":            pin.SourcePath,
		},
	}
}
