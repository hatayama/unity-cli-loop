package clicore

import (
	"context"
	"os"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

const (
	// Why spaced lookups: an Editor that is tearing down can still be listed for a moment after its
	// server closed, and one empty listing is thin evidence for ending a ten-minute wait. Two lookups this
	// far apart that both find nothing are not a transient.
	unityExitConfirmInterval = 2 * time.Second
	unityExitConfirmations   = 2
)

// NewUnityNotRunningError describes a project whose Unity Editor a successful process lookup did not
// find, noting whether the Editor left its lockfile behind. exitedDuringCommand says the command had
// reached Unity before it went away.
func NewUnityNotRunningError(
	projectRoot string,
	endpoint string,
	cause error,
	exitedDuringCommand bool,
) clierrors.UnityNotRunningError {
	return clierrors.UnityNotRunningError{
		ProjectRoot:         projectRoot,
		Endpoint:            endpoint,
		Cause:               cause,
		LockfileLeftBehind:  lockfileLeftBehind(projectRoot),
		EditorLogPath:       unityprocess.DefaultEditorLogPath(),
		ExitedDuringCommand: exitedDuringCommand,
	}
}

// Why only a lockfile that is certainly there counts: a stat that fails for another reason does not show
// how Unity shut down, and the abnormal-exit guidance must not rest on it.
func lockfileLeftBehind(projectRoot string) bool {
	_, err := os.Stat(unityprocess.LockfilePath(projectRoot))
	return err == nil
}

// UnityExitWatch tells a wait loop that keeps losing its connection to Unity when the Editor process
// for the project has exited, so the loop can stop instead of polling until its own timeout.
type UnityExitWatch struct {
	projectRoot  string
	endpoint     string
	findProcess  func(context.Context, string) (*unityprocess.UnityProcess, error)
	now          func() time.Time
	lastLookupAt time.Time
	goneLookups  int
}

// NewUnityExitWatch creates a watch for the project at projectRoot. findProcess is the process lookup and
// now the clock; a nil now uses time.Now.
func NewUnityExitWatch(
	projectRoot string,
	endpoint string,
	findProcess func(context.Context, string) (*unityprocess.UnityProcess, error),
	now func() time.Time,
) *UnityExitWatch {
	if now == nil {
		now = time.Now
	}
	return &UnityExitWatch{
		projectRoot: projectRoot,
		endpoint:    endpoint,
		findProcess: findProcess,
		now:         now,
	}
}

// Observe takes the error of one poll, nil for a poll that succeeded. It returns a
// clierrors.UnityNotRunningError once polls that found the server gone were followed by two lookups, at
// least unityExitConfirmInterval apart, that both found no Editor process; otherwise it returns nil.
func (watch *UnityExitWatch) Observe(ctx context.Context, pollErr error) error {
	// Any other outcome, a success included, shows a server that may be alive, so the lookups that
	// found nothing so far no longer belong to one exit.
	if !clierrors.IsUnityServerGoneError(pollErr) {
		watch.goneLookups = 0
		return nil
	}
	now := watch.now()
	if !watch.lastLookupAt.IsZero() && now.Sub(watch.lastLookupAt) < unityExitConfirmInterval {
		return nil
	}
	watch.lastLookupAt = now

	process, err := watch.findProcess(ctx, watch.projectRoot)
	// A lookup that failed observed nothing, so it cannot count toward an exit.
	if err != nil || process != nil {
		watch.goneLookups = 0
		return nil
	}
	watch.goneLookups++
	if watch.goneLookups < unityExitConfirmations {
		return nil
	}
	return NewUnityNotRunningError(watch.projectRoot, watch.endpoint, pollErr, true)
}
