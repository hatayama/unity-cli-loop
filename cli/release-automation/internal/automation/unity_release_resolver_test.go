package automation

import (
	"context"
	"io"
	"net/http"
	"net/http/httptest"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func TestSelectUnityReleasePicksFirstResultAsLatest(t *testing.T) {
	// What: a multi-result fixture ordered newest-first selects results[0] and its Linux TAR_XZ URL.
	body := readUnityReleaseFixture(t, "unity-release-latest-first.json")

	release, err := selectUnityRelease(body)
	if err != nil {
		t.Fatalf("expected fixture to parse: %v", err)
	}
	if release.Version != "6000.7.0a5" {
		t.Fatalf("expected first (newest) version, got %q", release.Version)
	}
	if release.Changeset != "bbbbbbbbbbbb" {
		t.Fatalf("expected first shortRevision, got %q", release.Changeset)
	}
	if release.EditorURL != "https://example.invalid/Unity-6000.7.0a5.tar.xz" {
		t.Fatalf("expected first Linux editor URL, got %q", release.EditorURL)
	}
}

func TestSelectUnityReleaseRejectsEmptyResults(t *testing.T) {
	// What: an empty results list is a resolver failure, not a blank GitHub output.
	body := readUnityReleaseFixture(t, "unity-release-empty.json")

	_, err := selectUnityRelease(body)
	if err == nil {
		t.Fatal("expected empty results to fail")
	}
	if !strings.Contains(err.Error(), "no unity editor releases") {
		t.Fatalf("expected empty-results message, got %v", err)
	}
}

func TestSelectUnityReleaseRejectsMissingLinuxEditorDownload(t *testing.T) {
	// What: a release without LINUX/X86_64/TAR_XZ is a resolver failure instead of a hand-built URL.
	body := readUnityReleaseFixture(t, "unity-release-missing-linux.json")

	_, err := selectUnityRelease(body)
	if err == nil {
		t.Fatal("expected missing Linux editor download to fail")
	}
	if !strings.Contains(err.Error(), "LINUX") {
		t.Fatalf("expected missing-download message, got %v", err)
	}
}

func TestResolveUnityReleaseFiltersBySeriesQuery(t *testing.T) {
	// What: the resolver sends one query whose version= parameter is the caller-supplied series, not a hardcoded 6000.7.
	var captured url.Values
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		captured = request.URL.Query()
		writer.Header().Set("Content-Type", "application/json")
		_, _ = writer.Write(readUnityReleaseFixture(t, "unity-release-latest-first.json"))
	}))
	t.Cleanup(server.Close)

	release, err := ResolveUnityRelease(context.Background(), ResolveUnityReleaseRequest{
		Series:     "6000.5",
		APIBaseURL: server.URL,
		HTTPClient: server.Client(),
	})
	if err != nil {
		t.Fatalf("expected series query to succeed: %v", err)
	}
	if captured.Get(unityReleaseQueryVersion) != "6000.5" {
		t.Fatalf("expected version query %q, got %q", "6000.5", captured.Get(unityReleaseQueryVersion))
	}
	if captured.Get(unityReleaseQueryOrder) != unityReleaseOrderReleaseDateDesc {
		t.Fatalf("expected order %q, got %q", unityReleaseOrderReleaseDateDesc, captured.Get(unityReleaseQueryOrder))
	}
	if captured.Get(unityReleaseQueryLimit) != unityReleaseLimitLatest {
		t.Fatalf("expected limit %q, got %q", unityReleaseLimitLatest, captured.Get(unityReleaseQueryLimit))
	}
	if release.Version != "6000.7.0a5" {
		t.Fatalf("expected parsed version from fixture, got %q", release.Version)
	}
}

func TestResolveUnityReleaseRejectsHTTPError(t *testing.T) {
	// What: a non-200 Unity Release API response fails the resolver with the status code.
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		writer.WriteHeader(http.StatusBadGateway)
	}))
	t.Cleanup(server.Close)

	_, err := ResolveUnityRelease(context.Background(), ResolveUnityReleaseRequest{
		Series:     "6000.7",
		APIBaseURL: server.URL,
		HTTPClient: server.Client(),
	})
	if err == nil {
		t.Fatal("expected HTTP error to fail")
	}
	if !strings.Contains(err.Error(), "502") {
		t.Fatalf("expected HTTP 502 in error, got %v", err)
	}
}

func TestResolveUnityReleaseHonorsContextDeadline(t *testing.T) {
	// What: a stalled Unity Release API fails when the caller context deadline expires.
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		<-request.Context().Done()
	}))
	t.Cleanup(server.Close)

	ctx, cancel := context.WithTimeout(context.Background(), 50*time.Millisecond)
	defer cancel()
	_, err := ResolveUnityRelease(ctx, ResolveUnityReleaseRequest{
		Series:     "6000.7",
		APIBaseURL: server.URL,
		HTTPClient: server.Client(),
	})
	if err == nil {
		t.Fatal("expected a deadline to fail a stalled request")
	}
}

func TestWriteGitHubOutputWritesVersionChangesetAndEditorURL(t *testing.T) {
	// What: resolver stdout is GITHUB_OUTPUT assignments for version, changeset, and editorUrl.
	builder := &strings.Builder{}
	err := WriteGitHubOutput(builder, UnityRelease{
		Version:   "6000.7.0a4",
		Changeset: "7305b6f6fd4f",
		EditorURL: "https://example.invalid/Unity-6000.7.0a4.tar.xz",
	})
	if err != nil {
		t.Fatalf("expected GitHub output write to succeed: %v", err)
	}

	got := builder.String()
	want := "version=6000.7.0a4\nchangeset=7305b6f6fd4f\neditorUrl=https://example.invalid/Unity-6000.7.0a4.tar.xz\n"
	if got != want {
		t.Fatalf("GitHub output mismatch\nwant %q\ngot  %q", want, got)
	}
}

func TestRunResolveUnityReleaseRequiresSeries(t *testing.T) {
	// What: the CLI rejects a missing --series instead of defaulting to a hardcoded Unity series.
	exitCode := RunResolveUnityRelease(io.Discard, io.Discard, []string{})
	if exitCode == 0 {
		t.Fatal("expected missing --series to exit non-zero")
	}
}

func readUnityReleaseFixture(t *testing.T, name string) []byte {
	t.Helper()
	body, err := os.ReadFile(filepath.Join("testdata", name))
	if err != nil {
		t.Fatalf("read fixture %s: %v", name, err)
	}
	return body
}
