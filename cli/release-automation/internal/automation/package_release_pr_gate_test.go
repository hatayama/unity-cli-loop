package automation

import (
	"context"
	"encoding/json"
	"fmt"
	"net/url"
	"strings"
	"testing"
)

type openPullRequestHead struct {
	owner  string
	branch string
}

// ghPullsAPI answers GET repos/owner/repository/pulls the way GitHub does: head takes
// owner:branch and matches that owner only, and a head without an owner is ignored, so
// every open pull request comes back.
func ghPullsAPI(open []openPullRequestHead) func(context.Context, string, ...string) (string, error) {
	return func(_ context.Context, name string, args ...string) (string, error) {
		if name != "gh" || len(args) != 2 || args[0] != "api" {
			return "", fmt.Errorf("unexpected command: %s %v", name, args)
		}
		request, err := url.Parse(args[1])
		if err != nil {
			return "", err
		}
		if request.Path != "repos/owner/repository/pulls" {
			return "", fmt.Errorf("unexpected path: %s", request.Path)
		}

		head := request.Query().Get("head")
		listed := []map[string]any{}
		for index, pullRequest := range open {
			if !strings.Contains(head, ":") || head == pullRequest.owner+":"+pullRequest.branch {
				listed = append(listed, map[string]any{"number": index + 1})
			}
		}
		output, err := json.Marshal(listed)
		return string(output), err
	}
}

// Verifies only this repository's release pull request of that component holds the package release: a fork's pull request from a branch of the same name does not.
func TestOpenReleasePullRequestExistsCountsOnlyThisRepositorysPullRequest(t *testing.T) {
	const branchPrefix = "release-please--branches--main--components--"
	gates := []struct {
		name      string
		component string
		other     string
		exists    func(context.Context, func(context.Context, string, ...string) (string, error), string, string) (bool, error)
	}{
		{"dispatcher", "dispatcher", "uloop-project-runner", openDispatcherReleasePullRequestExists},
		{"project runner", "uloop-project-runner", "dispatcher", openProjectRunnerReleasePullRequestExists},
	}
	for _, gate := range gates {
		releasePR := openPullRequestHead{owner: "owner", branch: branchPrefix + gate.component}
		forkPR := openPullRequestHead{owner: "someone", branch: branchPrefix + gate.component}
		otherComponentPR := openPullRequestHead{owner: "owner", branch: branchPrefix + gate.other}
		cases := []struct {
			name string
			open []openPullRequestHead
			want bool
		}{
			{"nothing open", nil, false},
			{"release pull request", []openPullRequestHead{releasePR}, true},
			{"fork only", []openPullRequestHead{forkPR}, false},
			{"fork next to the release pull request", []openPullRequestHead{forkPR, releasePR}, true},
			{"another component only", []openPullRequestHead{otherComponentPR}, false},
		}
		for _, testCase := range cases {
			t.Run(gate.name+"/"+testCase.name, func(t *testing.T) {
				got, err := gate.exists(context.Background(), ghPullsAPI(testCase.open), "owner/repository", "main")
				if err != nil {
					t.Fatalf("gate failed: %v", err)
				}
				if got != testCase.want {
					t.Fatalf("open release pull request = %t, want %t", got, testCase.want)
				}
			})
		}
	}
}
