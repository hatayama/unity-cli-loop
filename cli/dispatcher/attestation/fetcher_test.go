package attestation

import (
	"context"
	"errors"
	"fmt"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

// useGithubAPIBase points tag-ref lookups at baseURL for the duration of the test.
func useGithubAPIBase(t *testing.T, baseURL string) {
	t.Helper()
	original := githubAPIBase()
	setGithubAPIBase(baseURL)
	t.Cleanup(func() { setGithubAPIBase(original) })
}

// serveTagRefs answers each tag-ref request with the next body in order.
func serveTagRefs(t *testing.T, bodies ...string) *httptest.Server {
	t.Helper()
	calls := 0
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if calls >= len(bodies) {
			t.Errorf("unexpected extra call to %s", r.URL.Path)
			http.Error(w, "unexpected", http.StatusInternalServerError)
			return
		}
		w.Header().Set("Content-Type", "application/json")
		_, _ = fmt.Fprintln(w, bodies[calls])
		calls++
	}))
	t.Cleanup(server.Close)
	useGithubAPIBase(t, server.URL)
	return server
}

// Verifies the bundle URL points at the release asset with the sigstore suffix, path-escaped.
func TestBundleAssetURLEscapesTheAssetName(t *testing.T) {
	got := BundleAssetURL("owner/repo", "dispatcher-v3.1.0", "uloop darwin.tar.gz")

	want := "https://github.com/owner/repo/releases/download/dispatcher-v3.1.0/uloop%20darwin.tar.gz.sigstore.json"
	if got != want {
		t.Fatalf("BundleAssetURL = %q, want %q", got, want)
	}
}

// Verifies a successful bundle download returns the body as is.
func TestFetchBundleReturnsTheBody(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = w.Write([]byte(`{"bundle":true}`))
	}))
	defer server.Close()

	body, err := FetchBundle(context.Background(), server.URL+"/asset.sigstore.json")
	if err != nil || string(body) != `{"bundle":true}` {
		t.Fatalf("body=%q err=%v", body, err)
	}
}

// Verifies each bundle download failure before a status is known fails closed with its own cause.
func TestFetchBundleFailsClosedOnTransportErrors(t *testing.T) {
	t.Run("invalid URL", func(t *testing.T) {
		_, err := FetchBundle(context.Background(), "http://bad host/asset")
		if !errors.Is(err, ErrBundleFetch) || !strings.Contains(err.Error(), "build request") {
			t.Fatalf("err = %v, want a build-request ErrBundleFetch", err)
		}
	})
	t.Run("connection refused", func(t *testing.T) {
		server := httptest.NewServer(http.NotFoundHandler())
		serverURL := server.URL
		server.Close()

		_, err := FetchBundle(context.Background(), serverURL+"/asset")
		if !errors.Is(err, ErrBundleFetch) || strings.Contains(err.Error(), "status") || strings.Contains(err.Error(), "build request") {
			t.Fatalf("err = %v, want a transport ErrBundleFetch", err)
		}
	})
	t.Run("truncated body", func(t *testing.T) {
		server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			w.Header().Set("Content-Length", "100")
			_, _ = w.Write([]byte("short"))
		}))
		defer server.Close()

		_, err := FetchBundle(context.Background(), server.URL+"/asset")
		if !errors.Is(err, ErrBundleFetch) || !strings.Contains(err.Error(), "read body") {
			t.Fatalf("err = %v, want a read-body ErrBundleFetch", err)
		}
	})
}

// Verifies tag resolution rejects a ref that does not end at a commit, and a commit SHA that is
// not 40 hex characters.
func TestFetchTagCommitSHARejectsUnexpectedRefs(t *testing.T) {
	t.Run("tree object", func(t *testing.T) {
		serveTagRefs(t, `{"object":{"sha":"1eb1ebb9841b1bcb8fc7dec3fa282568a1c31a4f","type":"tree"}}`)

		_, err := FetchTagCommitSHA(context.Background(), "owner/repo", "dispatcher-v3.1.0")
		if !errors.Is(err, ErrTagRefFetch) || !strings.Contains(err.Error(), `unexpected object type "tree"`) {
			t.Fatalf("err = %v", err)
		}
	})
	t.Run("short SHA", func(t *testing.T) {
		serveTagRefs(t, `{"object":{"sha":"1eb1ebb","type":"commit"}}`)

		_, err := FetchTagCommitSHA(context.Background(), "owner/repo", "dispatcher-v3.1.0")
		if !errors.Is(err, ErrTagRefFetch) || !strings.Contains(err.Error(), `bad commit SHA "1eb1ebb"`) {
			t.Fatalf("err = %v", err)
		}
	})
	t.Run("annotated tag lookup fails", func(t *testing.T) {
		serveTagRefs(t, `{"object":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","type":"tag"}}`, `not json`)

		_, err := FetchTagCommitSHA(context.Background(), "owner/repo", "dispatcher-v3.1.0")
		if !errors.Is(err, ErrTagRefFetch) || !strings.Contains(err.Error(), "decode payload") {
			t.Fatalf("err = %v", err)
		}
	})
}

// Verifies each tag-ref lookup failure before a payload is decoded fails closed with its own cause.
func TestFetchTagCommitSHAFailsClosedOnTransportErrors(t *testing.T) {
	t.Run("invalid base URL", func(t *testing.T) {
		useGithubAPIBase(t, "http://bad host")

		_, err := FetchTagCommitSHA(context.Background(), "owner/repo", "dispatcher-v3.1.0")
		if !errors.Is(err, ErrTagRefFetch) || !strings.Contains(err.Error(), "build request") {
			t.Fatalf("err = %v, want a build-request ErrTagRefFetch", err)
		}
	})
	t.Run("connection refused", func(t *testing.T) {
		server := httptest.NewServer(http.NotFoundHandler())
		useGithubAPIBase(t, server.URL)
		server.Close()

		_, err := FetchTagCommitSHA(context.Background(), "owner/repo", "dispatcher-v3.1.0")
		if !errors.Is(err, ErrTagRefFetch) || strings.Contains(err.Error(), "status") || strings.Contains(err.Error(), "build request") {
			t.Fatalf("err = %v, want a transport ErrTagRefFetch", err)
		}
	})
}

// Verifies the asset name is the last path element, without any query, for both URLs and bare
// paths, and falls back to the raw value when the URL cannot be parsed.
func TestAssetNameFromReleaseAsset(t *testing.T) {
	cases := map[string]string{
		"https://github.com/owner/repo/releases/download/v1/uloop.tar.gz?token=x": "uloop.tar.gz",
		"dist/uloop.zip":   "uloop.zip",
		"%zz/broken/uloop": "uloop",
	}
	for input, want := range cases {
		if got := AssetNameFromReleaseAsset(input); got != want {
			t.Fatalf("AssetNameFromReleaseAsset(%q) = %q, want %q", input, got, want)
		}
	}
}
