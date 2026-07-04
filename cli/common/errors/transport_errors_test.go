package clierrors

import (
	"fmt"
	"io"
	"net"
	"os"
	"syscall"
	"testing"
)

// Test support type that exposes a typed cause behind an opaque message, simulating
// platform-specific transport errors (e.g. Windows named pipes) whose text does not
// contain the POSIX phrases the string fallback looks for.
type opaqueCauseError struct {
	cause error
}

func (err opaqueCauseError) Error() string {
	return "transport failure (code 0x6d)"
}

func (err opaqueCauseError) Unwrap() error {
	return err.cause
}

// Test support type that reports Timeout() without a recognizable message,
// matching how go-winio surfaces named pipe deadline expiry.
type timeoutOnlyError struct{}

func (timeoutOnlyError) Error() string {
	return "operation did not finish in time"
}

func (timeoutOnlyError) Timeout() bool {
	return true
}

// Verifies that disconnect classification matches typed causes instead of relying on
// platform- or locale-dependent error strings.
func TestIsTransportDisconnectErrorMatchesTypedCauses(t *testing.T) {
	typedCauses := []error{
		io.EOF,
		io.ErrUnexpectedEOF,
		io.ErrClosedPipe,
		net.ErrClosed,
		syscall.ECONNRESET,
		syscall.ECONNABORTED,
		syscall.EPIPE,
	}

	for _, cause := range typedCauses {
		if !IsTransportDisconnectError(opaqueCauseError{cause: cause}) {
			t.Fatalf("typed cause was not classified as disconnect: %v", cause)
		}
	}
}

// Verifies that non-transport errors are not misclassified as disconnects.
func TestIsTransportDisconnectErrorRejectsUnrelatedErrors(t *testing.T) {
	unrelated := []error{
		fmt.Errorf("unity error: compilation failed"),
		opaqueCauseError{cause: fmt.Errorf("some inner failure")},
	}

	for _, err := range unrelated {
		if IsTransportDisconnectError(err) {
			t.Fatalf("unrelated error was classified as disconnect: %v", err)
		}
	}
}

// Verifies the legacy string fallback still classifies errors without a typed cause.
func TestIsTransportDisconnectErrorKeepsStringFallback(t *testing.T) {
	fallbackErrors := []error{
		fmt.Errorf("UNITY_NO_RESPONSE"),
		fmt.Errorf("read tcp 127.0.0.1:1: connection reset by peer"),
	}

	for _, err := range fallbackErrors {
		if !IsTransportDisconnectError(err) {
			t.Fatalf("string fallback did not classify: %v", err)
		}
	}
}

// Verifies that final-response timeout classification matches typed deadline errors
// and Timeout()-reporting errors instead of relying on the "i/o timeout" message.
func TestIsFinalResponseTimeoutErrorMatchesTypedCauses(t *testing.T) {
	if !IsFinalResponseTimeoutError(opaqueCauseError{cause: os.ErrDeadlineExceeded}) {
		t.Fatal("wrapped deadline error was not classified as timeout")
	}
	if !IsFinalResponseTimeoutError(timeoutOnlyError{}) {
		t.Fatal("Timeout()-reporting error was not classified as timeout")
	}
	if IsFinalResponseTimeoutError(fmt.Errorf("unity error: busy")) {
		t.Fatal("unrelated error was classified as timeout")
	}
}

// Verifies that a Timeout()-reporting error stays classified even when wrapped, which
// is how go-winio's named pipe deadline error reaches the caller on Windows.
func TestIsFinalResponseTimeoutErrorUnwrapsTimeoutCauses(t *testing.T) {
	wrapped := opaqueCauseError{cause: timeoutOnlyError{}}
	if !IsFinalResponseTimeoutError(wrapped) {
		t.Fatal("wrapped Timeout()-reporting error was not classified as timeout")
	}
}
