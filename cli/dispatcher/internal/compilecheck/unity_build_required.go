package compilecheck

import (
	"fmt"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

// UnityBuildRequiredError reports a precondition compile-check cannot repair on its own.
// Why it is its own type: every one of these means the Bee artifacts no longer describe the
// project, and the only fix is letting the Editor build once, which is a different action from
// the retry an internal error invites.
type UnityBuildRequiredError struct {
	Reason string
}

func (err UnityBuildRequiredError) Error() string {
	return err.Reason + "; " + runCompileFirstAdvice
}

// ToCLIError lets the shared classifier render this error without knowing the compilecheck package.
func (err UnityBuildRequiredError) ToCLIError(context clierrors.ErrorContext) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeCompileCheckUnityBuildRequired,
		Phase:       clierrors.ErrorPhaseExecution,
		Message:     err.Error(),
		Retryable:   false,
		SafeToRetry: false,
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: []string{"Run `uloop compile`, then retry `compile-check`."},
	}
}

// unityBuildRequired states one precondition failure the user fixes by building in Unity.
func unityBuildRequired(format string, arguments ...any) error {
	return UnityBuildRequiredError{Reason: fmt.Sprintf(format, arguments...)}
}
