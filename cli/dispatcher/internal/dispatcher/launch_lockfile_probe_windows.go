//go:build windows

package dispatcher

// isUnityLockfileHeld always reports the lockfile as free on Windows.
// Why no probe: Windows normally refuses to delete a file another process has open, so the cleanup's
// delete is expected to fail on a lockfile a running Editor holds. That the Editor opens it without
// delete sharing has not been verified here.
func isUnityLockfileHeld(string) (bool, error) {
	return false, nil
}
