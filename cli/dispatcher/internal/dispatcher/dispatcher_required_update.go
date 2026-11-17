package dispatcher

import (
	"context"
	"io"
	"runtime"

	sharedversion "github.com/hatayama/unity-cli-loop/common/version"
	sharedupdate "github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
)

func runRequiredDispatcherUpdate(ctx context.Context, minimumVersion string) (string, error) {
	return runRequiredDispatcherUpdateForOS(ctx, minimumVersion, runtime.GOOS)
}

// runRequiredDispatcherUpdateForOS installs a release that meets the pin's floor and returns its
// version, or "" when the running dispatcher already is that release. It installs the newest
// release on the running channel, or the floor itself when that newest release is older, for
// example a beta floor while the newest stable release is older. The version hold is not touched.
func runRequiredDispatcherUpdateForOS(ctx context.Context, minimumVersion string, goos string) (string, error) {
	resolved, err := resolveUpdateTargetVersionFunc(ctx, sharedupdate.Options{
		CurrentVersion: dispatcherVersion,
	})
	if err != nil {
		return "", err
	}
	_, latestVersion, _ := normalizedDispatcherUpdateVersions(dispatcherVersion, resolved.TargetVersion)
	if err := validateDispatcherProjectRunnerVersion(latestVersion); err != nil {
		return "", err
	}
	if sharedversion.IsLessThan(latestVersion, minimumVersion) {
		resolved, err = resolveUpdateTargetVersionFunc(ctx, sharedupdate.Options{
			CurrentVersion: dispatcherVersion,
			TargetVersion:  sharedupdate.NormalizeTargetVersion(minimumVersion),
		})
		if err != nil {
			return "", err
		}
	}
	_, targetVersion, targetChanged := normalizedDispatcherUpdateVersions(dispatcherVersion, resolved.TargetVersion)
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
	if err := updateRunCommand(ctx, command, io.Discard, io.Discard); err != nil {
		return "", err
	}
	// Why return the target instead of re-reading the binary: a failed re-read would look like
	// "nothing installed" and turn a successful update into a manual-update error.
	return targetVersion, nil
}
