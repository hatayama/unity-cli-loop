//go:build windows

package dispatcher

// isUnityLockfileHeld always reports the lockfile as free on Windows.
// Why no probe: Windows refuses to delete a file another process has open, so a lockfile a running
// Editor holds already fails the cleanup's delete instead of being removed.
func isUnityLockfileHeld(string) (bool, error) {
	return false, nil
}
