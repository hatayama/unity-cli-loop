//go:build !windows

package projectrunner

import (
	"context"
	"errors"
)

// listUnityProcessWindows exists so the package builds on every operating system; the inspector
// calls it only on Windows, where the real one reads the windows through Win32.
func listUnityProcessWindows(context.Context, int) ([]win32Window, error) {
	return nil, errors.New("a process's windows can be listed only on Windows")
}
