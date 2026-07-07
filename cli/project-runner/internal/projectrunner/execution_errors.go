package projectrunner

import (
	"fmt"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

func compileWaitTimeoutError(projectRoot string) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode: clierrors.ErrorCodeCompileWaitTimeout,
		Phase:     clierrors.ErrorPhaseCompileWaiting,
		Message: fmt.Sprintf(
			"Compile status wait timed out after %dms. This does not mean the Unity Editor is frozen; the compile may simply still be running.",
			compileWaitTimeout.Milliseconds()),
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		Command:     clicore.CompileCommandName,
		// Agents have terminated whole sessions after misreading this timeout as a
		// frozen Editor, so the guidance must walk them through a responsiveness check.
		NextActions: []string{
			"Run a light command such as `uloop get-logs --max-count 1` to check whether Unity is responsive before treating this as a freeze.",
			"If Unity responds, retry `uloop compile`; the previous compile likely finished in the meantime.",
			"Only if Unity does not respond to any command, restart it with `uloop launch -r`.",
		},
	}
}
