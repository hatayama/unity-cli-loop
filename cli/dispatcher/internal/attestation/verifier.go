package attestation

import (
	"encoding/hex"
	"fmt"

	"github.com/sigstore/sigstore-go/pkg/bundle"
	"github.com/sigstore/sigstore-go/pkg/fulcio/certificate"
	"github.com/sigstore/sigstore-go/pkg/root"
	"github.com/sigstore/sigstore-go/pkg/verify"
)

// Identity describes the GitHub Actions workflow identity that a signed release
// asset must present. Refs is the set of git refs the workflow may have run
// from — we accept a small closed allowlist (e.g. v3-beta and main branches)
// rather than a regex so a leaked OIDC token for an unrelated ref cannot forge
// releases.
type Identity struct {
	Repository   string
	WorkflowPath string
	Refs         []string
}

// SubjectAlternativeNames constructs the exact-match SAN strings that a Fulcio
// certificate must expose to pass this identity — one per allowed ref.
func (id Identity) SubjectAlternativeNames() []string {
	sans := make([]string, 0, len(id.Refs))
	for _, ref := range id.Refs {
		sans = append(sans, fmt.Sprintf("https://github.com/%s/%s@%s", id.Repository, id.WorkflowPath, ref))
	}
	return sans
}

// SourceRepositoryURI is the value the Fulcio cert must expose under OID
// 1.3.6.1.4.1.57264.1.12 — the top-level repository the workflow ran in.
func (id Identity) SourceRepositoryURI() string {
	return "https://github.com/" + id.Repository
}

// VerifyOptions is the input to Verify.
type VerifyOptions struct {
	// AssetDigest is the SHA-256 hex digest of the local release asset the
	// caller intends to trust. Must be the same digest the caller will use
	// to open/execute the file.
	AssetDigest string

	// BundleData is the raw JSON bytes of the <asset>.sigstore.json bundle
	// downloaded from the release. The single bundle carries multiple
	// subjects (one per release asset); Verify enforces that AssetDigest
	// appears in the subject list.
	BundleData []byte

	// ExpectedCommitSHA is the 40-char hex git commit SHA that the release
	// tag points at (resolved via /repos/{owner}/{repo}/git/ref/tags/{tag}).
	// The Fulcio cert's OID 1.3.6.1.4.1.57264.1.13 (SourceRepositoryDigest)
	// must match — this binds the attestation to a specific tree state and
	// prevents a stolen OIDC token from being reused on a different commit.
	ExpectedCommitSHA string

	// Identity is the SAN allowlist + repository URI the certificate must
	// match. The verifier accepts any listed ref, so releases from either
	// v3-beta or main pass.
	Identity Identity
}

// Verify runs full Sigstore verification on the bundle: signature validity,
// transparency log inclusion, integrated timestamps, artifact digest match,
// certificate identity (SAN + source repo digest binding). Fail-closed on any
// step. Callers should treat a nil return as "safe to execute this file".
//
// The trustedMaterial argument is threaded in to make CI guard tests possible;
// production callers should pass LoadEmbeddedTrustedMaterial() so verification
// stays offline and deterministic across dispatcher invocations.
func Verify(trustedMaterial root.TrustedMaterial, opts VerifyOptions) error {
	if err := opts.validate(); err != nil {
		return err
	}

	digestBytes, err := hex.DecodeString(opts.AssetDigest)
	if err != nil {
		return fmt.Errorf("%w: asset digest must be hex sha256: %v", ErrVerificationFailed, err)
	}
	if len(digestBytes) != 32 {
		return fmt.Errorf("%w: asset digest must be 32 bytes (sha256), got %d", ErrVerificationFailed, len(digestBytes))
	}

	var b bundle.Bundle
	if err := b.UnmarshalJSON(opts.BundleData); err != nil {
		return fmt.Errorf("%w: %v", ErrMalformedBundle, err)
	}

	verifier, err := verify.NewVerifier(trustedMaterial,
		verify.WithTransparencyLog(1),
		verify.WithIntegratedTimestamps(1),
		verify.WithSignedCertificateTimestamps(1))
	if err != nil {
		return fmt.Errorf("%w: build verifier: %v", ErrVerificationFailed, err)
	}

	identities, err := buildCertificateIdentities(opts.Identity, opts.ExpectedCommitSHA)
	if err != nil {
		return fmt.Errorf("%w: build identities: %v", ErrVerificationFailed, err)
	}

	policyOpts := make([]verify.PolicyOption, 0, len(identities))
	for _, id := range identities {
		policyOpts = append(policyOpts, verify.WithCertificateIdentity(id))
	}

	policy := verify.NewPolicy(verify.WithArtifactDigest("sha256", digestBytes), policyOpts...)

	if _, err := verifier.Verify(&b, policy); err != nil {
		return fmt.Errorf("%w: %v", ErrVerificationFailed, err)
	}
	return nil
}

// SubjectsOptions parameterizes VerifySubjects. Unlike VerifyOptions there is
// no AssetDigest — the whole point of VerifySubjects is to obtain the digests
// of the release's assets from the verified statement, so the caller does not
// know them ahead of time.
type SubjectsOptions struct {
	BundleData        []byte
	ExpectedCommitSHA string
	Identity          Identity
}

