package dispatcher

import (
	"context"
	"errors"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/attestation"
)

func TestDefaultVerifyReleaseAssetAttestationRejectsEmptyTag(t *testing.T) {
	// Verifies the attestation verifier refuses to hit the network when the caller passes an empty release tag —
	// contract-programming safeguard so correctness never depends on the git-refs endpoint's 404 response for a
	// caller bug that could be caught locally.
	err := defaultVerifyReleaseAssetAttestation(
		context.Background(),
		"",
		"https://example.test/install.sh",
		"/tmp/install.sh",
		attestationDispatcherPublishWorkflowPath,
	)
	if err == nil {
		t.Fatal("expected empty releaseTag to fail closed, got nil")
	}
	if !errors.Is(err, attestation.ErrVerificationFailed) {
		t.Fatalf("expected ErrVerificationFailed sentinel, got %v", err)
	}
	if !strings.Contains(err.Error(), "releaseTag required") {
		t.Fatalf("expected error to mention the missing field, got %v", err)
	}
}
