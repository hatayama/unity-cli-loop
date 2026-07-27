package clierrors

import (
	"errors"
	"io"
	"net"
	"os"
	"strings"
	"syscall"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Reports whether the error means the transport dropped mid-request. Domain reload
// recovery (status polling) depends on this classification, so typed causes are
// checked first: error strings differ across platforms and locales (notably Windows
// named pipes), and a missed match would fail the command instead of recovering.
func IsTransportDisconnectError(err error) bool {
	if err == nil {
		return false
	}
	if errors.Is(err, io.EOF) ||
		errors.Is(err, io.ErrUnexpectedEOF) ||
		errors.Is(err, io.ErrClosedPipe) ||
		errors.Is(err, net.ErrClosed) ||
		errors.Is(err, syscall.ECONNRESET) ||
		errors.Is(err, syscall.ECONNABORTED) ||
		errors.Is(err, syscall.EPIPE) {
		return true
	}

	var noResponseErr *unityipc.NoResponseError
	if errors.As(err, &noResponseErr) {
		return true
	}

	// Fallback for errors that expose no typed cause.
	message := err.Error()
	return strings.Contains(message, "EOF") ||
		strings.Contains(message, "connection reset") ||
		strings.Contains(message, "broken pipe") ||
		strings.Contains(message, "use of closed network connection")
}

// Reports whether a dial failed for a reason that cannot clear while the caller waits: the
// kernel refused the socket outright (a sandbox policy that denies Unix socket connects,
// permissions on the socket path) instead of reporting that nobody is listening yet. Retrying
// such an error wastes the whole dial-retry window and then reports the window's own deadline
// expiry, so the syscall error the first attempt already had never reaches the caller.
func IsPermanentConnectError(err error) bool {
	return errors.Is(err, syscall.EPERM) || errors.Is(err, syscall.EACCES)
}

// Reports whether the error is a connection deadline expiry. The Timeout() probe
// runs through the unwrap chain because go-winio's named pipe deadline error is not
// os.ErrDeadlineExceeded and os.IsTimeout does not unwrap fmt.Errorf("%w") wrapping.
func IsFinalResponseTimeoutError(err error) bool {
	if err == nil {
		return false
	}
	if errors.Is(err, os.ErrDeadlineExceeded) || os.IsTimeout(err) {
		return true
	}
	var timeoutCause interface{ Timeout() bool }
	if errors.As(err, &timeoutCause) && timeoutCause.Timeout() {
		return true
	}

	// Fallback for errors that expose no typed cause.
	return strings.Contains(err.Error(), "i/o timeout")
}
