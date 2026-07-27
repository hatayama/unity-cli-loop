//go:build windows

package clierrors

import (
	"os"
	"syscall"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// The status Windows returns when opening the project's named pipe is denied, and the one
// go-winio puts in the path error it hands back.
const windowsErrorAccessDenied = syscall.Errno(5)

// Verifies the classification holds against the real Windows status code. The cross-platform test
// substitutes os.ErrPermission for it, which assumes the mapping Go's syscall package performs;
// this test fails if that assumption ever stops holding and Windows silently starts retrying a
// refusal that never clears.
func TestIsPermanentConnectErrorMatchesWindowsAccessDenied(t *testing.T) {
	deniedPipe := &unityipc.ConnectionAttemptError{
		Cause: &os.PathError{
			Op:   "open",
			Path: `\\.\pipe\UnityCliLoop-sample`,
			Err:  windowsErrorAccessDenied,
		},
	}

	if !IsPermanentConnectError(deniedPipe) {
		t.Fatalf("denied named pipe was not classified as permanent: %v", deniedPipe)
	}
}
