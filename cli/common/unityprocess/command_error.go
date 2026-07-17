package unityprocess

import (
	"context"
	"errors"
	"fmt"
	"strings"
)

func commandErrorWithStderr(err error, stderr string) error {
	if err == nil {
		return nil
	}
	trimmedStderr := strings.TrimSpace(stderr)
	if trimmedStderr == "" {
		return err
	}
	return fmt.Errorf("%w: %s", err, trimmedStderr)
}

// focusCommandError converts a focus script failure into an actionable error.
// Why: a timeout kills the script before it can write anything to stderr, so without this
// mapping the caller only sees a bare "exit status 1" with no hint about the stalled Editor.
func focusCommandError(contextErr error, runErr error, stderr string) error {
	if runErr == nil {
		return nil
	}
	if errors.Is(contextErr, context.DeadlineExceeded) {
		return fmt.Errorf(
			"focusing the Unity window timed out after %s; the Unity Editor may be busy (for example during a domain reload), retry once it is responsive: %w",
			FocusCommandTimeout,
			runErr,
		)
	}
	return commandErrorWithStderr(runErr, stderr)
}
