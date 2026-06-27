package cli

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

	"github.com/hatayama/unity-cli-loop/cli/internal/project"
	sharedupdate "github.com/hatayama/unity-cli-loop/cli/internal/update"
	sharedversion "github.com/hatayama/unity-cli-loop/cli/internal/version"
)

const (
	dispatcherCacheDirEnvName              = "ULOOP_CACHE_DIR"
	dispatcherDisableSelfUpdateEnvName     = "ULOOP_DISABLE_SELF_UPDATE"
	dispatcherCacheDirectoryName           = "uloop"
	dispatcherVersionsDirectoryName        = "versions"
	dispatcherUpdateStateFileName          = "dispatcher-update.json"
	dispatcherProjectPinRelativePath       = ".uloop/cli-pin.json"
	dispatcherPackagePinFileName           = "cli-pin.json"
	dispatcherUnityPackageName             = "io.github.hatayama.uloopmcp"
	dispatcherRealCLIUnixFileName          = "uloop-cli"
	dispatcherRealCLIWindowsFileName       = "uloop-cli.exe"
	dispatcherReleaseRepository            = "hatayama/unity-cli-loop"
	dispatcherReleaseBaseURL               = "https://github.com/" + dispatcherReleaseRepository + "/releases/download"
	dispatcherSelfUpdateInterval           = 24 * time.Hour
	dispatcherSelfUpdateRequiredRetryError = "Dispatcher update completed. Retry the command so the updated dispatcher can run."
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

	remainingArgs, projectPath, err := parseGlobalProjectPath(args)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{})
		return 1
	}

	if shouldRunInDispatcherProcess(remainingArgs) {
		return RunProjectLocal(ctx, args, stdout, stderr)
	}

	startPath, err := os.Getwd()
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{})
		return 1
	}

	projectRoot, err := resolveDispatcherProjectRoot(startPath, projectPath, remainingArgs)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{})
		return 1
	}

	pin, err := loadDispatcherPin(projectRoot)
	if err != nil {
		writeErrorEnvelope(stderr, dispatcherPinResolutionError(projectRoot, err))
		return 1
	}

	if handled, code := enforceDispatcherFreshness(ctx, pin, stderr); handled {
		return code
	}

	realCLIPath, err := resolveDispatcherRealCLI(ctx, pin)
	if err != nil {
		writeErrorEnvelope(stderr, dispatcherRealCLIResolutionError(projectRoot, pin, err))
		return 1
	}

	return dispatcherRunRealCLI(ctx, realCLIPath, args, stdout, stderr)
}

func shouldRunInDispatcherProcess(args []string) bool {
	if len(args) == 0 || isHelpRequest(args) || containsHelpRequest(args) || isVersionRequest(args) || isVersionJSONRequest(args) {
		return true
	}
	if isUnknownLeadingOption(args[0]) {
		return true
	}
	if shouldHandleCompletionRequest(args) {
		return true
	}

	switch args[0] {
	case launchCommandName, installCommandName, updateCommandName, uninstallCommandName, skillsCommandName:
		return true
	default:
		return false
	}
}

func resolveDispatcherProjectRoot(startPath string, explicitProjectPath string, args []string) (string, error) {
	if len(args) > 0 && args[0] == launchCommandName {
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
		if updateRequired {
			writeErrorEnvelope(stderr, cliError{
				ErrorCode:   errorCodeCLIUpdateRequired,
				Phase:       errorPhaseExecution,
				Message:     dispatcherSelfUpdateRequiredRetryError,
				Retryable:   true,
				SafeToRetry: true,
				NextActions: []string{"Retry the same uloop command."},
			})
			return true, 1
		}
		return false, 0
	}

	if updateRequired {
		writeDispatcherManualUpdateRequiredError(stderr, minimumVersion, "Automatic update failed: "+err.Error())
		return true, 1
	}

	writeFormat(stderr, "warning: dispatcher self-update skipped: %v\n", err)
	return false, 0
}

func writeDispatcherManualUpdateRequiredError(stderr io.Writer, minimumVersion string, reason string) {
	writeErrorEnvelope(stderr, cliError{
		ErrorCode:   errorCodeCLIUpdateRequired,
		Phase:       errorPhaseExecution,
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
		writeErrorEnvelope(stderr, cliError{
			ErrorCode:   errorCodeInternalError,
			Phase:       errorPhaseExecution,
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

func dispatcherPinResolutionError(projectRoot string, cause error) cliError {
	return cliError{
		ErrorCode:   errorCodeInternalError,
		Phase:       errorPhaseProjectResolve,
		Message:     "Could not resolve the required uloop CLI for this Unity project.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		NextActions: []string{
			"Open the Unity project once so Unity CLI Loop can write `.uloop/cli-pin.json`.",
			"Run the CLI setup from Unity CLI Loop Settings if the pin file is still missing.",
		},
		Details: map[string]any{
			"Cause": cause.Error(),
		},
	}
}

func dispatcherRealCLIResolutionError(projectRoot string, pin dispatcherPin, cause error) cliError {
	return cliError{
		ErrorCode:   errorCodeInternalError,
		Phase:       errorPhaseExecution,
		Message:     "Could not prepare the pinned uloop CLI version.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		NextActions: []string{"Check network access to GitHub releases, then retry the command."},
		Details: map[string]any{
			"Cause":      cause.Error(),
			"CliVersion": pin.CLIVersion,
			"PinSource":  pin.SourcePath,
		},
	}
}
