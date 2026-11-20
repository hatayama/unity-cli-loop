//go:build !windows

package unitydialog

import (
	"context"
	"errors"
)

// pressWin32WindowButton exists so the package builds on every operating system; the inspector
// calls it only on Windows, where the real one sends the click through Win32.
func pressWin32WindowButton(context.Context, win32PressTargetResult) error {
	return errors.New("a window's button can be pressed only on Windows")
}
