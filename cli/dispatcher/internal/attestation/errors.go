package attestation

import "errors"

// Sentinel errors let callers distinguish network trouble from a real signature
// or identity mismatch so update.go and dispatcher_download.go can render
// distinct messages ("update to a newer CLI" vs "release may be compromised").
var (
	// ErrBundleFetch reports any failure retrieving <asset>.sigstore.json
	// (network error, 4xx/5xx, empty body). Fail-closed at the call site.
	ErrBundleFetch = errors.New("attestation bundle fetch failed")

	// ErrTagRefFetch reports failure resolving a release tag to its commit
	// SHA via the GitHub git-refs API. Fail-closed at the call site.
	ErrTagRefFetch = errors.New("release tag commit SHA lookup failed")

	// ErrMalformedBundle reports a bundle that could not be parsed as a
	// Sigstore protobuf JSON.
	ErrMalformedBundle = errors.New("attestation bundle is malformed")

	// ErrVerificationFailed reports Sigstore signature or policy failure —
	// digest mismatch, identity mismatch, tlog absent, timestamp missing.
	// This is the signal that a release asset may have been tampered with.
	ErrVerificationFailed = errors.New("attestation verification failed")
)
