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

// unityReleaseRoundTripper serves a canned response without opening a connection.
type unityReleaseRoundTripper func(*http.Request) (*http.Response, error)

func (roundTripper unityReleaseRoundTripper) RoundTrip(request *http.Request) (*http.Response, error) {
	return roundTripper(request)
}

// unityReleaseFailingBody fails every read so the response body cannot be consumed.
type unityReleaseFailingBody struct{}

func (unityReleaseFailingBody) Read([]byte) (int, error) { return 0, io.ErrUnexpectedEOF }
func (unityReleaseFailingBody) Close() error             { return nil }

func TestResolveUnityReleaseDefaultsToTheUnityServicesAPI(t *testing.T) {
	// What: an empty API base URL resolves against the Unity services API with the release query.
	requestedURL := ""
	client := &http.Client{Transport: unityReleaseRoundTripper(func(request *http.Request) (*http.Response, error) {
		requestedURL = request.URL.String()
		return &http.Response{
			StatusCode: http.StatusOK,
			Body:       io.NopCloser(strings.NewReader(string(readUnityReleaseFixture(t, "unity-release-latest-first.json")))),
		}, nil
	})}

	release, err := ResolveUnityRelease(context.Background(), ResolveUnityReleaseRequest{Series: "6000.7", HTTPClient: client})
	if err != nil {
		t.Fatalf("ResolveUnityRelease failed: %v", err)
	}
	if release.Version == "" {
		t.Fatal("expected a resolved release")
	}
	if !strings.HasPrefix(requestedURL, defaultUnityReleaseAPIBaseURL+unityEditorReleasesPath+"?") ||
		!strings.Contains(requestedURL, "version=6000.7") {
		t.Fatalf("requested URL = %q", requestedURL)
	}
}

func TestResolveUnityReleaseUsesTheDefaultHTTPClient(t *testing.T) {
	// What: a request without an HTTP client still reaches the given API through the default client.
	body := readUnityReleaseFixture(t, "unity-release-latest-first.json")
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, _ *http.Request) {
		_, _ = writer.Write(body)
	}))
	defer server.Close()

	release, err := ResolveUnityRelease(context.Background(), ResolveUnityReleaseRequest{Series: "6000.7", APIBaseURL: server.URL})
	if err != nil {
		t.Fatalf("ResolveUnityRelease failed: %v", err)
	}
	if release.Version == "" || release.EditorURL == "" {
		t.Fatalf("release = %+v", release)
	}
}

func TestResolveUnityReleaseRejectsUnusableInputs(t *testing.T) {
	// What: a missing series, an unparsable base URL, and an unreadable response body each fail with a specific error.
	failingBodyClient := &http.Client{Transport: unityReleaseRoundTripper(func(*http.Request) (*http.Response, error) {
		return &http.Response{StatusCode: http.StatusOK, Body: unityReleaseFailingBody{}}, nil
	})}
	cases := []struct {
		name    string
		request ResolveUnityReleaseRequest
		wantErr string
	}{
		{"missing series", ResolveUnityReleaseRequest{}, "--series is required"},
		{"invalid base URL", ResolveUnityReleaseRequest{Series: "6000.7", APIBaseURL: "://missing-scheme"}, "parse unity release API base URL"},
		{"unreadable body", ResolveUnityReleaseRequest{Series: "6000.7", APIBaseURL: "https://example.invalid", HTTPClient: failingBodyClient}, "read unity release API response"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			_, err := ResolveUnityRelease(context.Background(), testCase.request)

			if err == nil || !strings.Contains(err.Error(), testCase.wantErr) {
				t.Fatalf("expected error containing %q, got %v", testCase.wantErr, err)
			}
		})
	}
}

func TestSelectUnityReleaseRejectsMalformedResults(t *testing.T) {
	// What: invalid JSON, a result without a version or a short revision, and a Linux archive without a URL are rejected.
	cases := []struct {
		name    string
		body    string
		wantErr string
	}{
		{"invalid JSON", "{", "parse unity release response"},
		{"missing version", `{"results":[{"shortRevision":"abc"}]}`, "missing version or shortRevision"},
		{"missing short revision", `{"results":[{"version":"6000.7.0"}]}`, "missing version or shortRevision"},
		{"archive without URL", `{"results":[{"version":"6000.7.0","shortRevision":"abc","downloads":[{"platform":"LINUX","architecture":"X86_64","type":"TAR_XZ"}]}]}`, "download is missing url"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			_, err := selectUnityRelease([]byte(testCase.body))

			if err == nil || !strings.Contains(err.Error(), testCase.wantErr) {
				t.Fatalf("expected error containing %q, got %v", testCase.wantErr, err)
			}
		})
	}
}

func TestRunResolveUnityReleaseRejectsUnknownFlags(t *testing.T) {
	// What: an unknown flag exits non-zero before any API request.
	stderr := strings.Builder{}

	exitCode := RunResolveUnityRelease(io.Discard, &stderr, []string{"--unknown"})

	if exitCode != 1 {
		t.Fatalf("exit code = %d, want 1", exitCode)
	}
	if !strings.Contains(stderr.String(), "flag provided but not defined") || strings.Contains(stderr.String(), "--series is required") {
		t.Fatalf("expected only the flag parse error, got %q", stderr.String())
	}
}
