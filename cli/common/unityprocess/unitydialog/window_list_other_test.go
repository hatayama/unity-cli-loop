//go:build !windows

package unitydialog

import (
	"context"
	"testing"
)

// Verifies the window listing built for other operating systems fails instead of returning an
// empty list, which would read as "no dialog".
func TestListUnityProcessWindowsFailsOutsideWindows(t *testing.T) {
	windows, err := listUnityProcessWindows(context.Background(), 4242)

	if err == nil || windows != nil {
		t.Fatalf("listUnityProcessWindows = %#v, %v; want an error and no windows", windows, err)
	}
}
