//go:build windows

package dispatcher

import (
	"errors"

	"golang.org/x/sys/windows"
)

// isUnityLockfileHeld always reports the lockfile as free on Windows.
// Why no probe: a running Editor keeps its lockfile open without delete sharing, so deleting it fails
// with a sharing violation, and isLockfileDeleteRefusedForOpenHandle turns that failure into the
// held-lockfile report. This was observed with an Editor started as administrator, whose command line a
// standard user cannot read.
func isUnityLockfileHeld(string) (bool, error) {
	return false, nil
}

// isLockfileDeleteRefusedForOpenHandle reports whether a lockfile delete failed because another
// process has the file open without delete sharing, as a running Editor does.
// Why the errno and not the message: the message is localized with the Windows display language.
// Why ERROR_ACCESS_DENIED is not included: it also means missing permissions, which waiting or closing
// an Editor does not fix.
func isLockfileDeleteRefusedForOpenHandle(deleteError error) bool {
	return errors.Is(deleteError, windows.ERROR_SHARING_VIOLATION)
}
