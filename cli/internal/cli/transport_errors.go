package cli

import (
	"errors"
	"io"
	"net"
	"os"
	"strings"
	"syscall"
)

// Reports whether the error means the transport dropped mid-request. Domain reload
// recovery (status polling) depends on this classification, so typed causes are
// checked first: error strings differ across platforms and locales (notably Windows
// named pipes), and a missed match would fail the command instead of recovering.
func isTransportDisconnectError(err error) bool {
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

	// Fallback for errors that expose no typed cause.
	message := err.Error()
	return message == "UNITY_NO_RESPONSE" ||
		strings.Contains(message, "EOF") ||
		strings.Contains(message, "connection reset") ||
		strings.Contains(message, "broken pipe") ||
		strings.Contains(message, "use of closed network connection")
}

// Reports whether the error is a connection deadline expiry. os.IsTimeout covers
// net.Error implementations (including go-winio's pipe timeout) that the typed
// deadline check alone would miss.
func isFinalResponseTimeoutError(err error) bool {
	if err == nil {
		return false
	}
	if errors.Is(err, os.ErrDeadlineExceeded) || os.IsTimeout(err) {
		return true
	}

	// Fallback for errors that expose no typed cause.
	return strings.Contains(err.Error(), "i/o timeout")
}
