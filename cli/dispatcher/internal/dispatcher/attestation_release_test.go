package dispatcher

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"reflect"
	"strconv"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/dispatcher/attestation"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/githubapi"
	sharedupdate "github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
)

// Verifies latest-beta selection ignores stable releases, non-dispatcher tags,
// draft releases, and non-beta prereleases so a dispatcher on a beta channel
// cannot silently resolve to a stable-only tag it would then attest against.
func TestResolveDispatcherLatestReleaseTagBetaChannelSkipsUnmatchedEntries(t *testing.T) {
	entriesPage := []githubReleaseListEntry{
		{TagName: "dispatcher-v3.0.1-beta.12", Prerelease: true, Draft: false},
		{TagName: "dispatcher-v3.0.0", Prerelease: false, Draft: false},
		{TagName: "uloop-project-runner-v0.9.0-beta.4", Prerelease: true, Draft: false},
		{TagName: "dispatcher-v3.0.2-beta.1", Prerelease: true, Draft: true},
		{TagName: "dispatcher-v3.0.0-rc.1", Prerelease: true, Draft: false},
	}
	server, restoreBase := installReleaseListServer(t, entriesPage)
	defer server.Close()
	defer restoreBase()

	tag, err := resolveDispatcherLatestReleaseTag(context.Background(), true)
	if err != nil {
		t.Fatalf("resolveDispatcherLatestReleaseTag failed: %v", err)
	}
	if tag != "dispatcher-v3.0.1-beta.12" {
		t.Fatalf("unexpected beta tag: %s", tag)
	}
}

// Verifies stable-channel resolution rejects prereleases and only accepts a
// dispatcher-v tag that is not marked prerelease.
func TestResolveDispatcherLatestReleaseTagStableChannelSkipsPrereleases(t *testing.T) {
	entriesPage := []githubReleaseListEntry{
		{TagName: "dispatcher-v3.0.1-beta.12", Prerelease: true},
		{TagName: "dispatcher-v3.0.0", Prerelease: false},
		{TagName: "uloop-project-runner-v0.9.0", Prerelease: false},
	}
	server, restoreBase := installReleaseListServer(t, entriesPage)
	defer server.Close()
	defer restoreBase()

	tag, err := resolveDispatcherLatestReleaseTag(context.Background(), false)
	if err != nil {
		t.Fatalf("resolveDispatcherLatestReleaseTag failed: %v", err)
	}
	if tag != "dispatcher-v3.0.0" {
		t.Fatalf("unexpected stable tag: %s", tag)
	}
}

// Verifies a channel with no matching release returns an error rather than
// silently falling back — attestation flows must fail closed on an unmet
// selector rather than downgrade to the wrong channel.
func TestResolveDispatcherLatestReleaseTagFailsWhenChannelEmpty(t *testing.T) {
	entriesPage := []githubReleaseListEntry{
		{TagName: "dispatcher-v3.0.0", Prerelease: false},
	}
	server, restoreBase := installReleaseListServer(t, entriesPage)
	defer server.Close()
	defer restoreBase()

	if _, err := resolveDispatcherLatestReleaseTag(context.Background(), true); err == nil {
		t.Fatal("expected beta resolution to fail when no beta release exists")
	}
}

// Verifies resolveUpdateTargetVersion respects an already-populated
// TargetVersion so tryHandleUpdateRequest's --to-version path is untouched.
func TestResolveUpdateTargetVersionKeepsExplicitTarget(t *testing.T) {
	options, err := resolveUpdateTargetVersion(context.Background(), sharedupdate.Options{
		CurrentVersion: "3.0.0-beta.10",
		TargetVersion:  "3.0.0-beta.7",
	})
	if err != nil {
		t.Fatalf("resolveUpdateTargetVersion failed: %v", err)
	}
	if options.TargetVersion != "3.0.0-beta.7" {
		t.Fatalf("explicit target was overwritten: %s", options.TargetVersion)
	}
}

// Verifies resolveUpdateTargetVersion picks the beta channel when the caller
// is currently running a beta build.
func TestResolveUpdateTargetVersionPromotesLatestForBetaChannel(t *testing.T) {
	entriesPage := []githubReleaseListEntry{
		{TagName: "dispatcher-v3.0.1-beta.5", Prerelease: true},
	}
	server, restoreBase := installReleaseListServer(t, entriesPage)
	defer server.Close()
	defer restoreBase()

	options, err := resolveUpdateTargetVersion(context.Background(), sharedupdate.Options{
		CurrentVersion: "3.0.0-beta.1",
	})
	if err != nil {
		t.Fatalf("resolveUpdateTargetVersion failed: %v", err)
	}
	if options.TargetVersion != "3.0.1-beta.5" {
		t.Fatalf("unexpected resolved target: %s", options.TargetVersion)
	}
}

func installReleaseListServer(t *testing.T, entries []githubReleaseListEntry) (*httptest.Server, func()) {
	t.Helper()
	handler := http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		if r.URL.Query().Get("page") != "1" {
			_ = json.NewEncoder(w).Encode([]githubReleaseListEntry{})
			return
		}
		_ = json.NewEncoder(w).Encode(entries)
	})
	server := httptest.NewServer(handler)
	previousBase := dispatcherAPIBaseURL
	dispatcherAPIBaseURL = server.URL
	return server, func() {
		dispatcherAPIBaseURL = previousBase
	}
}

