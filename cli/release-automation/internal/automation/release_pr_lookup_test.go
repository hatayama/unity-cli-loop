package automation

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"slices"
	"strconv"
	"strings"
	"testing"
	"time"
)

// fakeGHPRListing answers gh pr list the way gh does: a --label filter goes through GitHub search,
// which returns only what it has indexed, and every listing stops at --limit, 30 by default.
type fakeGHPRListing struct {
	newestFirst []releasePullRequest
	indexed     map[int]bool
}

func (listing fakeGHPRListing) run(_ context.Context, _ string, args ...string) (string, error) {
	limit := 30
	searched := false
	for index, arg := range args {
		if arg == "--label" {
			searched = true
		}
		if arg == "--limit" {
			parsed, err := strconv.Atoi(args[index+1])
			if err != nil {
				return "", err
			}
			limit = parsed
		}
	}

	listed := []releasePullRequest{}
	for _, releasePR := range listing.newestFirst {
		if searched && !listing.indexed[releasePR.Number] {
			continue
		}
		listed = append(listed, releasePR)
	}
	if len(listed) > limit {
		listed = listed[:limit]
	}
	output, err := json.Marshal(listed)
	return string(output), err
}

func listedReleasePRNumbers(t *testing.T, listing fakeGHPRListing) []int {
	t.Helper()
	config := releasePRCheckConfig{repository: "owner/repository", targetBranch: "main"}
	releasePRs, err := findReleasePRCheckPullRequests(context.Background(), config, releasePRCheckDeps{runOutput: listing.run})
	if err != nil {
		t.Fatalf("lookup failed: %v", err)
	}
	numbers := []int{}
	for _, releasePR := range releasePRs {
		numbers = append(numbers, releasePR.Number)
	}
	return numbers
}

func componentReleasePR(number int, component string) releasePullRequest {
	return releasePullRequest{
		Number:      number,
		HeadRefName: "release-please--branches--main--components--" + component,
		HeadRefOID:  "head" + strconv.Itoa(number),
		Title:       "chore(main): release " + component,
		Labels:      []releasePullRequestLabel{{Name: releasePRPendingLabel}},
	}
}

// Verifies release pull requests created moments before the lookup get their checks, even though GitHub search has not indexed them yet.
func TestFindReleasePRCheckPullRequestsSeesPullRequestsSearchHasNotIndexed(t *testing.T) {
	listing := fakeGHPRListing{
		newestFirst: []releasePullRequest{
			componentReleasePR(3, "uloop-project-runner"),
			componentReleasePR(2, "dispatcher"),
			componentReleasePR(1, "unity-package"),
		},
		indexed: map[int]bool{1: true},
	}

	got := listedReleasePRNumbers(t, listing)

	if want := []int{3, 2, 1}; !slices.Equal(got, want) {
		t.Fatalf("listed release PRs = %v, want %v", got, want)
	}
}

// Verifies a pull request that only copies a release branch name and title, as anyone can from a fork, gets no checks because it lacks the label only maintainers can apply.
func TestFindReleasePRCheckPullRequestsIgnoresPullRequestWithoutThePendingLabel(t *testing.T) {
	impostor := componentReleasePR(9, "dispatcher")
	impostor.Labels = nil
	listing := fakeGHPRListing{
		newestFirst: []releasePullRequest{impostor, componentReleasePR(1, "dispatcher")},
		indexed:     map[int]bool{1: true},
	}

	got := listedReleasePRNumbers(t, listing)

	if want := []int{1}; !slices.Equal(got, want) {
		t.Fatalf("listed release PRs = %v, want %v", got, want)
	}
}

// Verifies a release pull request stays found behind more newer pull requests than gh lists by default, and that those other pull requests are not treated as releases.
func TestFindReleasePRCheckPullRequestsSeesOlderPullRequestBehindNewerOnes(t *testing.T) {
	newestFirst := []releasePullRequest{}
	for number := 100; number > 60; number-- {
		newestFirst = append(newestFirst, releasePullRequest{
			Number:      number,
			HeadRefName: fmt.Sprintf("dependabot/go_modules/module-%d", number),
			HeadRefOID:  "head",
			Title:       "chore(deps): bump a module",
		})
	}
	newestFirst = append(newestFirst, componentReleasePR(1, "unity-package"))

	got := listedReleasePRNumbers(t, fakeGHPRListing{newestFirst: newestFirst, indexed: map[int]bool{1: true}})

	if want := []int{1}; !slices.Equal(got, want) {
		t.Fatalf("listed release PRs = %v, want %v", got, want)
	}
}

