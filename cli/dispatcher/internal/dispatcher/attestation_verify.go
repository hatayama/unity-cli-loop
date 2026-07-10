package dispatcher

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"io"
	"os"
	"path/filepath"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/attestation"
)

// Attestation identity constants for the two workflows that publish signed
// release assets. Adding a new signed workflow requires appending the SAN here
// so a leaked OIDC token for an unrelated workflow cannot forge our releases.
const (
	attestationDispatcherPublishWorkflowPath = ".github/workflows/dispatcher-publish.yml"
	attestationRunnerPublishWorkflowPath     = ".github/workflows/native-cli-publish.yml"
)

// attestationAllowedRefs is the closed set of refs the OIDC token may be issued
// for. Exact strings, no regex — a stolen token on a different branch fails.
var attestationAllowedRefs = []string{
	"refs/heads/v3-beta",
	"refs/heads/main",
}

// verifyReleaseAssetAttestation is the hook production code calls to verify
// a downloaded release asset. Tests override it to isolate checksum/extract
// logic from real network + sigstore verification.
var verifyReleaseAssetAttestation = defaultVerifyReleaseAssetAttestation

// defaultVerifyReleaseAssetAttestation fetches the `<assetURL>.sigstore.json`
// bundle and validates it against the local asset file, the resolved release
// tag commit SHA, and the given workflow identity. Fail-closed on every
// branch: bundle-missing, network-failure, digest-mismatch, identity-mismatch.
// Callers must NOT run the asset when this returns a non-nil error.
func defaultVerifyReleaseAssetAttestation(ctx context.Context, releaseTag string, assetURL string, assetPath string, workflowPath string) error {
	digestHex, err := computeAssetSHA256Hex(assetPath)
	if err != nil {
		return fmt.Errorf("compute asset digest for attestation: %w", err)
	}

	bundleData, err := attestation.FetchBundle(ctx, assetURL+".sigstore.json")
	if err != nil {
		return err
	}

	commitSHA, err := attestation.FetchTagCommitSHA(ctx, dispatcherReleaseRepository, releaseTag)
	if err != nil {
		return err
	}

	trustedMaterial, err := attestation.LoadEmbeddedTrustedMaterial()
	if err != nil {
		return fmt.Errorf("%w: load embedded trusted root: %v", attestation.ErrVerificationFailed, err)
	}

	return attestation.Verify(trustedMaterial, attestation.VerifyOptions{
		AssetDigest:       digestHex,
		BundleData:        bundleData,
		ExpectedCommitSHA: commitSHA,
		Identity: attestation.Identity{
			Repository:   dispatcherReleaseRepository,
			WorkflowPath: workflowPath,
			Refs:         attestationAllowedRefs,
		},
	})
}

// computeAssetSHA256Hex returns the lowercase hex-encoded SHA-256 of the file
// at path. The dispatcher already sha256-verifies release assets against the
// sibling `.sha256` file for transport-corruption detection; this second read
// exists so the attestation layer never depends on the checksum file that
// ships from the same origin as the asset — the attestation must be able to
// stand on its own if that origin is compromised.
func computeAssetSHA256Hex(path string) (string, error) {
	file, err := os.Open(filepath.Clean(path))
	if err != nil {
		return "", err
	}
	defer func() {
		_ = file.Close()
	}()
	hash := sha256.New()
	if _, err := io.Copy(hash, file); err != nil {
		return "", err
	}
	return hex.EncodeToString(hash.Sum(nil)), nil
}
