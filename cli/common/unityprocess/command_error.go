package unityprocess

import (
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
