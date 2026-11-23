package automation

import (
	"context"
	"encoding/json"
	"fmt"
	"strconv"
)

// releasePullRequestState is the part of `gh pr view --json state,mergedAt`
// that tells a merged release PR apart from one closed without merging.
type releasePullRequestState struct {
	State    string `json:"state"`
	MergedAt string `json:"mergedAt"`
}

// releasePRCheckMergedSkipMessage returns the skip line for a release PR that a
// person merged while the job was working through the list, and false for any
// other PR. The documented release order has people merge the dispatcher and
// project runner release PRs by hand, so a merge during the checks is expected
// and the release goes out without them. A PR closed without merging, still
// open, or whose state cannot be read is not that race, so the caller keeps its
// original error.
func releasePRCheckMergedSkipMessage(
	ctx context.Context,
	config releasePRCheckConfig,
	releasePR releasePullRequest,
	deps releasePRCheckDeps,
) (string, bool) {
	output, err := deps.runOutput(
		ctx, "gh", "pr", "view", strconv.Itoa(releasePR.Number), "--repo", config.repository, "--json", "state,mergedAt")
	if err != nil {
		return "", false
	}
	state := releasePullRequestState{}
	if json.Unmarshal([]byte(output), &state) != nil {
		return "", false
	}
	if state.State != "MERGED" || state.MergedAt == "" {
		return "", false
	}
	return fmt.Sprintf("Skipping release PR #%d: it was merged while the release PR checks ran.", releasePR.Number), true
}
