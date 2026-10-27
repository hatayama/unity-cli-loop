package clicore

import (
	"os"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
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
