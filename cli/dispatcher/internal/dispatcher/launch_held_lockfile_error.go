package dispatcher

import (
	"fmt"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

const errorCodeUnityProjectAlreadyOpen = "UNITY_PROJECT_ALREADY_OPEN"

// heldUnityLockfileError reports that launch found no Editor for the project while a process still
// holds the project's lockfile, so launch neither removed the lockfile nor started an Editor.
type heldUnityLockfileError struct {
	lockfilePath string
}

func (err heldUnityLockfileError) Error() string {
	return fmt.Sprintf("a running process still holds %s, but no Unity Editor was found for this project", err.lockfilePath)
}

func (err heldUnityLockfileError) ToCLIError(context clierrors.ErrorContext) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode: errorCodeUnityProjectAlreadyOpen,
		Phase:     clierrors.ErrorPhaseExecution,
		Message: "A running process still holds this project's UnityLockfile, but no Unity Editor was found for the project. " +
			"Launch did not start another Editor, because two Editors on one project corrupt each other's Temp and Library.",
		Retryable:   false,
		SafeToRetry: false,
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: []string{
			"Check whether a Unity Editor already has this project open, including one started in batch mode or without -projectPath, and use or close it.",
			"If no Unity process has the project open, find the process holding the lockfile (for example with `lsof` on the lockfile path), stop it, and retry the launch command.",
		},
		Details: map[string]any{
			"LockfilePath": err.lockfilePath,
		},
	}
}
