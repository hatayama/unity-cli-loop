package automation

import (
	"context"
	"fmt"
	"io"
)

const releasePRDispatcherMinimumCommitMessage = "chore: sync dispatcher minimum for release PR"

func syncReleasePRDispatcherMinimum(
	ctx context.Context,
	stdout io.Writer,
	config releasePRCheckConfig,
	releasePR releasePullRequest,
) (releasePullRequest, error) {
	repoRoot, err := gitRepoRoot(ctx)
	if err != nil {
		return releasePullRequest{}, fmt.Errorf("failed to resolve git repository root: %w", err)
	}
	if err := checkoutReleasePRBranch(ctx, repoRoot, releasePR); err != nil {
		return releasePullRequest{}, err
	}

	values, err := dispatcherMinimumVersionValuesAtRef(ctx, repoRoot, "")
	if err != nil {
		return releasePullRequest{}, err
	}
	targetVersion, needsSync := dispatcherMinimumVersionSyncTarget(ctx, repoRoot, values)
	if !needsSync {
		return releasePR, nil
	}

	changed, err := syncDispatcherMinimumVersionFiles(repoRoot, targetVersion)
	if err != nil {
		return releasePullRequest{}, err
	}
	if !changed {
		return releasePR, nil
	}

	if err := commitReleasePRDispatcherMinimum(ctx, repoRoot, releasePR); err != nil {
		return releasePullRequest{}, err
	}

	updatedReleasePR, found, err := findReleasePRCheckPullRequest(ctx, config)
	if err != nil {
		return releasePullRequest{}, err
	}
	if !found {
		return releasePullRequest{}, fmt.Errorf("release PR #%d is no longer pending after dispatcher minimum sync", releasePR.Number)
	}
	if updatedReleasePR.Number != releasePR.Number {
		return releasePullRequest{}, fmt.Errorf("pending release PR changed from #%d to #%d after dispatcher minimum sync", releasePR.Number, updatedReleasePR.Number)
	}

	writeReleasePRCheckLine(stdout, fmt.Sprintf(
		"Updated release PR #%d dispatcher minimum version to %s.",
		releasePR.Number,
		targetVersion))
	return updatedReleasePR, nil
}

func dispatcherMinimumVersionSyncTarget(
	ctx context.Context,
	repoRoot string,
	values dispatcherMinimumVersionValues,
) (string, bool) {
	err := verifyDispatcherMinimumVersionAtRef(ctx, repoRoot, values)
	if err == nil {
		return "", false
	}
	return values.CurrentCliVersion, true
}

func checkoutReleasePRBranch(ctx context.Context, repoRoot string, releasePR releasePullRequest) error {
	_, err := runReleasePRCheckOutput(ctx, "git", "-C", repoRoot, "fetch", "origin", releasePR.HeadRefName)
	if err != nil {
		return err
	}
	_, err = runReleasePRCheckOutput(ctx, "git", "-C", repoRoot, "switch", "--detach", "FETCH_HEAD")
	return err
}

func commitReleasePRDispatcherMinimum(ctx context.Context, repoRoot string, releasePR releasePullRequest) error {
	commands := [][]string{
		{"config", "user.name", "github-actions[bot]"},
		{"config", "user.email", "41898282+github-actions[bot]@users.noreply.github.com"},
		{"add", protocolMinimumVersionFile, unityPackageCliPinFile, unityProjectCliPinFile},
		{"commit", "-m", releasePRDispatcherMinimumCommitMessage},
		{"push", "origin", "HEAD:refs/heads/" + releasePR.HeadRefName},
	}
	for _, args := range commands {
		commandArgs := append([]string{"-C", repoRoot}, args...)
		if _, err := runReleasePRCheckOutput(ctx, "git", commandArgs...); err != nil {
			return err
		}
	}
	return nil
}
