package dispatcher

import (
	"context"
	"io"
	"runtime"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
)

// runDispatcherUpdateRequest runs `uloop update` after its arguments were accepted. With
// --to-version it installs that release and holds it; without, it removes any hold and installs
// the newest release. It returns the process exit code.
func runDispatcherUpdateRequest(ctx context.Context, options updateOptions, stdout io.Writer, stderr io.Writer) int {
	return runDispatcherUpdateRequestForOS(ctx, options, runtime.GOOS, stdout, stderr)
}

func runDispatcherUpdateRequestForOS(ctx context.Context, options updateOptions, goos string, stdout io.Writer, stderr io.Writer) int {
	holding := options.targetVersion != ""
	// Why before the release lookup: asking to resume automatic updates must work even when
	// GitHub cannot be reached right now.
	if !holding && !resumeAutomaticDispatcherUpdates(stdout, stderr) {
		return 1
	}
	resolvedOptions, err := resolveUpdateTargetVersionFunc(ctx, update.Options{
		CurrentVersion: dispatcherVersion,
		TargetVersion:  options.targetVersion,
	})
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{Command: clicore.UpdateCommandName})
		return 1
	}
	_, targetVersion, targetChanged := normalizedDispatcherUpdateVersions(dispatcherVersion, resolvedOptions.TargetVersion)
	if err := validateDispatcherProjectRunnerVersion(targetVersion); err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{Command: clicore.UpdateCommandName})
		return 1
	}
	if !targetChanged {
		return finishCurrentDispatcherUpdateRequest(targetVersion, holding, stdout, stderr)
	}
	updateCommand, err := update.CommandForOS(goos, resolvedOptions)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, wrapUnsupportedPlatformError(err), clierrors.ErrorContext{Command: clicore.UpdateCommandName})
		return 1
	}
	release, acquired := acquireManualDispatcherUpdateLock(stderr)
	if !acquired {
		return 1
	}
	defer release()
	return installDispatcherUpdate(ctx, updateCommand, targetVersion, holding, stdout, stderr)
}

// acquireManualDispatcherUpdateLock takes the lock the periodic and required updates use, so
// `uloop update` never runs its installer alongside theirs. The lock is taken before the hold is
// recorded, so a refused request leaves no hold behind. An unusable cache does not block a manual
// update, matching the required update.
func acquireManualDispatcherUpdateLock(stderr io.Writer) (func(), bool) {
	cacheRoot, err := dispatcherCacheRoot(runtime.GOOS)
	if err != nil {
		return releaseNothing, true
	}
	release, result := tryAcquireDispatcherUpdateLock(cacheRoot, time.Now())
	if result != dispatcherUpdateLockHeld {
		return release, true
	}
	clierrors.WriteErrorEnvelope(stderr, clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeInternalError,
		Phase:       clierrors.ErrorPhaseExecution,
		Message:     "Another uloop command is updating the dispatcher.",
		Retryable:   true,
		SafeToRetry: true,
		Command:     clicore.UpdateCommandName,
		NextActions: []string{"Retry `uloop update` when it finishes."},
	})
	return releaseNothing, false
}

func finishCurrentDispatcherUpdateRequest(targetVersion string, holding bool, stdout io.Writer, stderr io.Writer) int {
	if holding && !recordDispatcherVersionHold(targetVersion, stderr) {
		return 1
	}
	clicore.WriteLine(stdout, "uloop dispatcher is already up to date ("+targetVersion+").")
	if holding {
		writeDispatcherVersionHoldNotice(stdout, targetVersion)
	}
	return 0
}

func installDispatcherUpdate(ctx context.Context, updateCommand update.Command, targetVersion string, holding bool, stdout io.Writer, stderr io.Writer) int {
	previousHold := snapshotDispatcherVersionHold()
	// Why record before the installer: the install is the hard-to-undo step, and a hold that
	// cannot be written must stop the request before anything changes.
	if holding && !recordDispatcherVersionHold(targetVersion, stderr) {
		return 1
	}
	clicore.WriteLine(stdout, "Updating global uloop dispatcher...")
	if err := updateRunCommand(ctx, updateCommand, stdout, stderr); err != nil {
		if holding {
			restoreDispatcherVersionHold(previousHold)
		}
		clierrors.WriteErrorEnvelope(stderr, clierrors.CLIError{
			ErrorCode:   clierrors.ErrorCodeInternalError,
			Phase:       clierrors.ErrorPhaseExecution,
			Message:     "Update failed: " + err.Error(),
			Retryable:   true,
			SafeToRetry: true,
			Command:     clicore.UpdateCommandName,
			NextActions: []string{"Retry `uloop update` after checking network access to GitHub."},
			Details: map[string]any{
				"Cause": err.Error(),
			},
		})
		return 1
	}
	writeManualDispatcherUpdateCompletion(stdout, dispatcherVersion, dispatcherInstalledVersionOrEmpty(ctx))
	if holding {
		writeDispatcherVersionHoldNotice(stdout, targetVersion)
	}
	return 0
}

func recordDispatcherVersionHold(version string, stderr io.Writer) bool {
	if err := writeDispatcherVersionHold(version); err != nil {
		writeDispatcherVersionHoldError(stderr, "Could not record the version hold: "+err.Error())
		return false
	}
	return true
}

func resumeAutomaticDispatcherUpdates(stdout io.Writer, stderr io.Writer) bool {
	removed, err := removeDispatcherVersionHold()
	if err != nil {
		writeDispatcherVersionHoldError(stderr, "Could not remove the version hold: "+err.Error())
		return false
	}
	if removed {
		clicore.WriteLine(stdout, "Automatic updates resumed.")
	}
	return true
}

func writeDispatcherVersionHoldNotice(stdout io.Writer, version string) {
	clicore.WriteLine(stdout, "uloop is held at "+version+"; automatic updates are paused. Run `uloop update` to resume them.")
}

func writeDispatcherVersionHoldError(stderr io.Writer, message string) {
	clierrors.WriteErrorEnvelope(stderr, clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeInternalError,
		Phase:       clierrors.ErrorPhaseExecution,
		Message:     message,
		Retryable:   true,
		SafeToRetry: true,
		Command:     clicore.UpdateCommandName,
		NextActions: []string{"Check that the uloop config directory is writable, then retry `uloop update`."},
	})
}
