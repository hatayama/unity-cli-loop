//go:build !windows

package projectrunner

import (
	"context"
	"testing"
)

// Verifies the button press built for other operating systems fails instead of reporting a press
// that never happened.
func TestPressWin32WindowButtonFailsOutsideWindows(t *testing.T) {
	err := pressWin32WindowButton(context.Background(), win32PressTargetResult{DialogFound: true, ButtonFound: true})

	if err == nil {
		t.Fatal("pressWin32WindowButton must fail outside Windows")
	}
}
