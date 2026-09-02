package attestation

import (
	"context"
	"errors"
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/githubapi"
)

// happyFixture returns fixture values pulled from a real backfilled
// dispatcher-v3.0.1-beta.12 arm64 tarball attestation. Verifier tests run
// offline against the embedded trusted_root.json so they stay deterministic.
type happyFixture struct {
	bundle    []byte
	digest    string
	commitSHA string
	identity  Identity
}

func loadHappyFixture(t *testing.T) happyFixture {
	t.Helper()
	fixtureDir := filepath.Join("testdata")
	bundleBytes, err := os.ReadFile(filepath.Join(fixtureDir, "happy_bundle.json"))
	if err != nil {
		t.Fatalf("read happy_bundle.json: %v", err)
	}
	digest := readOneLine(t, filepath.Join(fixtureDir, "happy_asset_digest.txt"))
	commitSHA := readOneLine(t, filepath.Join(fixtureDir, "happy_commit_sha.txt"))
	return happyFixture{
		bundle:    bundleBytes,
		digest:    digest,
		commitSHA: commitSHA,
		identity: Identity{
			Repository:   "hatayama/unity-cli-loop",
			WorkflowPath: ".github/workflows/dispatcher-publish.yml",
			Refs:         []string{"refs/heads/v3-beta", "refs/heads/main"},
		},
	}
}

func readOneLine(t *testing.T, path string) string {
	t.Helper()
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("read %s: %v", path, err)
	}
	return strings.TrimSpace(string(data))
}

// Verifies that a real bundle + digest + commit SHA + allowlisted SAN passes.
func TestVerify_HappyPath(t *testing.T) {
	f := loadHappyFixture(t)
	trusted, err := LoadEmbeddedTrustedMaterial()
	if err != nil {
		t.Fatalf("load trusted root: %v", err)
	}
	err = Verify(trusted, VerifyOptions{
		AssetDigest:       f.digest,
		BundleData:        f.bundle,
		ExpectedCommitSHA: f.commitSHA,
		Identity:          f.identity,
	})
	if err != nil {
		t.Fatalf("expected happy path to verify, got: %v", err)
	}
}

// Verifies VerifySubjects returns the release's per-asset SHA-256 digests when the bundle is valid.
func TestVerifySubjects_HappyPath(t *testing.T) {
	f := loadHappyFixture(t)
	trusted, err := LoadEmbeddedTrustedMaterial()
	if err != nil {
		t.Fatalf("load trusted root: %v", err)
	}
	subjects, err := VerifySubjects(trusted, SubjectsOptions{
		BundleData:        f.bundle,
		ExpectedCommitSHA: f.commitSHA,
		Identity:          f.identity,
	})
	if err != nil {
		t.Fatalf("expected VerifySubjects to succeed, got: %v", err)
	}
	if len(subjects) == 0 {
		t.Fatal("expected at least one subject digest")
	}
	// The fixture digest belongs to the arm64 archive subject — confirm the map includes it.
	found := false
	for name, hex := range subjects {
		if hex == f.digest {
			if !strings.Contains(name, "arm64") {
				t.Fatalf("expected arm64-shaped subject name for the fixture digest, got %q", name)
			}
			found = true
		}
	}
	if !found {
		t.Fatalf("expected fixture digest %s to appear in subjects map: %v", f.digest, subjects)
	}
}

// Verifies VerifySubjects fail-closed when the certificate's Source Repository Digest differs from ExpectedCommitSHA.
func TestVerifySubjects_CommitMismatchFailsClosed(t *testing.T) {
	f := loadHappyFixture(t)
	trusted, err := LoadEmbeddedTrustedMaterial()
	if err != nil {
		t.Fatalf("load trusted root: %v", err)
	}
	badCommit := "1111111111111111111111111111111111111111"
	_, err = VerifySubjects(trusted, SubjectsOptions{
		BundleData:        f.bundle,
		ExpectedCommitSHA: badCommit,
		Identity:          f.identity,
	})
	if err == nil {
		t.Fatal("expected commit SHA mismatch to fail, got nil")
	}
	if !errors.Is(err, ErrVerificationFailed) {
		t.Fatalf("expected ErrVerificationFailed, got %v", err)
	}
}

