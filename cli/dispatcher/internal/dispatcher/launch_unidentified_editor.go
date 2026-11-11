package dispatcher

import (
	"errors"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

// launchUnidentifiedEditorError reports that quit cannot tell which Editor to stop, because an
// Editor whose command line does not name its project is running and may be this project's.
type launchUnidentifiedEditorError struct {
	cause error
}

func (err launchUnidentifiedEditorError) Error() string {
	return err.cause.Error()
}

func (err launchUnidentifiedEditorError) Unwrap() error {
	return err.cause
}

// ToCLIError reports the failure as one nothing was stopped by, so the command is safe to run again once
// that Editor is closed.
func (err launchUnidentifiedEditorError) ToCLIError(context clierrors.ErrorContext) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode: clierrors.ErrorCodeUnityNotReachable,
		Phase:     clierrors.ErrorPhaseExecution,
		Message: "A running Unity Editor's command line does not name its project, so uloop cannot tell " +
			"whether it is this project's Editor and did not stop any Editor.",
		Retryable:   false,
		SafeToRetry: true,
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: []string{
			"If that Unity Editor has this project open, close it by hand.",
			"On Windows, the Editor may be running as administrator while uloop is not; " +
				"if so, run uloop from a shell with the same rights.",
		},
	}
}

// launchStopLookupError turns quit's definitive lookup's unidentified-Editor result into the launch error
// that explains it, and returns any other error as is.
func launchStopLookupError(err error) error {
	if errors.Is(err, unityprocess.ErrUnidentifiedUnityEditor) {
		return launchUnidentifiedEditorError{cause: err}
	}
	return err
}
