package dispatcher

import (
	"context"
	"encoding/json"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	sharedversion "github.com/hatayama/unity-cli-loop/common/version"
	sharedupdate "github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
)

// enforceDispatcherFreshness enforces the pin's minimumDispatcherVersion floor. The periodic
// update to the newest release lives in runPeriodicDispatcherSelfUpdate; this path only ever
// runs the required update.
func enforceDispatcherFreshness(ctx context.Context, pin dispatcherPin, installedVersion string, stderr io.Writer) (bool, int) {
	return enforceDispatcherFreshnessWithDeps(ctx, pin, installedVersion, stderr, defaultDispatcherRunDeps())
}

type dispatcherFreshnessAction string

const (
	dispatcherFreshnessNoop                 dispatcherFreshnessAction = "noop"
	dispatcherFreshnessManualUpdateRequired dispatcherFreshnessAction = "manual-update-required"
	dispatcherFreshnessRunRequiredUpdate    dispatcherFreshnessAction = "run-required-update"
)

type dispatcherFreshnessInputs struct {
	MinimumVersion     string
	CurrentVersion     string
	SelfUpdateDisabled bool
	ManagedInstall     sharedupdate.ManagedInstall
}

type dispatcherFreshnessPlan struct {
	Action         dispatcherFreshnessAction
	MinimumVersion string
	Reason         string
	NextAction     string
}

const (
	dispatcherFreshnessReasonSelfUpdateDisabled = "Automatic update is disabled."
)

// enforceDispatcherFreshnessWithDeps returns handled=true when the command must stop because the
// running dispatcher is below the pin's floor. installedVersion is the release the periodic
// update installed earlier in this process ("" when it installed nothing).
func enforceDispatcherFreshnessWithDeps(ctx context.Context, pin dispatcherPin, installedVersion string, stderr io.Writer, deps dispatcherRunDeps) (bool, int) {
	plan := decideDispatcherFreshness(dispatcherFreshnessInputs{
		MinimumVersion:     pin.MinimumDispatcherVersion,
		CurrentVersion:     dispatcherVersion,
		SelfUpdateDisabled: dispatcherSelfUpdateDisabled(),
		ManagedInstall:     detectManagedDispatcherInstall(),
	})
	// Why not install again: the periodic update already put the newest release in place, and
	// comparing against the embedded version would download and reinstall that same release.
	if plan.Action == dispatcherFreshnessRunRequiredUpdate && installedVersion != "" {
		writeDispatcherSelfUpdateRequiredError(stderr, installedVersion)
		return true, 1
	}
	return executeDispatcherFreshnessPlan(ctx, plan, stderr, deps)
}

// detectManagedDispatcherInstall reports whether a package manager owns this dispatcher binary.
// Why: freshness is best-effort background work — path resolution failures stay false here so
// ordinary commands keep running; explicit `uloop update` is what surfaces resolution errors.
func detectManagedDispatcherInstall() sharedupdate.ManagedInstall {
	executablePath, err := resolveUpdateExecutablePathFunc()
	if err != nil {
		return sharedupdate.ManagedInstall{}
	}
	return sharedupdate.DetectManagedInstall(executablePath)
}

func decideDispatcherFreshness(inputs dispatcherFreshnessInputs) dispatcherFreshnessPlan {
	minimumVersion := strings.TrimSpace(inputs.MinimumVersion)
	if minimumVersion == "" || !sharedversion.IsLessThan(inputs.CurrentVersion, minimumVersion) {
		return dispatcherFreshnessPlan{Action: dispatcherFreshnessNoop, MinimumVersion: minimumVersion}
	}
	if inputs.ManagedInstall.IsManaged() {
		return dispatcherFreshnessPlan{
			Action:         dispatcherFreshnessManualUpdateRequired,
			MinimumVersion: minimumVersion,
			Reason:         managedInstallFreshnessReason(inputs.ManagedInstall),
			NextAction:     "Run `" + inputs.ManagedInstall.UpgradeCommand + "` and retry the command.",
		}
	}
	if inputs.SelfUpdateDisabled {
		return dispatcherFreshnessPlan{
			Action:         dispatcherFreshnessManualUpdateRequired,
			MinimumVersion: minimumVersion,
			Reason:         dispatcherFreshnessReasonSelfUpdateDisabled,
			NextAction:     "Run `uloop update` and retry the command.",
		}
	}
	return dispatcherFreshnessPlan{Action: dispatcherFreshnessRunRequiredUpdate, MinimumVersion: minimumVersion}
}

