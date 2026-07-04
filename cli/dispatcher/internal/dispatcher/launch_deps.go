package dispatcher

import (
	"context"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

type launchDeps struct {
	findRunningUnityProcess    func(context.Context, string) (*unityprocess.UnityProcess, error)
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
		findRunningUnityProcess:    unityprocess.FindRunningUnityProcess,
		focusUnityProcess:          unityprocess.FocusUnityProcess,
		killUnityProcess:           killUnityProcess,
		resolveUnityExecutablePath: resolveUnityExecutablePath,
		waitForUnityProcessExit:    waitForUnityProcessExit,
		waitForUnityStartupMarker:  waitForUnityStartupMarkerOrTimeout,
		waitForToolReadiness:       clicore.WaitForToolReadinessWithTimeout,
		probeProjectIpcFallback:    clicore.ProbeToolReadinessSequence,
	}
}
