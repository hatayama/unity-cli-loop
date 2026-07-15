package unityipc

import (
	"context"
	"errors"
	"net"
	"testing"
)

// Verifies failed endpoint validation prevents every network dial attempt.
func TestDialEndpointWithValidatorAndDialerSkipsDialWhenValidationFails(t *testing.T) {
	validationErr := errors.New("endpoint is insecure")
	dialAttempts := 0
	_, err := dialEndpointWithValidatorAndDialer(
		context.Background(),
		Endpoint{Network: "unix", Address: "/tmp/uloop-501/test.sock"},
		func(Endpoint) error { return validationErr },
		func(context.Context, Endpoint) (net.Conn, error) {
			dialAttempts++
			return nil, errors.New("must not dial")
		},
	)
	if !errors.Is(err, validationErr) {
		t.Fatalf("expected validation error, got %v", err)
	}
	if dialAttempts != 0 {
		t.Fatalf("expected no dial attempts, got %d", dialAttempts)
	}
}