// VerifySubjects performs full Sigstore verification of the bundle (signature,
// transparency log, integrated timestamps, SCT, certificate identity, source
// repository digest binding) and returns a map of subject filename → lowercase
// hex-encoded SHA-256 digest extracted from the verified in-toto statement.
//
// This is the "manifest" surface B4 uses: the dispatcher pins a set of trusted
// archive digests here, then hands the manifest to install.sh so the shell
// script can compare against a digest that is cryptographically bound to the
// release commit, rather than the sibling `.sha256` file which ships from the
// same origin as the archive and thus offers no authenticity guarantee.
//
// The sigstore-go policy option is called WithoutArtifactUnsafe — the name is
// alarming but correct here: verification without a caller-provided artifact
// is unsafe when the caller then uses the certificate for something else
// (e.g. claiming a local file is trusted). Here the "artifact" IS the subject
// list we return, and callers must not skip the second-stage digest comparison
// against the specific asset they downloaded. Verify() still enforces artifact
// binding for the installer script path.
func VerifySubjects(trustedMaterial root.TrustedMaterial, opts SubjectsOptions) (map[string]string, error) {
	if len(opts.BundleData) == 0 {
		return nil, fmt.Errorf("%w: BundleData required", ErrVerificationFailed)
	}
	if !isHexCommitSHA(opts.ExpectedCommitSHA) {
		return nil, fmt.Errorf("%w: ExpectedCommitSHA must be 40-char hex", ErrVerificationFailed)
	}
	if opts.Identity.Repository == "" || opts.Identity.WorkflowPath == "" || len(opts.Identity.Refs) == 0 {
		return nil, fmt.Errorf("%w: Identity.Repository, WorkflowPath, and at least one Ref required", ErrVerificationFailed)
	}

	var b bundle.Bundle
	if err := b.UnmarshalJSON(opts.BundleData); err != nil {
		return nil, fmt.Errorf("%w: %v", ErrMalformedBundle, err)
	}

	verifier, err := verify.NewVerifier(trustedMaterial,
		verify.WithTransparencyLog(1),
		verify.WithIntegratedTimestamps(1),
		verify.WithSignedCertificateTimestamps(1))
	if err != nil {
		return nil, fmt.Errorf("%w: build verifier: %v", ErrVerificationFailed, err)
	}

	identities, err := buildCertificateIdentities(opts.Identity, opts.ExpectedCommitSHA)
	if err != nil {
		return nil, fmt.Errorf("%w: build identities: %v", ErrVerificationFailed, err)
	}

	policyOpts := make([]verify.PolicyOption, 0, len(identities))
	for _, id := range identities {
		policyOpts = append(policyOpts, verify.WithCertificateIdentity(id))
	}

	policy := verify.NewPolicy(verify.WithoutArtifactUnsafe(), policyOpts...)

	result, err := verifier.Verify(&b, policy)
	if err != nil {
		return nil, fmt.Errorf("%w: %v", ErrVerificationFailed, err)
	}
	if result == nil || result.Statement == nil {
		return nil, fmt.Errorf("%w: verified bundle had no statement", ErrVerificationFailed)
	}
	subjects := make(map[string]string, len(result.Statement.Subject))
	for _, subject := range result.Statement.Subject {
		if subject == nil {
			continue
		}
		hex, ok := subject.Digest["sha256"]
		if !ok || hex == "" {
			continue
		}
		subjects[subject.Name] = hex
	}
	if len(subjects) == 0 {
		return nil, fmt.Errorf("%w: verified bundle had no sha256 subjects", ErrVerificationFailed)
	}
	return subjects, nil
}

func (o VerifyOptions) validate() error {
	if o.AssetDigest == "" {
		return fmt.Errorf("%w: AssetDigest required", ErrVerificationFailed)
	}
	if len(o.BundleData) == 0 {
		return fmt.Errorf("%w: BundleData required", ErrVerificationFailed)
	}
	if !isHexCommitSHA(o.ExpectedCommitSHA) {
		return fmt.Errorf("%w: ExpectedCommitSHA must be 40-char hex", ErrVerificationFailed)
	}
	if o.Identity.Repository == "" || o.Identity.WorkflowPath == "" || len(o.Identity.Refs) == 0 {
		return fmt.Errorf("%w: Identity.Repository, WorkflowPath, and at least one Ref required", ErrVerificationFailed)
	}
	return nil
}

func isHexCommitSHA(s string) bool {
	if len(s) != 40 {
		return false
	}
	for i := 0; i < len(s); i++ {
		c := s[i]
		if (c < '0' || c > '9') && (c < 'a' || c > 'f') {
			return false
		}
	}
	return true
}

func buildCertificateIdentities(id Identity, expectedCommitSHA string) (verify.CertificateIdentities, error) {
	sans := id.SubjectAlternativeNames()
	if len(sans) == 0 {
		return nil, fmt.Errorf("identity has no refs")
	}
	identities := make(verify.CertificateIdentities, 0, len(sans))
	issuerMatcher, err := verify.NewIssuerMatcher("https://token.actions.githubusercontent.com", "")
	if err != nil {
		return nil, err
	}
	for _, san := range sans {
		sanMatcher, err := verify.NewSANMatcher(san, "")
		if err != nil {
			return nil, fmt.Errorf("build SAN matcher: %w", err)
		}
		identities = append(identities, verify.CertificateIdentity{
			SubjectAlternativeName: sanMatcher,
			Issuer:                 issuerMatcher,
			Extensions: certificate.Extensions{
				SourceRepositoryURI:    id.SourceRepositoryURI(),
				SourceRepositoryDigest: expectedCommitSHA,
			},
		})
	}
	if len(identities) == 0 {
		return nil, fmt.Errorf("identity produced no matchers")
	}
	return identities, nil
}
