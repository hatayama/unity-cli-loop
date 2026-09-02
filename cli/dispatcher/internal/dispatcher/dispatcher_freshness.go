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

	"github.com/hatayama/unity-cli-loop/common/clicore"
	sharedversion "github.com/hatayama/unity-cli-loop/common/version"
	sharedupdate "github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
)

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
		ManagedInstall:     detectManagedDispatcherInstall(),
	})
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
	if minimumVersion == "" {
		return dispatcherFreshnessPlan{Action: dispatcherFreshnessNoop}
	}
	updateRequired := sharedversion.IsLessThan(inputs.CurrentVersion, minimumVersion)
	if updateRequired && inputs.ManagedInstall.IsManaged() {
		return dispatcherFreshnessPlan{
			Action:         dispatcherFreshnessManualUpdateRequired,
			MinimumVersion: minimumVersion,
			Reason:         managedInstallFreshnessReason(inputs.ManagedInstall),
			NextAction:     "Run `" + inputs.ManagedInstall.UpgradeCommand + "` and retry the command.",
		}
	}
	if updateRequired && inputs.SelfUpdateDisabled {
		return dispatcherFreshnessPlan{
			Action:         dispatcherFreshnessManualUpdateRequired,
			MinimumVersion: minimumVersion,
			Reason:         dispatcherFreshnessReasonSelfUpdateDisabled,
			NextAction:     "Run `uloop update` and retry the command.",
		}
	}
	if updateRequired {
		return dispatcherFreshnessPlan{Action: dispatcherFreshnessRunRequiredUpdate, MinimumVersion: minimumVersion}
	}
	if inputs.ManagedInstall.IsManaged() || inputs.HasSiblingRealCLI || !inputs.UpdateDue {
		return dispatcherFreshnessPlan{Action: dispatcherFreshnessNoop, MinimumVersion: minimumVersion}
	}
	return dispatcherFreshnessPlan{Action: dispatcherFreshnessRunOptionalUpdate, MinimumVersion: minimumVersion}
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
	updated, err := deps.runUpdate(ctx)
	if err == nil {
		markDispatcherSelfUpdateCheckedWithDeps(deps)
		if plan.Action == dispatcherFreshnessRunRequiredUpdate {
			updatedVersion := dispatcherInstalledVersionOrEmpty(ctx)
			writeDispatcherSelfUpdateRequiredError(stderr, updatedVersion)
			return true, 1
		}
		if !updated {
			return false, 0
		}
		updatedVersion := dispatcherInstalledVersionOrEmpty(ctx)
		writeOptionalDispatcherUpdateCompletion(stderr, dispatcherVersion, updatedVersion)
		return false, 0
	}

	if plan.Action == dispatcherFreshnessRunRequiredUpdate {
		plan.Action = dispatcherFreshnessManualUpdateRequired
		plan.Reason = "Automatic update failed: " + err.Error()
		plan.NextAction = "Run `uloop update` and retry the command."
		return executeDispatcherFreshnessPlan(ctx, plan, stderr, deps)
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
