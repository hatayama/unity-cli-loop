package dispatcher

import (
	"bytes"
	"context"
	"errors"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/dispatcher/attestation"
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

const attestationTestCommitSHA = "0123456789abcdef0123456789abcdef01234567"

// attestationTestServer answers the bundle and git-ref lookups the attestation
// pipeline makes and records every requested URL, so tests can assert the
// release tag and asset were wired through instead of being dropped.
type attestationTestServer struct {
	bundle        []byte
	tagStatusCode int
	requested     []string
}

// stubAttestationHTTP routes every attestation fetch to an in-memory server, so no request reaches GitHub.
func stubAttestationHTTP(t *testing.T, bundle []byte, tagStatusCode int) *attestationTestServer {
	t.Helper()
	server := &attestationTestServer{bundle: bundle, tagStatusCode: tagStatusCode}
	previous := attestation.DefaultHTTPClient
	t.Cleanup(func() {
		attestation.DefaultHTTPClient = previous
	})
	attestation.DefaultHTTPClient = &http.Client{Transport: dispatcherRoundTripFunc(server.respond)}
	return server
}

func (server *attestationTestServer) respond(request *http.Request) (*http.Response, error) {
	server.requested = append(server.requested, request.URL.String())
	statusCode := http.StatusNotFound
	body := []byte{}
	switch {
	case strings.HasSuffix(request.URL.Path, ".sigstore.json") && server.bundle != nil:
		statusCode = http.StatusOK
		body = server.bundle
	case strings.Contains(request.URL.Path, "/git/ref/tags/"):
		statusCode = server.tagStatusCode
		body = []byte(`{"object":{"sha":"` + attestationTestCommitSHA + `","type":"commit"}}`)
	}
	return &http.Response{
		StatusCode: statusCode,
		Status:     http.StatusText(statusCode),
		Body:       io.NopCloser(bytes.NewReader(body)),
		Header:     http.Header{},
	}, nil
}

func TestDefaultVerifyReleaseAssetAttestationReportsMissingAsset(t *testing.T) {
	// Verifies an unreadable local asset fails before any bundle is fetched.
	server := stubAttestationHTTP(t, []byte("{}"), http.StatusOK)
	assetPath := filepath.Join(t.TempDir(), "missing")

	err := defaultVerifyReleaseAssetAttestation(context.Background(), "dispatcher-v1.0.0", "https://example.test/asset", assetPath, attestationDispatcherPublishWorkflowPath)

	if err == nil || !strings.HasPrefix(err.Error(), "compute asset digest for attestation:") || !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("expected the digest error, got %v", err)
	}
	if len(server.requested) != 0 {
		t.Fatalf("no request may be made for a missing asset: %v", server.requested)
	}
}

func TestDefaultVerifyReleaseAssetAttestationFailsClosed(t *testing.T) {
	// Verifies a missing bundle, a failed tag lookup, and a bundle that does not verify each reject the asset.
	cases := []struct {
		name          string
		bundle        []byte
		tagStatusCode int
		wantErr       error
		wantRequests  []string
	}{
		{
			name:         "bundle missing",
			wantErr:      attestation.ErrBundleFetch,
			wantRequests: []string{"https://example.test/asset.sigstore.json"},
		},
		{
			name:          "tag lookup fails",
			bundle:        []byte("{}"),
			tagStatusCode: http.StatusNotFound,
			wantErr:       attestation.ErrTagRefFetch,
			wantRequests: []string{
				"https://example.test/asset.sigstore.json",
				"https://api.github.com/repos/" + dispatcherReleaseRepository + "/git/ref/tags/dispatcher-v1.0.0",
			},
		},
		{
			name:          "bundle does not verify",
			bundle:        []byte("{}"),
			tagStatusCode: http.StatusOK,
			wantErr:       attestation.ErrMalformedBundle,
			wantRequests: []string{
				"https://example.test/asset.sigstore.json",
				"https://api.github.com/repos/" + dispatcherReleaseRepository + "/git/ref/tags/dispatcher-v1.0.0",
			},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			server := stubAttestationHTTP(t, testCase.bundle, testCase.tagStatusCode)
			assetPath := filepath.Join(t.TempDir(), "asset")
			writeDispatcherTestFile(t, assetPath, "asset")

			err := defaultVerifyReleaseAssetAttestation(context.Background(), "dispatcher-v1.0.0", "https://example.test/asset", assetPath, attestationDispatcherPublishWorkflowPath)

			if !errors.Is(err, testCase.wantErr) {
				t.Fatalf("expected %v, got %v", testCase.wantErr, err)
			}
			if !reflect.DeepEqual(server.requested, testCase.wantRequests) {
				t.Fatalf("requests mismatch:\n got %v\nwant %v", server.requested, testCase.wantRequests)
			}
		})
	}
}