// Characterizes VerifySubjects fail-closed input checks that run before Sigstore verification.
func TestVerifySubjects_RejectsIncompleteOptions(t *testing.T) {
	f := loadHappyFixture(t)
	trusted, err := LoadEmbeddedTrustedMaterial()
	if err != nil {
		t.Fatalf("load trusted root: %v", err)
	}

	testCases := []struct {
		name    string
		opts    SubjectsOptions
		wantErr string
	}{
		{
			name: "empty bundle",
			opts: SubjectsOptions{
				ExpectedCommitSHA: f.commitSHA,
				Identity:          f.identity,
			},
			wantErr: "attestation verification failed: BundleData required",
		},
		{
			name: "wrong length commit",
			opts: SubjectsOptions{
				BundleData:        f.bundle,
				ExpectedCommitSHA: "not-a-40-char-hex-commit-sha",
				Identity:          f.identity,
			},
			wantErr: "attestation verification failed: ExpectedCommitSHA must be 40-char hex",
		},
		{
			name: "non hex commit",
			opts: SubjectsOptions{
				BundleData:        f.bundle,
				ExpectedCommitSHA: strings.Repeat("g", 40),
				Identity:          f.identity,
			},
			wantErr: "attestation verification failed: ExpectedCommitSHA must be 40-char hex",
		},
		{
			name: "missing repository",
			opts: SubjectsOptions{
				BundleData:        f.bundle,
				ExpectedCommitSHA: f.commitSHA,
				Identity: Identity{
					WorkflowPath: f.identity.WorkflowPath,
					Refs:         f.identity.Refs,
				},
			},
			wantErr: "attestation verification failed: Identity.Repository, WorkflowPath, and at least one Ref required",
		},
		{
			name: "missing workflow path",
			opts: SubjectsOptions{
				BundleData:        f.bundle,
				ExpectedCommitSHA: f.commitSHA,
				Identity: Identity{
					Repository: f.identity.Repository,
					Refs:       f.identity.Refs,
				},
			},
			wantErr: "attestation verification failed: Identity.Repository, WorkflowPath, and at least one Ref required",
		},
		{
			name: "missing refs",
			opts: SubjectsOptions{
				BundleData:        f.bundle,
				ExpectedCommitSHA: f.commitSHA,
				Identity: Identity{
					Repository:   f.identity.Repository,
					WorkflowPath: f.identity.WorkflowPath,
				},
			},
			wantErr: "attestation verification failed: Identity.Repository, WorkflowPath, and at least one Ref required",
		},
	}

	for _, testCase := range testCases {
		t.Run(testCase.name, func(t *testing.T) {
			_, verifyErr := VerifySubjects(trusted, testCase.opts)
			if verifyErr == nil {
				t.Fatal("expected incomplete options to fail")
			}
			if !errors.Is(verifyErr, ErrVerificationFailed) {
				t.Fatalf("expected ErrVerificationFailed, got %v", verifyErr)
			}
			if verifyErr.Error() != testCase.wantErr {
				t.Fatalf("error = %q, want %q", verifyErr.Error(), testCase.wantErr)
			}
		})
	}
}

// Verifies VerifySubjects rejects bundles it cannot parse as Sigstore JSON.
func TestVerifySubjects_MalformedBundleFailsClosed(t *testing.T) {
	f := loadHappyFixture(t)
	trusted, err := LoadEmbeddedTrustedMaterial()
	if err != nil {
		t.Fatalf("load trusted root: %v", err)
	}
	_, err = VerifySubjects(trusted, SubjectsOptions{
		BundleData:        []byte("{not-a-bundle}"),
		ExpectedCommitSHA: f.commitSHA,
		Identity:          f.identity,
	})
	if err == nil {
		t.Fatal("expected malformed bundle to fail, got nil")
	}
	if !errors.Is(err, ErrMalformedBundle) {
		t.Fatalf("expected ErrMalformedBundle, got %v", err)
	}
}

// Verifies fail-closed when the local asset digest is not one of the bundle's subjects.
func TestVerify_DigestMismatch(t *testing.T) {
	f := loadHappyFixture(t)
	trusted, err := LoadEmbeddedTrustedMaterial()
	if err != nil {
		t.Fatalf("load trusted root: %v", err)
	}
	wrongDigest := "0000000000000000000000000000000000000000000000000000000000000000"
	err = Verify(trusted, VerifyOptions{
		AssetDigest:       wrongDigest,
		BundleData:        f.bundle,
		ExpectedCommitSHA: f.commitSHA,
		Identity:          f.identity,
	})
	if err == nil {
		t.Fatalf("expected digest mismatch to fail, got nil")
	}
	if !errors.Is(err, ErrVerificationFailed) {
		t.Fatalf("expected ErrVerificationFailed, got: %v", err)
	}
}

