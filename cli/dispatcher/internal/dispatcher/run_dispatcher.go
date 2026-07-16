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

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

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

type dispatcherUpdateState struct {
	LastChecked time.Time `json:"lastChecked"`
}

type dispatcherRunDeps struct {
	now        func() time.Time
	runRealCLI func(context.Context, string, []string, io.Writer, io.Writer) int
	runV2CLI   func(context.Context, string, []string, io.Writer, io.Writer) (int, error)
	runUpdate  func(context.Context) error
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
	if len(args) == 0 || clicore.IsHelpRequest(remainingArgs) || clicore.IsVersionRequest(remainingArgs) || clicore.IsVersionJSONRequest(remainingArgs) {
		if startPath, workingDirectoryErr := os.Getwd(); workingDirectoryErr == nil {
			if projectRoot, resolveErr := resolveDispatcherProjectRoot(startPath, projectPath, remainingArgs); resolveErr == nil {
				if handled, code := tryRunDetectedDispatcherV2Project(ctx, projectRoot, args, stdout, stderr, deps); handled {
					return code
				}
			}
		}
		if handled, code := tryHandleDispatcherInfoRequest(args, stdout); handled {
			return code
		}
	}

	if shouldRunInDispatcherProcess(remainingArgs) {
		return runDispatcherProcessCommandWithDeps(ctx, args, remainingArgs, projectPath, stdout, stderr, deps)
	}

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
	if len(args) == 0 || clicore.ShouldHandleCompletionRequest(args) {
		return true
	}
	switch args[0] {
	case clicore.InstallCommandName, clicore.UpdateCommandName, clicore.UninstallCommandName, clicore.LaunchCommandName:
		return true
	default:
		return false
	}
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
	return enforceDispatcherFreshnessWithDeps(ctx, pin, stderr, defaultDispatcherRunDeps())
}

type dispatcherFreshnessAction string

const (
	dispatcherFreshnessNoop                 dispatcherFreshnessAction = "noop"
	dispatcherFreshnessManualUpdateRequired dispatcherFreshnessAction = "manual-update-required"
	dispatcherFreshnessRunRequiredUpdate    dispatcherFreshnessAction = "run-required-update"
	dispatcherFreshnessRunOptionalUpdate    dispatcherFreshnessAction = "run-optional-update"
)

type dispatcherFreshnessInputs struct {
	MinimumVersion     string
	CurrentVersion     string
	SelfUpdateDisabled bool
	HasSiblingRealCLI  bool
	UpdateDue          bool
}

type dispatcherFreshnessPlan struct {
	Action         dispatcherFreshnessAction
	MinimumVersion string
}

func enforceDispatcherFreshnessWithDeps(ctx context.Context, pin dispatcherPin, stderr io.Writer, deps dispatcherRunDeps) (bool, int) {
	minimumVersion := strings.TrimSpace(pin.MinimumDispatcherVersion)
	selfUpdateDisabled := dispatcherSelfUpdateDisabled()
	updateRequired := minimumVersion != "" && sharedversion.IsLessThan(dispatcherVersion, minimumVersion)
	hasSiblingRealCLI := false
	if minimumVersion != "" && !updateRequired {
		if _, ok := dispatcherSiblingRealCLIPath(pin); ok {
			hasSiblingRealCLI = true
		}
	}
	updateDue := false
	if minimumVersion != "" && !updateRequired && !hasSiblingRealCLI && !selfUpdateDisabled {
		updateDue = dispatcherSelfUpdateDueWithDeps(deps)
	}
	plan := decideDispatcherFreshness(dispatcherFreshnessInputs{
		MinimumVersion:     minimumVersion,
		CurrentVersion:     dispatcherVersion,
		SelfUpdateDisabled: selfUpdateDisabled,
		HasSiblingRealCLI:  hasSiblingRealCLI,
		UpdateDue:          updateDue,
	})
	return executeDispatcherFreshnessPlan(ctx, plan, stderr, deps)
}

func decideDispatcherFreshness(inputs dispatcherFreshnessInputs) dispatcherFreshnessPlan {
	minimumVersion := strings.TrimSpace(inputs.MinimumVersion)
	if minimumVersion == "" {
		return dispatcherFreshnessPlan{Action: dispatcherFreshnessNoop}
	}
	updateRequired := sharedversion.IsLessThan(inputs.CurrentVersion, minimumVersion)
	if updateRequired && inputs.SelfUpdateDisabled {
		return dispatcherFreshnessPlan{Action: dispatcherFreshnessManualUpdateRequired, MinimumVersion: minimumVersion}
	}
	if updateRequired {
		return dispatcherFreshnessPlan{Action: dispatcherFreshnessRunRequiredUpdate, MinimumVersion: minimumVersion}
	}
	if inputs.HasSiblingRealCLI || !inputs.UpdateDue {
		return dispatcherFreshnessPlan{Action: dispatcherFreshnessNoop, MinimumVersion: minimumVersion}
	}
	return dispatcherFreshnessPlan{Action: dispatcherFreshnessRunOptionalUpdate, MinimumVersion: minimumVersion}
}

