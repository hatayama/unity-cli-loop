package dispatcher

import (
	"context"
	"os"
	"runtime"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
	"github.com/hatayama/unity-cli-loop/common/unityprocess/unitydialog"
)

type launchDeps struct {
	now                     func() time.Time
	findRunningUnityProcess func(context.Context, string) (*unityprocess.UnityProcess, error)
	// findRunningUnityProcessDefinitively is the lookup for quit, which must not read "no Editor" while an
	// Editor whose command line does not name its project may be this project's.
	findRunningUnityProcessDefinitively func(context.Context, string) (*unityprocess.UnityProcess, error)
	focusUnityProcess                   func(context.Context, int) error
	killUnityProcess                    func(int) error
	requestUnityQuit                    func(context.Context, string) bool
	resolveUnityExecutablePath          func(string) (string, error)
	waitForUnityProcessExit             func(context.Context, string, int, time.Duration, time.Duration) error
	waitForUnityStartupMarker           func(context.Context, string, time.Duration, time.Duration) error
	waitForFreshUnityLockfile           func(context.Context, string, time.Time, time.Duration, time.Duration) error
	waitForV2ServerReady                func(context.Context, string, string, *clicore.DialogLookout, time.Duration, time.Duration) error
	waitForToolReadiness                func(context.Context, string, time.Duration, clicore.DialogFinder) error
	// lookForDialogs reads the modal dialogs of the Editor that lookupProcess finds, through the
	// operating system.
	lookForDialogs func(context.Context, string, unitydialog.ProcessLookup) ([]unitydialog.Dialog, error)
	// canPressDialogs is whether uloop press-dialog-button works on this operating system.
	canPressDialogs         bool
	probeProjectIpcFallback func(context.Context, string) error
	removePath              func(string) error
	statPath                func(string) (os.FileInfo, error)
	isUnityLockfileHeld     func(string) (bool, error)
	sleep                   func(time.Duration)
}

func defaultLaunchDeps() launchDeps {
	return launchDeps{
		now:                                 time.Now,
		findRunningUnityProcess:             unityprocess.FindRunningUnityProcess,
		findRunningUnityProcessDefinitively: unityprocess.FindRunningUnityProcessDefinitively,
		focusUnityProcess:                   unityprocess.FocusUnityProcess,
		killUnityProcess:                    killUnityProcess,
		requestUnityQuit:                    requestUnityQuit,
		resolveUnityExecutablePath:          resolveUnityExecutablePath,
		waitForUnityProcessExit:             waitForUnityProcessExit,
		waitForUnityStartupMarker:           waitForUnityStartupMarkerOrTimeout,
		waitForFreshUnityLockfile:           waitForFreshUnityLockfile,
		waitForV2ServerReady: func(
			ctx context.Context,
			projectRoot string,
			previousServerSessionID string,
			lookout *clicore.DialogLookout,
			poll time.Duration,
			timeout time.Duration,
		) error {
			return waitForV2ServerReady(ctx, projectRoot, previousServerSessionID, defaultV2ServerDial, lookout, poll, timeout)
		},
		lookForDialogs:          lookForLaunchDialogs,
		canPressDialogs:         unitydialog.NewInspector(runtime.GOOS, nil).CanPress(),
		waitForToolReadiness:    clicore.WaitForToolReadinessWithTimeout,
		probeProjectIpcFallback: clicore.ProbeToolReadinessSequence,
		removePath:              os.RemoveAll,
		statPath:                os.Stat,
		isUnityLockfileHeld:     isUnityLockfileHeld,
		sleep:                   time.Sleep,
	}
}

// lookForLaunchDialogs reads the dialogs of the Editor lookupProcess finds, on this operating system.
func lookForLaunchDialogs(
	ctx context.Context,
	projectRoot string,
	lookupProcess unitydialog.ProcessLookup,
) ([]unitydialog.Dialog, error) {
	return unitydialog.NewInspector(runtime.GOOS, lookupProcess).Inspect(ctx, projectRoot)
}