// Verifies fail-closed when SAN allowlist does not include the cert's signer.
func TestVerify_IdentityMismatch_BadSAN(t *testing.T) {
	f := loadHappyFixture(t)
	trusted, err := LoadEmbeddedTrustedMaterial()
	if err != nil {
		t.Fatalf("load trusted root: %v", err)
	}
	badIdentity := f.identity
	badIdentity.Repository = "attacker/evil-fork"
	err = Verify(trusted, VerifyOptions{
		AssetDigest:       f.digest,
		BundleData:        f.bundle,
		ExpectedCommitSHA: f.commitSHA,
		Identity:          badIdentity,
	})
	if err == nil {
		t.Fatalf("expected SAN mismatch to fail, got nil")
	}
	if !errors.Is(err, ErrVerificationFailed) {
		t.Fatalf("expected ErrVerificationFailed, got: %v", err)
	}
}

// Verifies fail-closed when the expected commit SHA does not match cert's OID .13.
func TestVerify_IdentityMismatch_BadCommitSHA(t *testing.T) {
	f := loadHappyFixture(t)
	trusted, err := LoadEmbeddedTrustedMaterial()
	if err != nil {
		t.Fatalf("load trusted root: %v", err)
	}
	wrongCommit := "0000000000000000000000000000000000000000"
	err = Verify(trusted, VerifyOptions{
		AssetDigest:       f.digest,
		BundleData:        f.bundle,
		ExpectedCommitSHA: wrongCommit,
		Identity:          f.identity,
	})
	if err == nil {
		t.Fatalf("expected commit SHA mismatch to fail, got nil")
	}
	if !errors.Is(err, ErrVerificationFailed) {
		t.Fatalf("expected ErrVerificationFailed, got: %v", err)
	}
}

// Verifies fail-closed when the bundle bytes are malformed.
func TestVerify_MalformedBundle(t *testing.T) {
	f := loadHappyFixture(t)
	trusted, err := LoadEmbeddedTrustedMaterial()
	if err != nil {
		t.Fatalf("load trusted root: %v", err)
	}
	err = Verify(trusted, VerifyOptions{
		AssetDigest:       f.digest,
		BundleData:        []byte("this is not a sigstore bundle"),
		ExpectedCommitSHA: f.commitSHA,
		Identity:          f.identity,
	})
	if err == nil {
		t.Fatalf("expected malformed bundle to fail, got nil")
	}
	if !errors.Is(err, ErrMalformedBundle) {
		t.Fatalf("expected ErrMalformedBundle, got: %v", err)
	}
}

// Verifies FetchBundle wraps a 404 (bundle-missing) into ErrBundleFetch.
func TestFetchBundle_MissingReturns404(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		http.NotFound(w, r)
	}))
	defer server.Close()

	_, err := FetchBundle(context.Background(), server.URL+"/whatever.sigstore.json")
	if err == nil {
		t.Fatalf("expected 404 to fail, got nil")
	}
	if !errors.Is(err, ErrBundleFetch) {
		t.Fatalf("expected ErrBundleFetch, got: %v", err)
	}
}

// Verifies FetchBundle fails closed on 403 (which GitHub returns during rate
// limiting and abuse detection). We must never treat 403 as "no bundle
// available so skip", or an attacker can DoS attestation lookups to strip
// verification.
func TestFetchBundle_403FailsClosed(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusForbidden)
	}))
	defer server.Close()

	_, err := FetchBundle(context.Background(), server.URL+"/whatever.sigstore.json")
	if err == nil {
		t.Fatalf("expected 403 to fail-closed, got nil")
	}
	if !errors.Is(err, ErrBundleFetch) {
		t.Fatalf("expected ErrBundleFetch, got: %v", err)
	}
}

// Verifies FetchBundle fails closed on 429 (rate limit).
func TestFetchBundle_429FailsClosed(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusTooManyRequests)
	}))
	defer server.Close()

	_, err := FetchBundle(context.Background(), server.URL+"/whatever.sigstore.json")
	if err == nil {
		t.Fatalf("expected 429 to fail-closed, got nil")
	}
	if !errors.Is(err, ErrBundleFetch) {
		t.Fatalf("expected ErrBundleFetch, got: %v", err)
	}
}

