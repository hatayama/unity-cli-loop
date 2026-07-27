package dispatcher

import (
	"context"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

type launchDeps struct {
	now                        func() time.Time
	findRunningUnityProcess    func(context.Context, string) (*unityprocess.UnityProcess, error)
	focusUnityProcess          func(context.Context, int) error
	killUnityProcess           func(int) error
	resolveUnityExecutablePath func(string) (string, error)
	waitForUnityProcessExit    func(context.Context, string, int, time.Duration, time.Duration) error
	waitForUnityStartupMarker  func(context.Context, string, time.Duration, time.Duration) error
	waitForFreshUnityLockfile  func(context.Context, string, time.Time, time.Duration, time.Duration) error
	waitForV2ServerReady       func(context.Context, string, string, time.Duration, time.Duration) error
	waitForToolReadiness       func(context.Context, string, time.Duration) error
	probeProjectIpcFallback    func(context.Context, string) error
}

func defaultLaunchDeps() launchDeps {
	return launchDeps{
		now:                        time.Now,
		findRunningUnityProcess:    unityprocess.FindRunningUnityProcess,
		focusUnityProcess:          unityprocess.FocusUnityProcess,
		killUnityProcess:           killUnityProcess,
		resolveUnityExecutablePath: resolveUnityExecutablePath,
		waitForUnityProcessExit:    waitForUnityProcessExit,
		waitForUnityStartupMarker:  waitForUnityStartupMarkerOrTimeout,
		waitForFreshUnityLockfile:  waitForFreshUnityLockfile,
		waitForV2ServerReady: func(ctx context.Context, projectRoot string, previousServerSessionID string, poll time.Duration, timeout time.Duration) error {
			return waitForV2ServerReady(ctx, projectRoot, previousServerSessionID, defaultV2ServerDial, poll, timeout)
		},
		waitForToolReadiness:    clicore.WaitForToolReadinessWithTimeout,
		probeProjectIpcFallback: clicore.ProbeToolReadinessSequence,
	}
}
