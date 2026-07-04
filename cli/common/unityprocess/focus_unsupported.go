//go:build !darwin && !windows

package unityprocess

import (
	"context"
	"fmt"
	"runtime"
)

func FocusUnityProcess(context.Context, int) error {
	return fmt.Errorf("focus-window is not supported on %s", runtime.GOOS)
}

func FocusUnityProcessWithRestore(context.Context, int) (RestoreFocusFunc, error) {
	return nil, fmt.Errorf("focus-window is not supported on %s", runtime.GOOS)
}
