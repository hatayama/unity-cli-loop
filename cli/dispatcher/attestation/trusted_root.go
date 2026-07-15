// Package attestation verifies GitHub Artifact Attestations for dispatcher
// self-update and project runner download flows.
//
// Trust anchor is embedded via go:embed at build time so verification stays
// offline and deterministic. Refresh via cmd/refresh-attestation-trusted-root.
// The CI guard test in trusted_root_test.go fails when the embedded root's
// authorities approach expiration so a missed quarterly refresh surfaces as a
// red build, not a fleet-wide self-update outage after Fulcio or Rekor rotates.
package attestation

import (
	_ "embed"
	"fmt"

	"github.com/sigstore/sigstore-go/pkg/root"
)

//go:embed trusted_root.json
var embeddedTrustedRoot []byte

// LoadEmbeddedTrustedMaterial parses the embedded Sigstore trusted_root.json
// into a root.TrustedMaterial suitable for verify.NewVerifier.
func LoadEmbeddedTrustedMaterial() (root.TrustedMaterial, error) {
	tr, err := root.NewTrustedRootFromJSON(embeddedTrustedRoot)
	if err != nil {
		return nil, fmt.Errorf("parse embedded trusted_root.json: %w", err)
	}
	return tr, nil
}

// EmbeddedTrustedRootBytes returns the raw embedded trusted_root.json bytes
// so tests can inspect authority validity windows without re-parsing.
func EmbeddedTrustedRootBytes() []byte {
	return embeddedTrustedRoot
}