// Verifies which titles count as release-please release titles: plain and scoped "release" titles match, while other prefixes and a scope that never closes do not.
func TestReleasePleaseReleaseTitleMatches(t *testing.T) {
	cases := map[string]bool{
		"chore: release":                  true,
		"chore: release 3.6.0":            true,
		"chore(main): release dispatcher": true,
		"chore(main): release":            true,
		"chore(main): releases":           false,
		"chore(main release 3.6.0":        false,
		"feat: release 3.6.0":             false,
		"feat(main): release 3.6.0":       false,
	}
	for title, want := range cases {
		if got := releasePleaseReleaseTitleMatches(title); got != want {
			t.Fatalf("releasePleaseReleaseTitleMatches(%q) = %t, want %t", title, got, want)
		}
	}
}

// Verifies a matching release pull request without a head SHA fails the lookup instead of being checked against an empty commit.
func TestFindReleasePRCheckPullRequestsRejectsMissingHeadSHA(t *testing.T) {
	deps := releasePRCheckDeps{runOutput: func(context.Context, string, ...string) (string, error) {
		return `[{"number":7,"labels":[{"name":"autorelease: pending"}],"headRefName":"release-please--branches--main","headRefOid":"","title":"chore(main): release 3.6.0"}]`, nil
	}}

	_, err := findReleasePRCheckPullRequests(context.Background(), releasePRCheckConfig{repository: "owner/repository", targetBranch: "main"}, deps)

	if err == nil || !strings.Contains(err.Error(), "release PR #7 has no head SHA") {
		t.Fatalf("expected a missing head SHA error, got %v", err)
	}
}

// Verifies an interrupted wait between lookups fails with the sleep error instead of reporting that no release pull request exists.
func TestFindReleasePRCheckPullRequestsWithRetryStopsOnInterruptedWait(t *testing.T) {
	calls := 0
	deps := releasePRCheckDeps{
		runOutput: func(context.Context, string, ...string) (string, error) {
			calls++
			return "[]", nil
		},
		sleep: func(context.Context, time.Duration) error { return errors.New("lookup wait interrupted") },
	}
	config := releasePRCheckConfig{repository: "owner/repository", targetBranch: "main", lookupAttempts: 3, lookupIntervalSeconds: 1}

	_, err := findReleasePRCheckPullRequestsWithRetry(context.Background(), config, deps)

	if err == nil || err.Error() != "lookup wait interrupted" || calls != 1 {
		t.Fatalf("expected the wait error after one lookup, got %v after %d lookups", err, calls)
	}
}

// Verifies the Unity package release pull request is moved to the front while every other pull request keeps its listed order, including the combined branch and a listing without a package pull request.
func TestOrderReleasePRsForChecks(t *testing.T) {
	const prefix = "release-please--branches--main--components--"
	runner := releasePullRequest{Number: 1, HeadRefName: prefix + "uloop-project-runner"}
	dispatcher := releasePullRequest{Number: 2, HeadRefName: prefix + "dispatcher"}
	unityPackage := releasePullRequest{Number: 3, HeadRefName: prefix + "unity-package"}
	combined := releasePullRequest{Number: 4, HeadRefName: "release-please--branches--main"}
	cases := []struct {
		name   string
		listed []releasePullRequest
		want   []int
	}{
		{name: "package listed last", listed: []releasePullRequest{runner, dispatcher, unityPackage}, want: []int{3, 1, 2}},
		{name: "package already first", listed: []releasePullRequest{unityPackage, dispatcher}, want: []int{3, 2}},
		{name: "package only", listed: []releasePullRequest{unityPackage}, want: []int{3}},
		{name: "no package", listed: []releasePullRequest{runner, dispatcher}, want: []int{1, 2}},
		{name: "combined branch is not the package", listed: []releasePullRequest{combined, unityPackage}, want: []int{3, 4}},
	}
	for _, testCase := range cases {
		ordered := orderReleasePRsForChecks(testCase.listed, "main")
		got := []int{}
		for _, releasePR := range ordered {
			got = append(got, releasePR.Number)
		}
		if !slices.Equal(got, testCase.want) {
			t.Fatalf("%s: order = %v, want %v", testCase.name, got, testCase.want)
		}
	}
}
