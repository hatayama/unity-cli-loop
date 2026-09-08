package automation

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"strconv"
)

// mergePackageReleasePRMergedState is the state gh reports for a pull request
// another run already merged.
const mergePackageReleasePRMergedState = "MERGED"

type mergePackageReleasePRState struct {
	State   string `json:"state"`
	IsDraft bool   `json:"isDraft"`
}

// readyAndMergePackageReleasePR performs the two writes that release the
// package. Both paths that call this command wait for the same head to go
// green, so the release-please run started by the pin stamp can reach here
// while post-publish is still polling. Losing that race is a success: a failed
// write is re-read, and a pull request the other run already merged reports
// what happened instead of failing the job.
func readyAndMergePackageReleasePR(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config mergePackageReleasePRConfig,
	releasePR mergePackageReleasePullRequest,
	pinnedTag string,
	deps mergePackageReleasePRDeps,
) (settled bool, exitCode int) {
	if releasePR.IsDraft {
		readyErr := markPackageReleasePRReady(ctx, config, releasePR, deps)
		if readyErr != nil {
			resolved, exitCode := resolvePackageReleasePRWriteFailure(ctx, stdout, stderr, config, releasePR, readyErr, deps)
			if resolved {
				return true, exitCode
			}
		} else {
			writeMergePackageReleasePRLine(stdout, fmt.Sprintf(
				"Marked Unity package release PR #%d ready before merging it.", releasePR.Number))
		}
	}

	mergeErr := squashMergePackageReleasePR(ctx, config, releasePR, deps)
	if mergeErr != nil {
		_, exitCode := resolvePackageReleasePRWriteFailure(ctx, stdout, stderr, config, releasePR, mergeErr, deps)
		return true, exitCode
	}
	writeMergePackageReleasePRLine(stdout, fmt.Sprintf(
		"Merged Unity package release PR #%d at %s; it pins %s.", releasePR.Number, releasePR.HeadRefOID, pinnedTag))
	return true, 0
}

// resolvePackageReleasePRWriteFailure re-reads the pull request after a failed
// write and decides whether the failure was the other run getting there first.
// resolved is false only when the pull request is still open and out of draft,
// which means the draft was already lifted and the merge is still worth
// attempting; every other outcome is final and carries the exit code.
func resolvePackageReleasePRWriteFailure(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config mergePackageReleasePRConfig,
	releasePR mergePackageReleasePullRequest,
	writeErr error,
	deps mergePackageReleasePRDeps,
) (resolved bool, exitCode int) {
	state, err := packageReleasePullRequestState(ctx, config, releasePR, deps)
	if err != nil {
		writeMergePackageReleasePRLine(stderr, writeErr)
		writeMergePackageReleasePRLine(stderr, err)
		return true, 1
	}
	if state.State == mergePackageReleasePRMergedState {
		writeMergePackageReleasePRLine(stdout, fmt.Sprintf(
			"Unity package release PR #%d was already merged by another run; nothing left to do.", releasePR.Number))
		return true, 0
	}
	if state.IsDraft {
		writeMergePackageReleasePRLine(stderr, writeErr)
		return true, 1
	}
	writeMergePackageReleasePRLine(stdout, fmt.Sprintf(
		"Unity package release PR #%d is already out of draft; continuing to merge it.", releasePR.Number))
	return false, 0
}

// packageReleasePullRequestState re-reads the state a write may have raced
// with. Draft and merged are the two outcomes a concurrent run produces, so
// both are read in one call.
func packageReleasePullRequestState(
	ctx context.Context,
	config mergePackageReleasePRConfig,
	releasePR mergePackageReleasePullRequest,
	deps mergePackageReleasePRDeps,
) (mergePackageReleasePRState, error) {
	output, err := deps.runOutput(
		ctx,
		"gh",
		"pr",
		"view",
		strconv.Itoa(releasePR.Number),
		"--repo",
		config.repository,
		"--json",
		"state,isDraft",
	)
	if err != nil {
		return mergePackageReleasePRState{}, err
	}

	state := mergePackageReleasePRState{}
	err = json.Unmarshal([]byte(output), &state)
	if err != nil {
		return mergePackageReleasePRState{}, fmt.Errorf(
			"%s: failed to parse the state of PR #%d: %w", mergePackageReleasePRCommandName, releasePR.Number, err)
	}
	return state, nil
}

// markPackageReleasePRReady lifts the draft state the release PR check
// automation leaves in place. Only draft pull requests are readied, because
// gh pr ready fails on one that is already ready.
func markPackageReleasePRReady(
	ctx context.Context,
	config mergePackageReleasePRConfig,
	releasePR mergePackageReleasePullRequest,
	deps mergePackageReleasePRDeps,
) error {
	_, err := deps.runOutput(ctx, "gh", "pr", "ready", strconv.Itoa(releasePR.Number), "--repo", config.repository)
	return err
}

// squashMergePackageReleasePR pins the merge to the head this command
// verified, so a head moved between the check and the merge fails instead of
// releasing an unchecked commit.
func squashMergePackageReleasePR(
	ctx context.Context,
	config mergePackageReleasePRConfig,
	releasePR mergePackageReleasePullRequest,
	deps mergePackageReleasePRDeps,
) error {
	_, err := deps.runOutput(
		ctx,
		"gh",
		"pr",
		"merge",
		strconv.Itoa(releasePR.Number),
		"--repo",
		config.repository,
		"--squash",
		"--match-head-commit",
		releasePR.HeadRefOID,
	)
	return err
}
