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
		settled, exitCode := readyPackageReleasePRBeforeMerge(ctx, stdout, stderr, config, releasePR, deps)
		if settled {
			return true, exitCode
		}
	}

	mergeErr := squashMergePackageReleasePR(ctx, config, releasePR, deps)
	if mergeErr != nil {
		return true, resolvePackageReleasePRMergeFailure(ctx, stdout, stderr, config, releasePR, mergeErr, deps)
	}
	writeMergePackageReleasePRLine(stdout, fmt.Sprintf(
		"Merged Unity package release PR #%d at %s; it pins %s.", releasePR.Number, releasePR.HeadRefOID, pinnedTag))
	return true, 0
}

// readyPackageReleasePRBeforeMerge lifts the draft. settled is false only when
// the merge is still worth attempting: either the draft was lifted here, or the
// re-read shows another run lifted it first.
func readyPackageReleasePRBeforeMerge(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config mergePackageReleasePRConfig,
	releasePR mergePackageReleasePullRequest,
	deps mergePackageReleasePRDeps,
) (settled bool, exitCode int) {
	readyErr := markPackageReleasePRReady(ctx, config, releasePR, deps)
	if readyErr == nil {
		writeMergePackageReleasePRLine(stdout, fmt.Sprintf(
			"Marked Unity package release PR #%d ready before merging it.", releasePR.Number))
		return false, 0
	}

	state, err := packageReleasePullRequestState(ctx, config, releasePR, deps)
	if err != nil {
		writeMergePackageReleasePRLine(stderr, readyErr)
		writeMergePackageReleasePRLine(stderr, err)
		return true, 1
	}
	if state.State == mergePackageReleasePRMergedState {
		writeMergePackageReleasePRLine(stdout, packageReleasePRAlreadyMergedMessage(releasePR))
		return true, 0
	}
	// Still draft means the command genuinely could not lift it -- a permission
	// or ruleset failure, not a lost race.
	if state.IsDraft {
		writeMergePackageReleasePRLine(stderr, readyErr)
		return true, 1
	}
	writeMergePackageReleasePRLine(stdout, fmt.Sprintf(
		"Unity package release PR #%d is already out of draft; continuing to merge it.", releasePR.Number))
	return false, 0
}

// resolvePackageReleasePRMergeFailure decides what a failed merge means. Only
// a pull request another run already merged is a success here: any other state
// leaves the package unreleased, so reporting anything but a failure would let
// the release silently not happen.
func resolvePackageReleasePRMergeFailure(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config mergePackageReleasePRConfig,
	releasePR mergePackageReleasePullRequest,
	mergeErr error,
	deps mergePackageReleasePRDeps,
) int {
	state, err := packageReleasePullRequestState(ctx, config, releasePR, deps)
	if err != nil {
		writeMergePackageReleasePRLine(stderr, mergeErr)
		writeMergePackageReleasePRLine(stderr, err)
		return 1
	}
	if state.State == mergePackageReleasePRMergedState {
		writeMergePackageReleasePRLine(stdout, packageReleasePRAlreadyMergedMessage(releasePR))
		return 0
	}
	writeMergePackageReleasePRLine(stderr, mergeErr)
	return 1
}

func packageReleasePRAlreadyMergedMessage(releasePR mergePackageReleasePullRequest) string {
	return fmt.Sprintf(
		"Unity package release PR #%d was already merged by another run; nothing left to do.", releasePR.Number)
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