func executeDispatcherFreshnessPlan(ctx context.Context, plan dispatcherFreshnessPlan, stderr io.Writer, deps dispatcherRunDeps) (bool, int) {
	switch plan.Action {
	case dispatcherFreshnessNoop:
		return false, 0
	case dispatcherFreshnessManualUpdateRequired:
		writeDispatcherManualUpdateRequiredError(stderr, plan.MinimumVersion, "Automatic update is disabled.")
		return true, 1
	case dispatcherFreshnessRunRequiredUpdate, dispatcherFreshnessRunOptionalUpdate:
		return runDispatcherFreshnessUpdate(ctx, plan, stderr, deps)
	default:
		clierrors.WriteErrorEnvelope(stderr, clierrors.InternalCLIError(
			"Dispatcher freshness routing bug: unknown action: "+string(plan.Action),
			clierrors.ErrorContext{},
		))
		return true, 1
	}
}

func runDispatcherFreshnessUpdate(ctx context.Context, plan dispatcherFreshnessPlan, stderr io.Writer, deps dispatcherRunDeps) (bool, int) {
	err := deps.runUpdate(ctx)
	if err == nil {
		markDispatcherSelfUpdateCheckedWithDeps(deps)
		updatedVersion := dispatcherInstalledVersionOrEmpty(ctx)
		if plan.Action == dispatcherFreshnessRunRequiredUpdate {
			writeDispatcherSelfUpdateRequiredError(stderr, updatedVersion)
			return true, 1
		}
		writeOptionalDispatcherUpdateCompletion(stderr, dispatcherVersion, updatedVersion)
		return false, 0
	}

	if plan.Action == dispatcherFreshnessRunRequiredUpdate {
		writeDispatcherManualUpdateRequiredError(stderr, plan.MinimumVersion, "Automatic update failed: "+err.Error())
		return true, 1
	}

	// Why: optional update failures should not retry and redraw installer progress on every command.
	markDispatcherSelfUpdateCheckedWithDeps(deps)
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
	clierrors.WriteErrorEnvelope(stderr, clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeCLIUpdateRequired,
		Phase:       clierrors.ErrorPhaseExecution,
		Message:     message,
		Retryable:   true,
		SafeToRetry: true,
		NextActions: []string{"Retry the same uloop command."},
		Details:     details,
	})
}

func writeDispatcherManualUpdateRequiredError(stderr io.Writer, minimumVersion string, reason string) {
	clierrors.WriteErrorEnvelope(stderr, clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeCLIUpdateRequired,
		Phase:       clierrors.ErrorPhaseExecution,
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

func dispatcherSelfUpdateDueWithDeps(deps dispatcherRunDeps) bool {
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
	return deps.now().Sub(state.LastChecked) >= dispatcherSelfUpdateInterval
}

func markDispatcherSelfUpdateCheckedWithDeps(deps dispatcherRunDeps) {
	cacheRoot, err := dispatcherCacheRoot(runtime.GOOS)
	if err != nil {
		return
	}
	if err := os.MkdirAll(cacheRoot, 0o755); err != nil {
		return
	}
	content, err := json.Marshal(dispatcherUpdateState{LastChecked: deps.now().UTC()})
	if err != nil {
		return
	}
	_ = os.WriteFile(filepath.Join(cacheRoot, dispatcherUpdateStateFileName), content, 0o644)
}

func runDispatcherUpdateCommand(ctx context.Context) error {
	resolved, err := resolveUpdateTargetVersionFunc(ctx, sharedupdate.Options{
		CurrentVersion: dispatcherVersion,
	})
	if err != nil {
		return err
	}
	command, err := sharedupdate.CommandForOS(runtime.GOOS, resolved)
	if err != nil {
		return err
	}
	return runUpdateCommand(ctx, command, io.Discard, io.Discard)
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
		NextActions: []string{"Check network access to GitHub releases, then retry the command."},
		Details: map[string]any{
			"Cause":                cause.Error(),
			"ProjectRunnerVersion": pin.ProjectRunnerVersion,
			"PinSource":            pin.SourcePath,
		},
	}
}
