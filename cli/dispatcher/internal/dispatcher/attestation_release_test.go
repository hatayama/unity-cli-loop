package dispatcher

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"

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
