package dispatcher

import (
	"context"
	"errors"
	"io"
	"path/filepath"
	"runtime"
	"strings"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/githubapi"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
	sharedupdate "github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
)

// Only the newest-release lookup is bounded. The installer itself is never given a deadline:
// killing it mid-run can leave Windows without uloop.exe after the installer moved it aside.
const periodicUpdateLookupTimeout = 10 * time.Second

// isExcludedFromPeriodicSelfUpdate reports commands that must never trigger the periodic
// self-update: help and version output stay instant, update owns its own install, and
// install / uninstall run from inside installers and the Unity settings window, where
// starting a second installer would nest one install inside another.
func isExcludedFromPeriodicSelfUpdate(remainingArgs []string) bool {
	if len(remainingArgs) == 0 ||
		clicore.IsHelpRequest(remainingArgs) ||
		clicore.ContainsHelpRequest(remainingArgs) ||
		clicore.IsVersionRequest(remainingArgs) ||
		clicore.IsVersionJSONRequest(remainingArgs) {
		return true
	}
	switch remainingArgs[0] {
	case clicore.UpdateCommandName, clicore.InstallCommandName, clicore.UninstallCommandName, clicore.VersionCommandName:
		return true
	default:
		return false
	}
}

// isRunningFromInstallDir reports whether the running executable sits in the directory the
// installer writes to. The installer only replaces that directory, so a uloop elsewhere (a dev
// build, a hand-extracted archive) would rewrite the global install every day while staying old.
func isRunningFromInstallDir(executablePath string, installDir string, goos string) bool {
	if strings.TrimSpace(executablePath) == "" || strings.TrimSpace(installDir) == "" {
		return false
	}
	executableDir := pathForInstallDirComparison(filepath.Dir(executablePath))
	targetDir := pathForInstallDirComparison(installDir)
	if goos == "windows" {
		return strings.EqualFold(executableDir, targetDir)
	}
	return executableDir == targetDir
}

func pathForInstallDirComparison(path string) string {
	resolvedPath, err := filepath.EvalSymlinks(path)
	if err != nil {
		return filepath.Clean(path)
	}
	return filepath.Clean(resolvedPath)
}

// resolveDispatcherSelfUpdateLocation returns the running executable and the install directory.
func resolveDispatcherSelfUpdateLocation() (string, string, error) {
	executablePath, err := resolveUpdateExecutablePathFunc()
	if err != nil {
		return "", "", err
	}
	installDir, err := nativepath.ResolveInstallDir(runtime.GOOS, "", nativepath.DefaultEnvironment())
	if err != nil {
		return "", "", err
	}
	return executablePath, installDir, nil
}

// runPeriodicDispatcherSelfUpdate installs the newest dispatcher release at most once per
// interval and returns the version it installed ("" when nothing was installed). The command
// that found the update keeps running on the old dispatcher; the next command uses the new one.
func runPeriodicDispatcherSelfUpdate(ctx context.Context, stderr io.Writer, deps dispatcherRunDeps) string {
	if !periodicDispatcherSelfUpdateAllowed(deps) || !dispatcherSelfUpdateDueWithDeps(deps) {
		return ""
	}
	release, result := acquireDispatcherUpdateLockInCache(deps)
	if result != dispatcherUpdateLockAcquired {
		return ""
	}
	defer release()
	// Why check again under the lock: another process may have finished a check, or recorded a
	// version hold with `uloop update --to-version`, between the first checks and the lock.
	if !dispatcherSelfUpdateDueWithDeps(deps) {
		return ""
	}
	if held, _ := readDispatcherVersionHold(); held {
		return ""
	}
	// Why record before checking: when the record cannot be written, every command would see the
	// check as due and query GitHub (and print the same warning) forever.
	if !markDispatcherSelfUpdateCheckedWithDeps(deps) {
		return ""
	}
	installedVersion, err := deps.runPeriodicUpdate(ctx, stderr)
	if err != nil {
		writeDispatcherSelfUpdateSkippedWarning(stderr, err)
		return ""
	}
	if installedVersion == "" {
		return ""
	}
	writeOptionalDispatcherUpdateCompletion(stderr, dispatcherVersion, installedVersion)
	return installedVersion
}

func periodicDispatcherSelfUpdateAllowed(deps dispatcherRunDeps) bool {
	if dispatcherSelfUpdateDisabled() || detectManagedDispatcherInstall().IsManaged() {
		return false
	}
	executablePath, installDir, err := deps.resolveSelfUpdateLocation()
	if err != nil || !isRunningFromInstallDir(executablePath, installDir, runtime.GOOS) {
		return false
	}
	held, _ := readDispatcherVersionHold()
	return !held
}

// dispatcherInstallerError marks a failure of the installer run, as opposed to the release lookup.
type dispatcherInstallerError struct {
	cause error
}

func (installerError dispatcherInstallerError) Error() string {
	return installerError.cause.Error()
}

func (installerError dispatcherInstallerError) Unwrap() error {
	return installerError.cause
}

func writeDispatcherSelfUpdateSkippedWarning(stderr io.Writer, err error) {
	clicore.WriteFormat(stderr, "warning: dispatcher self-update skipped: %v\n", err)
	// Why point at `uloop update`: the periodic update discards installer output so it does not
	// draw progress before every command, which leaves only an exit status to read here.
	var installerError dispatcherInstallerError
	if errors.As(err, &installerError) {
		clicore.WriteLine(stderr, "warning: Run `uloop update` to see the installer output.")
	}
	var rateLimit githubapi.RateLimitError
	if errors.As(err, &rateLimit) {
		for _, action := range rateLimit.NextActions() {
			clicore.WriteFormat(stderr, "warning: %s\n", action)
		}
	}
}

func runPeriodicDispatcherUpdate(ctx context.Context, stderr io.Writer) (string, error) {
	return runPeriodicDispatcherUpdateForOS(ctx, stderr, runtime.GOOS)
}

// runPeriodicDispatcherUpdateForOS installs the newest release when it differs from the running
// dispatcher and returns that release's version, or "" when the running dispatcher is current.
func runPeriodicDispatcherUpdateForOS(ctx context.Context, stderr io.Writer, goos string) (string, error) {
	lookupCtx, cancel := context.WithTimeout(ctx, periodicUpdateLookupTimeout)
	resolved, err := resolveUpdateTargetVersionFunc(lookupCtx, sharedupdate.Options{
		CurrentVersion: dispatcherVersion,
	})
	cancel()
	if err != nil {
		return "", err
	}
	currentVersion, targetVersion, targetChanged := normalizedDispatcherUpdateVersions(dispatcherVersion, resolved.TargetVersion)
	if err := validateDispatcherProjectRunnerVersion(targetVersion); err != nil {
		return "", err
	}
	if !targetChanged {
		return "", nil
	}
	command, err := sharedupdate.CommandForOS(goos, resolved)
	if err != nil {
		return "", err
	}
	// Why announce first: the installer has no deadline, so a stalled download would otherwise
	// leave the user's command silently hanging.
	clicore.WriteFormat(stderr, "uloop: updating the dispatcher from %s to %s before running this command...\n", currentVersion, targetVersion)
	if err := updateRunCommand(ctx, command, io.Discard, io.Discard); err != nil {
		return "", dispatcherInstallerError{cause: err}
	}
	// Why return the target instead of re-reading the binary: a failed re-read would be
	// indistinguishable from "already current", and the required-update path relies on knowing
	// that this process already installed a release.
	return targetVersion, nil
}
