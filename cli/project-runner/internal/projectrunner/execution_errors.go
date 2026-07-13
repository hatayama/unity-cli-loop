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
		// Why: agents historically treated this timeout as a frozen Editor and ran
		// launch -r. The real recovery path is retrying compile while C# still holds
		// the result (CompileResultLifetime 20m = wait 10m + ~10m retrievable window).
		NextActions: []string{
			"Run a light command such as `uloop get-logs --max-count 1` to check whether Unity is responsive before treating this as a freeze.",
			"Unity-side compile continues after this timeout; retry `uloop compile` — the result remains retrievable for about 10 more minutes without `uloop launch -r`.",
			clierrors.ApiUpdateConsentModalNextAction,
			"Only if Unity does not respond to any command, restart it with `uloop launch -r`.",
		},
	}
}
