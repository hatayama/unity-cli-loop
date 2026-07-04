package dispatcher

import (
	"context"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

type launchDeps struct {
	findRunningUnityProcess    func(context.Context, string) (*clicore.UnityProcess, error)
	focusUnityProcess          func(context.Context, int) error
	killUnityProcess           func(int) error
	resolveUnityExecutablePath func(string) (string, error)
	waitForUnityProcessExit    func(context.Context, string, int, time.Duration, time.Duration) error
	waitForUnityStartupMarker  func(context.Context, string, time.Duration, time.Duration) error
	waitForToolReadiness       func(context.Context, string, time.Duration) error
	probeProjectIpcFallback    func(context.Context, string) error
}

func defaultLaunchDeps() launchDeps {
	return launchDeps{
		findRunningUnityProcess:    clicore.FindRunningUnityProcess,
		focusUnityProcess:          clicore.FocusUnityProcess,
		killUnityProcess:           killUnityProcess,
		resolveUnityExecutablePath: resolveUnityExecutablePath,
		waitForUnityProcessExit:    waitForUnityProcessExit,
		waitForUnityStartupMarker:  waitForUnityStartupMarkerOrTimeout,
		waitForToolReadiness:       clicore.WaitForToolReadinessWithTimeout,
		probeProjectIpcFallback:    clicore.ProbeToolReadinessSequence,
	}
}