func managedInstallFreshnessReason(managedInstall sharedupdate.ManagedInstall) string {
	return "This uloop install is managed by " + managedInstall.DisplayName + ". Run `" + managedInstall.UpgradeCommand + "` to update."
}

func executeDispatcherFreshnessPlan(ctx context.Context, plan dispatcherFreshnessPlan, stderr io.Writer, deps dispatcherRunDeps) (bool, int) {
	switch plan.Action {
	case dispatcherFreshnessNoop:
		return false, 0
	case dispatcherFreshnessManualUpdateRequired:
		writeDispatcherManualUpdateRequiredError(stderr, plan)
		return true, 1
	case dispatcherFreshnessRunRequiredUpdate:
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
	release, lockResult := acquireDispatcherUpdateLockInCache(deps)
	if lockResult == dispatcherUpdateLockHeld {
		writeDispatcherUpdateInProgressError(stderr, plan)
		return true, 1
	}
	// Why an unavailable lock still updates: a cache that cannot be written must not stop the
	// required update, which is the only way past the pin's floor.
	defer release()
	_, err := deps.runUpdate(ctx)
	if err == nil {
		markDispatcherSelfUpdateCheckedWithDeps(deps)
		updatedVersion := dispatcherInstalledVersionOrEmpty(ctx)
		writeDispatcherSelfUpdateRequiredError(stderr, updatedVersion)
		return true, 1
	}

	plan.Action = dispatcherFreshnessManualUpdateRequired
	plan.Reason = "Automatic update failed: " + err.Error()
	plan.NextAction = "Run `uloop update` and retry the command."
	return executeDispatcherFreshnessPlan(ctx, plan, stderr, deps)
}

func writeDispatcherUpdateInProgressError(stderr io.Writer, plan dispatcherFreshnessPlan) {
	clierrors.WriteErrorEnvelope(stderr, clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeCLIUpdateRequired,
		Phase:       clierrors.ErrorPhaseExecution,
		Message:     "Another uloop command is updating the dispatcher. Retry the same command when it finishes.",
		Retryable:   true,
		SafeToRetry: true,
		NextActions: []string{"Retry the same uloop command when the other uloop command finishes."},
		Details: map[string]any{
			"CurrentDispatcherVersion": dispatcherVersion,
			"MinimumDispatcherVersion": plan.MinimumVersion,
		},
	})
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

func writeDispatcherManualUpdateRequiredError(stderr io.Writer, plan dispatcherFreshnessPlan) {
	clierrors.WriteErrorEnvelope(stderr, clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeCLIUpdateRequired,
		Phase:       clierrors.ErrorPhaseExecution,
		Message:     "This project requires uloop dispatcher >= " + plan.MinimumVersion + ". " + plan.Reason,
		Retryable:   true,
		SafeToRetry: true,
		NextActions: []string{plan.NextAction},
		Details: map[string]any{
			"CurrentDispatcherVersion": dispatcherVersion,
			"MinimumDispatcherVersion": plan.MinimumVersion,
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

// markDispatcherSelfUpdateCheckedWithDeps records the check time and reports whether it was written.
func markDispatcherSelfUpdateCheckedWithDeps(deps dispatcherRunDeps) bool {
	cacheRoot, err := dispatcherCacheRoot(runtime.GOOS)
	if err != nil {
		return false
	}
	if err := os.MkdirAll(cacheRoot, 0o755); err != nil {
		return false
	}
	content, err := json.Marshal(dispatcherUpdateState{LastChecked: deps.now().UTC()})
	if err != nil {
		return false
	}
	return os.WriteFile(filepath.Join(cacheRoot, dispatcherUpdateStateFileName), content, 0o644) == nil
}

func runDispatcherUpdateCommand(ctx context.Context) (bool, error) {
	return runDispatcherUpdateCommandForOS(ctx, runtime.GOOS)
}

func runDispatcherUpdateCommandForOS(ctx context.Context, goos string) (bool, error) {
	resolved, err := resolveUpdateTargetVersionFunc(ctx, sharedupdate.Options{
		CurrentVersion: dispatcherVersion,
	})
	if err != nil {
		return false, err
	}
	_, targetVersion, targetChanged := normalizedDispatcherUpdateVersions(dispatcherVersion, resolved.TargetVersion)
	if err := validateDispatcherProjectRunnerVersion(targetVersion); err != nil {
		return false, err
	}
	if !targetChanged {
		return false, nil
	}
	command, err := sharedupdate.CommandForOS(goos, resolved)
	if err != nil {
		return false, err
	}
	if err := updateRunCommand(ctx, command, io.Discard, io.Discard); err != nil {
		return false, err
	}
	return true, nil
}