// Verifies the release listing surfaces an exhausted GitHub quota as a typed rate-limit error.
func TestFetchDispatcherReleasePageReportsRateLimit(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("X-RateLimit-Remaining", "0")
		http.Error(w, `{"message":"API rate limit exceeded"}`, http.StatusForbidden)
	}))
	defer server.Close()
	previousBase := dispatcherAPIBaseURL
	dispatcherAPIBaseURL = server.URL
	defer func() { dispatcherAPIBaseURL = previousBase }()

	_, err := fetchDispatcherReleasePage(context.Background(), 1)
	var rateLimit githubapi.RateLimitError
	if !errors.As(err, &rateLimit) {
		t.Fatalf("expected RateLimitError, got: %v", err)
	}
	if !strings.HasPrefix(err.Error(), "list releases: ") {
		t.Fatalf("expected list releases prefix, got: %v", err)
	}
}

// installAuthorizationRecordingReleaseServer serves an empty release page and
// records the Authorization header the request carried.
func installAuthorizationRecordingReleaseServer(t *testing.T, recorded *string) (*httptest.Server, func()) {
	t.Helper()
	handler := http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		*recorded = r.Header.Get("Authorization")
		w.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(w).Encode([]githubReleaseListEntry{})
	})
	server := httptest.NewServer(handler)
	previousBase := dispatcherAPIBaseURL
	dispatcherAPIBaseURL = server.URL
	return server, func() {
		dispatcherAPIBaseURL = previousBase
	}
}

// Verifies the release listing authenticates with the token the shared source resolved.
func TestFetchDispatcherReleasePageSendsResolvedTokenAsBearer(t *testing.T) {
	useTokenSource(t, "resolved-token")
	var recorded string
	server, restoreBase := installAuthorizationRecordingReleaseServer(t, &recorded)
	defer server.Close()
	defer restoreBase()

	if _, err := fetchDispatcherReleasePage(context.Background(), 1); err != nil {
		t.Fatalf("fetchDispatcherReleasePage failed: %v", err)
	}
	if recorded != "Bearer resolved-token" {
		t.Fatalf("authorization header mismatch: got %q", recorded)
	}
}

// Verifies the release listing stays anonymous when no token is available, instead of sending an empty bearer.
func TestFetchDispatcherReleasePageOmitsAuthorizationWithoutToken(t *testing.T) {
	useTokenSource(t, "")
	var recorded string
	server, restoreBase := installAuthorizationRecordingReleaseServer(t, &recorded)
	defer server.Close()
	defer restoreBase()

	if _, err := fetchDispatcherReleasePage(context.Background(), 1); err != nil {
		t.Fatalf("fetchDispatcherReleasePage failed: %v", err)
	}
	if recorded != "" {
		t.Fatalf("expected no authorization header, got %q", recorded)
	}
}

// installPagedReleaseServer serves the given pages in order and records each request's
// path and query so tests can assert the pagination contract.
func installPagedReleaseServer(t *testing.T, pages [][]githubReleaseListEntry, requested *[]string) {
	t.Helper()
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		*requested = append(*requested, r.URL.RequestURI())
		page, err := strconv.Atoi(r.URL.Query().Get("page"))
		entries := []githubReleaseListEntry{}
		if err == nil && page >= 1 && page <= len(pages) {
			entries = pages[page-1]
		}
		w.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(w).Encode(entries)
	}))
	t.Cleanup(server.Close)
	previousBase := dispatcherAPIBaseURL
	dispatcherAPIBaseURL = server.URL
	t.Cleanup(func() { dispatcherAPIBaseURL = previousBase })
}

func fullReleasePage(tagName string, draft bool) []githubReleaseListEntry {
	entries := make([]githubReleaseListEntry, 100)
	for index := range entries {
		entries[index] = githubReleaseListEntry{TagName: tagName, Draft: draft}
	}
	return entries
}

func TestResolveDispatcherLatestReleaseTagFollowsFullPages(t *testing.T) {
	// Verifies a full page without a match moves on to the next page, and drafts are never selected.
	var requested []string
	installPagedReleaseServer(t, [][]githubReleaseListEntry{
		fullReleasePage("dispatcher-v9.0.0", true),
		{{TagName: "dispatcher-v3.0.0"}},
	}, &requested)

	tag, err := resolveDispatcherLatestReleaseTag(context.Background(), false)

	if err != nil || tag != "dispatcher-v3.0.0" {
		t.Fatalf("unexpected result: tag=%q err=%v", tag, err)
	}
	wantRequests := []string{
		"/repos/" + dispatcherReleaseRepository + "/releases?per_page=100&page=1",
		"/repos/" + dispatcherReleaseRepository + "/releases?per_page=100&page=2",
	}
	if !reflect.DeepEqual(requested, wantRequests) {
		t.Fatalf("requests mismatch: %v", requested)
	}
}