// Verifies FetchBundle fails when the body is empty. Empty response from a
// caching CDN would otherwise let a stripped bundle look like "downloaded OK".
func TestFetchBundle_EmptyBodyFails(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
	}))
	defer server.Close()

	_, err := FetchBundle(context.Background(), server.URL+"/whatever.sigstore.json")
	if err == nil {
		t.Fatalf("expected empty body to fail, got nil")
	}
	if !errors.Is(err, ErrBundleFetch) {
		t.Fatalf("expected ErrBundleFetch, got: %v", err)
	}
}

// Verifies FetchTagCommitSHA parses a lightweight tag ref API response.
func TestFetchTagCommitSHA_Lightweight(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = fmt.Fprintln(w, `{"object":{"sha":"1eb1ebb9841b1bcb8fc7dec3fa282568a1c31a4f","type":"commit"}}`)
	}))
	defer server.Close()

	original := githubAPIBase()
	setGithubAPIBase(server.URL)
	defer setGithubAPIBase(original)

	sha, err := FetchTagCommitSHA(context.Background(), "hatayama/unity-cli-loop", "dispatcher-v3.0.1-beta.12")
	if err != nil {
		t.Fatalf("expected happy tag lookup, got: %v", err)
	}
	if sha != "1eb1ebb9841b1bcb8fc7dec3fa282568a1c31a4f" {
		t.Fatalf("unexpected sha: %s", sha)
	}
}

// Verifies FetchTagCommitSHA follows one indirection when the ref points at
// an annotated tag object rather than a commit.
func TestFetchTagCommitSHA_Annotated(t *testing.T) {
	var hits int
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		hits++
		switch hits {
		case 1:
			_, _ = fmt.Fprintln(w, `{"object":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","type":"tag"}}`)
		case 2:
			_, _ = fmt.Fprintln(w, `{"object":{"sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","type":"commit"}}`)
		default:
			t.Errorf("unexpected extra call %d", hits)
		}
	}))
	defer server.Close()

	original := githubAPIBase()
	setGithubAPIBase(server.URL)
	defer setGithubAPIBase(original)

	sha, err := FetchTagCommitSHA(context.Background(), "hatayama/unity-cli-loop", "annotated-tag")
	if err != nil {
		t.Fatalf("expected annotated tag lookup to resolve, got: %v", err)
	}
	if sha != "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" {
		t.Fatalf("unexpected sha: %s", sha)
	}
	if hits != 2 {
		t.Fatalf("expected 2 API hits (ref then tag), got %d", hits)
	}
}

// Verifies FetchTagCommitSHA fails closed on network / server errors.
func TestFetchTagCommitSHA_ServerError(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		http.Error(w, "boom", http.StatusInternalServerError)
	}))
	defer server.Close()

	original := githubAPIBase()
	setGithubAPIBase(server.URL)
	defer setGithubAPIBase(original)

	_, err := FetchTagCommitSHA(context.Background(), "hatayama/unity-cli-loop", "any")
	if err == nil {
		t.Fatalf("expected server error to fail-closed")
	}
	if !errors.Is(err, ErrTagRefFetch) {
		t.Fatalf("expected ErrTagRefFetch, got: %v", err)
	}
}

// Verifies FetchTagCommitSHA surfaces an exhausted GitHub quota as a typed
// rate-limit error while still failing closed under ErrTagRefFetch.
func TestFetchTagCommitSHA_RateLimited(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("X-RateLimit-Remaining", "0")
		w.Header().Set("X-RateLimit-Reset", "1790000000")
		http.Error(w, `{"message":"API rate limit exceeded"}`, http.StatusForbidden)
	}))
	defer server.Close()

	original := githubAPIBase()
	setGithubAPIBase(server.URL)
	defer setGithubAPIBase(original)

	_, err := FetchTagCommitSHA(context.Background(), "hatayama/unity-cli-loop", "any")
	if !errors.Is(err, ErrTagRefFetch) {
		t.Fatalf("expected ErrTagRefFetch, got: %v", err)
	}
	var rateLimit githubapi.RateLimitError
	if !errors.As(err, &rateLimit) {
		t.Fatalf("expected RateLimitError, got: %v", err)
	}
	if rateLimit.ResetAt.Unix() != 1790000000 {
		t.Fatalf("unexpected reset time: %v", rateLimit.ResetAt)
	}
}
