package automation

import (
	"context"
	"fmt"
	"io"
	"os"
	"time"
)

// DispatchPullRequestChecksForHead starts every required check workflow on an
// explicit head ref. Why this exists: a pull request opened with GITHUB_TOKEN
// never triggers pull_request workflows, so its required checks only appear
// when automation dispatches them. description labels the log lines with the
// pull request the dispatch belongs to.
func DispatchPullRequestChecksForHead(
	ctx context.Context,
	stdout io.Writer,
	repository string,
	headRefName string,
	description string,
) error {
	if repository == "" {
		return fmt.Errorf("repository is required to dispatch pull request checks")
	}
	if headRefName == "" {
		return fmt.Errorf("head ref is required to dispatch pull request checks")
	}
	workflows, err := releasePRCheckWorkflowsFromEnvironment()
	if err != nil {
		return err
	}
	_, err = dispatchPullRequestCheckWorkflows(ctx, stdout, repository, headRefName, description, workflows, defaultReleasePRCheckDeps())
	return err
}

// dispatchPullRequestCheckWorkflows dispatches the workflows and reports the
// instant the first dispatch was issued, which callers use to ignore workflow
// runs that predate their own dispatch.
func dispatchPullRequestCheckWorkflows(
	ctx context.Context,
	stdout io.Writer,
	repository string,
	headRefName string,
	description string,
	workflows []string,
	deps releasePRCheckDeps,
) (time.Time, error) {
	dispatchedAt := deps.now().UTC().Truncate(time.Second)
	for _, workflow := range workflows {
		writeReleasePRCheckLine(stdout, fmt.Sprintf("Dispatching %s for %s", workflow, description))
		_, err := deps.runOutput(ctx, "gh", "workflow", "run", workflow, "--repo", repository, "--ref", headRefName)
		if err != nil {
			return time.Time{}, err
		}
	}
	return dispatchedAt, nil
}

// writePullRequestBodyFile stages a pull request body on disk because gh only
// accepts multi-line bodies through --body-file.
func writePullRequestBodyFile(body string) (string, func(), error) {
	bodyFile, err := os.CreateTemp("", "uloop-pull-request-body-*.md")
	if err != nil {
		return "", func() {}, fmt.Errorf("failed to create pull request body file: %w", err)
	}

	cleanup := func() { _ = os.Remove(bodyFile.Name()) }
	_, writeErr := bodyFile.WriteString(body)
	closeErr := bodyFile.Close()
	if writeErr != nil {
		cleanup()
		return "", func() {}, fmt.Errorf("failed to write pull request body file: %w", writeErr)
	}
	if closeErr != nil {
		cleanup()
		return "", func() {}, fmt.Errorf("failed to close pull request body file: %w", closeErr)
	}
	return bodyFile.Name(), cleanup, nil
}