func TestResolveDispatcherLatestReleaseTagStopsAtEmptyPage(t *testing.T) {
	// Verifies an empty page ends the search with a channel-specific error instead of requesting more pages.
	var requested []string
	installPagedReleaseServer(t, [][]githubReleaseListEntry{fullReleasePage("uloop-project-runner-v1.0.0", false)}, &requested)

	_, err := resolveDispatcherLatestReleaseTag(context.Background(), false)

	if err == nil || err.Error() != "no stable dispatcher release available" {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(requested) != 2 {
		t.Fatalf("expected the search to stop at the empty second page: %v", requested)
	}
}

func TestFetchDispatcherReleasePageReportsResponseFailures(t *testing.T) {
	// Verifies a non-2xx status and an undecodable body are reported with their own messages.
	cases := []struct {
		name       string
		statusCode int
		body       string
		wantPrefix string
	}{
		{name: "server error", statusCode: http.StatusInternalServerError, body: "{}", wantPrefix: "list releases: status 500 Internal Server Error"},
		{name: "undecodable body", statusCode: http.StatusOK, body: "{", wantPrefix: "decode releases: "},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				w.WriteHeader(testCase.statusCode)
				_, _ = io.WriteString(w, testCase.body)
			}))
			t.Cleanup(server.Close)
			previousBase := dispatcherAPIBaseURL
			dispatcherAPIBaseURL = server.URL
			t.Cleanup(func() { dispatcherAPIBaseURL = previousBase })

			_, err := fetchDispatcherReleasePage(context.Background(), 1)

			if err == nil || !strings.HasPrefix(err.Error(), testCase.wantPrefix) {
				t.Fatalf("expected %q, got %v", testCase.wantPrefix, err)
			}
		})
	}
}

func TestResolveUpdateTargetVersionReportsListingFailure(t *testing.T) {
	// Verifies an unbuildable release listing request is returned and leaves the target version unset.
	previousBase := dispatcherAPIBaseURL
	dispatcherAPIBaseURL = "://invalid"
	t.Cleanup(func() { dispatcherAPIBaseURL = previousBase })

	options, err := resolveUpdateTargetVersion(context.Background(), sharedupdate.Options{CurrentVersion: "3.0.0"})

	if err == nil || !strings.Contains(err.Error(), "missing protocol scheme") || options.TargetVersion != "" {
		t.Fatalf("unexpected result: options=%+v err=%v", options, err)
	}
}

func TestFetchAttestationSubjectManifestFailsClosed(t *testing.T) {
	// Verifies an empty tag, a missing bundle, a failed tag lookup, and an unverifiable bundle all refuse to produce a manifest.
	bundleURL := dispatcherReleaseBaseURL + "/dispatcher-v1.0.0/" + sharedupdate.PosixScriptName + ".sigstore.json"
	tagURL := "https://api.github.com/repos/" + attestation.ReleaseRepository + "/git/ref/tags/dispatcher-v1.0.0"
	cases := []struct {
		name          string
		tag           string
		bundle        []byte
		tagStatusCode int
		wantErr       error
		wantRequests  []string
	}{
		{name: "empty tag", wantErr: attestation.ErrVerificationFailed},
		{name: "bundle missing", tag: "dispatcher-v1.0.0", wantErr: attestation.ErrBundleFetch, wantRequests: []string{bundleURL}},
		{name: "tag lookup fails", tag: "dispatcher-v1.0.0", bundle: []byte("{}"), tagStatusCode: http.StatusNotFound, wantErr: attestation.ErrTagRefFetch, wantRequests: []string{bundleURL, tagURL}},
		{name: "bundle does not verify", tag: "dispatcher-v1.0.0", bundle: []byte("{}"), tagStatusCode: http.StatusOK, wantErr: attestation.ErrMalformedBundle, wantRequests: []string{bundleURL, tagURL}},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			server := stubAttestationHTTP(t, testCase.bundle, testCase.tagStatusCode)

			manifest, err := fetchAttestationSubjectManifest(context.Background(), testCase.tag)

			if manifest != "" || !errors.Is(err, testCase.wantErr) {
				t.Fatalf("expected %v, got manifest=%q err=%v", testCase.wantErr, manifest, err)
			}
			if !reflect.DeepEqual(server.requested, testCase.wantRequests) {
				t.Fatalf("requests mismatch:\n got %v\nwant %v", server.requested, testCase.wantRequests)
			}
		})
	}
}

func TestFetchDispatcherReleasePageReportsTransportFailure(t *testing.T) {
	// Verifies a registry that cannot be reached is reported instead of being treated as an empty page.
	server := httptest.NewServer(http.HandlerFunc(func(http.ResponseWriter, *http.Request) {}))
	server.Close()
	previousBase := dispatcherAPIBaseURL
	dispatcherAPIBaseURL = server.URL
	t.Cleanup(func() { dispatcherAPIBaseURL = previousBase })

	entries, err := fetchDispatcherReleasePage(context.Background(), 1)

	if err == nil || entries != nil || !strings.Contains(err.Error(), server.Listener.Addr().String()) {
		t.Fatalf("expected the transport error, got entries=%v err=%v", entries, err)
	}
}
