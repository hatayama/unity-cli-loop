package automation

import (
	"context"
	"encoding/json"
	"fmt"
	"strings"
	"time"
)

// findReleasePRCheckPullRequests lists every pending release-please PR for the
// target branch. release-please opens one PR per component, so several PRs are
// the normal case rather than an error.
func findReleasePRCheckPullRequests(ctx context.Context, config releasePRCheckConfig, deps releasePRCheckDeps) ([]releasePullRequest, error) {
	output, err := deps.runOutput(
		ctx,
		"gh",
		"pr",
		"list",
		"--repo",
		config.repository,
		"--state",
		"open",
		"--base",
		config.targetBranch,
		"--label",
		"autorelease: pending",
		"--json",
		"number,headRefName,headRefOid,title,url",
	)
	if err != nil {
		return nil, err
	}

	releasePRs := []releasePullRequest{}
	err = json.Unmarshal([]byte(output), &releasePRs)
	if err != nil {
		return nil, fmt.Errorf("failed to parse release PR list: %w", err)
	}

	matchingPRs := []releasePullRequest{}
	for _, releasePR := range releasePRs {
		if !releasePRCheckMatches(releasePR, config.targetBranch) {
			continue
		}
		if releasePR.HeadRefOID == "" {
			return nil, fmt.Errorf("release PR #%d has no head SHA", releasePR.Number)
		}
		matchingPRs = append(matchingPRs, releasePR)
	}
	return matchingPRs, nil
}

func findReleasePRCheckPullRequestsWithRetry(ctx context.Context, config releasePRCheckConfig, deps releasePRCheckDeps) ([]releasePullRequest, error) {
	for attempt := 0; attempt < config.lookupAttempts; attempt++ {
		releasePRs, err := findReleasePRCheckPullRequests(ctx, config, deps)
		if err != nil || len(releasePRs) > 0 {
			return releasePRs, err
		}
		if attempt+1 < config.lookupAttempts {
			err = deps.sleep(ctx, time.Duration(config.lookupIntervalSeconds)*time.Second)
			if err != nil {
				return nil, err
			}
		}
	}
	return nil, nil
}

func releasePRCheckMatches(releasePR releasePullRequest, targetBranch string) bool {
	releasePRBranch := "release-please--branches--" + targetBranch
	if releasePR.HeadRefName != releasePRBranch && !strings.HasPrefix(releasePR.HeadRefName, releasePRBranch+"--components--") {
		return false
	}
	return releasePRCheckTitleMatches(releasePR.Title)
}

// releasePRCheckComponentFromHeadRef reads the release-please component out of
// a per-component release branch, and returns "" for the combined branch.
func releasePRCheckComponentFromHeadRef(headRefName string, targetBranch string) string {
	componentPrefix := "release-please--branches--" + targetBranch + "--components--"
	if !strings.HasPrefix(headRefName, componentPrefix) {
		return ""
	}
	return strings.TrimPrefix(headRefName, componentPrefix)
}

func releasePRCheckTitleMatches(title string) bool {
	if title == "chore: release" || strings.HasPrefix(title, "chore: release ") {
		return true
	}
	if !strings.HasPrefix(title, "chore(") {
		return false
	}
	closeIndex := strings.Index(title, "):")
	if closeIndex == -1 {
		return false
	}
	rest := strings.TrimSpace(title[closeIndex+2:])
	return rest == "release" || strings.HasPrefix(rest, "release ")
}

// releasePRCheckPullRequestByNumber keeps the head verification scoped to the
// PR whose checks just ran. Other components' release PRs come and go in the
// same listing, so their appearance must not read as this PR changing.
func releasePRCheckPullRequestByNumber(releasePRs []releasePullRequest, number int) (releasePullRequest, bool) {
	for _, releasePR := range releasePRs {
		if releasePR.Number == number {
			return releasePR, true
		}
	}
	return releasePullRequest{}, false
}
