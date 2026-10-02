package attestation

import (
	"encoding/json"
	"errors"
	"strings"
	"testing"

	"github.com/sigstore/sigstore-go/pkg/verify"
)

// Verifies Verify rejects incomplete options and malformed digests before it touches the bundle,
// each with its own message.
func TestVerifyRejectsIncompleteOptionsAndBadDigests(t *testing.T) {
	f := loadHappyFixture(t)
	trusted, err := LoadEmbeddedTrustedMaterial()
	if err != nil {
		t.Fatalf("load trusted root: %v", err)
	}
	valid := VerifyOptions{BundleData: f.bundle, AssetDigest: f.digest, ExpectedCommitSHA: f.commitSHA, Identity: f.identity}
	cases := []struct {
		name    string
		mutate  func(*VerifyOptions)
		wantErr string
	}{
		{name: "missing digest", mutate: func(o *VerifyOptions) { o.AssetDigest = "" }, wantErr: "AssetDigest required"},
		{name: "missing bundle", mutate: func(o *VerifyOptions) { o.BundleData = nil }, wantErr: "BundleData required"},
		{name: "bad commit", mutate: func(o *VerifyOptions) { o.ExpectedCommitSHA = "abc" }, wantErr: "ExpectedCommitSHA must be 40-char hex"},
		{name: "missing refs", mutate: func(o *VerifyOptions) { o.Identity.Refs = nil }, wantErr: "at least one Ref required"},
		{name: "non-hex digest", mutate: func(o *VerifyOptions) { o.AssetDigest = "zz" }, wantErr: "asset digest must be hex sha256"},
		{name: "short digest", mutate: func(o *VerifyOptions) { o.AssetDigest = "abcd" }, wantErr: "asset digest must be 32 bytes (sha256), got 2"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			opts := valid
			testCase.mutate(&opts)

			err := Verify(trusted, opts)
			if !errors.Is(err, ErrVerificationFailed) || !strings.Contains(err.Error(), testCase.wantErr) {
				t.Fatalf("err = %v, want ErrVerificationFailed containing %q", err, testCase.wantErr)
			}
		})
	}
}

// decodeVerificationResult builds a verification result from its JSON form, so the subject
// extraction can be tested without signing a new bundle.
func decodeVerificationResult(t *testing.T, raw string) *verify.VerificationResult {
	t.Helper()
	var result verify.VerificationResult
	if err := json.Unmarshal([]byte(raw), &result); err != nil {
		t.Fatalf("decode verification result: %v", err)
	}
	return &result
}

// Verifies subject extraction keeps only subjects with a sha256 digest and fails closed when a
// verified result has no statement or no usable subject.
func TestExtractVerifiedSubjects(t *testing.T) {
	t.Run("keeps sha256 subjects only", func(t *testing.T) {
		result := decodeVerificationResult(t, `{"statement":{"subject":[
			{"name":"uloop.tar.gz","digest":{"sha256":"abc123"}},
			{"name":"sha512-only","digest":{"sha512":"def456"}},
			{"name":"empty-sha256","digest":{"sha256":""}}
		]}}`)
		result.Statement.Subject = append(result.Statement.Subject, nil)

		subjects, err := extractVerifiedSubjects(result)
		if err != nil {
			t.Fatalf("extractVerifiedSubjects failed: %v", err)
		}
		if len(subjects) != 1 || subjects["uloop.tar.gz"] != "abc123" {
			t.Fatalf("subjects = %#v", subjects)
		}
	})
	t.Run("no statement", func(t *testing.T) {
		for _, result := range []*verify.VerificationResult{nil, {}} {
			_, err := extractVerifiedSubjects(result)
			if !errors.Is(err, ErrVerificationFailed) || !strings.Contains(err.Error(), "verified bundle had no statement") {
				t.Fatalf("err = %v", err)
			}
		}
	})
	t.Run("no sha256 subject", func(t *testing.T) {
		result := decodeVerificationResult(t, `{"statement":{"subject":[{"name":"a","digest":{"sha512":"x"}}]}}`)

		_, err := extractVerifiedSubjects(result)
		if !errors.Is(err, ErrVerificationFailed) || !strings.Contains(err.Error(), "verified bundle had no sha256 subjects") {
			t.Fatalf("err = %v", err)
		}
	})
}
