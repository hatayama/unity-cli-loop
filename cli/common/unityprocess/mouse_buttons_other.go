//go:build !windows

package unityprocess

// HeldMouseButtons reports that the OS mouse-button state is not readable on this platform.
func HeldMouseButtons() ([]string, bool) {
	return nil, false
}
