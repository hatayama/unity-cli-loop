package automation

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"io"
	"net/url"
	"strings"
)

const (
	dispatcherReleasePRComponent    = "dispatcher"
	projectRunnerReleasePRComponent = "uloop-project-runner"
)

// githubFileContentAtRef reads a repository file through the contents API at an
// exact ref. The merge gate reasons about commits that are not in this
// checkout, so the file has to come from GitHub rather than from the worktree.
func githubFileContentAtRef(
	ctx context.Context,
	runOutput func(context.Context, string, ...string) (string, error),
	repository string,
	path string,
	ref string,
) ([]byte, error) {
	// The ref is a query parameter, so an unescaped one would be cut short at
	// the first & and read a different commit than the caller asked for.
	output, err := runOutput(
		ctx,
		"gh",
		"api",
		"repos/"+repository+"/contents/"+path+"?ref="+url.QueryEscape(ref),
		"--jq",
		".content",
	)
	if err != nil {
		return nil, err
	}

	// The contents API wraps base64 at a fixed width, so every whitespace
	// character has to go before decoding.
	encoded := strings.Join(strings.Fields(output), "")
	decoded, err := base64.StdEncoding.DecodeString(encoded)
	if err != nil {
		return nil, fmt.Errorf("%s: failed to decode %s at %s: %w",
			mergePackageReleasePRCommandName, path, ref, err)
	}
	return decoded, nil
}

// dispatcherReleaseTagFromManifestAtRef reports the dispatcher release tag the
// base branch tip publishes. The package pin must record that exact tag, so
// this is the value the merge gate compares the pin against when the caller
// does not name a tag itself.
func dispatcherReleaseTagFromManifestAtRef(
	ctx context.Context,
	runOutput func(context.Context, string, ...string) (string, error),
	repository string,
	ref string,
) (string, error) {
	content, err := githubFileContentAtRef(ctx, runOutput, repository, releasePleaseManifestRelativePath, ref)
	if err != nil {
		return "", err
	}

	manifest := map[string]string{}
	err = json.Unmarshal(content, &manifest)
	if err != nil {
		return "", fmt.Errorf("%s: failed to parse %s at %s: %w",
			mergePackageReleasePRCommandName, releasePleaseManifestRelativePath, ref, err)
	}

	version := manifest[releasePleaseDispatcherPackage]
	if strings.TrimSpace(version) == "" {
		return "", fmt.Errorf("%s: %s at %s has no %q version",
			mergePackageReleasePRCommandName, releasePleaseManifestRelativePath, ref, releasePleaseDispatcherPackage)
	}
	// A padded version would build a tag no release ever carries, so the gate
	// would wait out its whole timeout instead of reporting the bad manifest.
	if version != strings.TrimSpace(version) {
		return "", fmt.Errorf("%s: %s at %s releases %q as %q, which is not a bare version",
			mergePackageReleasePRCommandName, releasePleaseManifestRelativePath, ref, releasePleaseDispatcherPackage, version)
	}
	return dispatcherReleaseTagPrefix + version, nil
}

// openDispatcherReleasePullRequestExists reports whether a dispatcher release
// is still pending. The label is deliberately not part of the query: a pending
// release pull request that lost its label is still a dispatcher release the
// package must not overtake.
func openDispatcherReleasePullRequestExists(
	ctx context.Context,
	runOutput func(context.Context, string, ...string) (string, error),
	repository string,
	baseBranch string,
) (bool, error) {
	return openReleasePullRequestExists(ctx, runOutput, repository, baseBranch, dispatcherReleasePRComponent)
}

// openProjectRunnerReleasePullRequestExists reports whether a project runner
// release is still pending. Its release pull request raises the runner the
// package pins, so a package cut before it ships the previous runner.
func openProjectRunnerReleasePullRequestExists(
	ctx context.Context,
	runOutput func(context.Context, string, ...string) (string, error),
	repository string,
	baseBranch string,
) (bool, error) {
	return openReleasePullRequestExists(ctx, runOutput, repository, baseBranch, projectRunnerReleasePRComponent)
}

// openReleasePullRequestExists reports whether the release pull request of one
// component is open, found by the head branch release-please names after it.
func openReleasePullRequestExists(
	ctx context.Context,
	runOutput func(context.Context, string, ...string) (string, error),
	repository string,
	baseBranch string,
	component string,
) (bool, error) {
	headBranch := "release-please--branches--" + baseBranch + "--components--" + component
	output, err := runOutput(
		ctx,
		"gh",
		"pr",
		"list",
		"--repo",
		repository,
		"--state",
		"open",
		"--base",
		baseBranch,
		"--head",
		headBranch,
		"--json",
		"number",
	)
	if err != nil {
		return false, err
	}

	releasePRs := []mergePackageReleasePullRequest{}
	err = json.Unmarshal([]byte(output), &releasePRs)
	if err != nil {
		return false, fmt.Errorf("%s: failed to parse %s release PR list: %w",
			mergePackageReleasePRCommandName, component, err)
	}
	return len(releasePRs) > 0, nil
}

// releasePublished reports whether a GitHub release exists for the tag and is
// no longer a draft. A missing release reads as unpublished, but any other
// failure is returned: reading an API outage as "unpublished" would hold a
// release back for good without anyone being told.
func releasePublished(
	ctx context.Context,
	runOutput func(context.Context, string, ...string) (string, error),
	repository string,
	tag string,
) (bool, error) {
	output, err := runOutput(ctx, "gh", "release", "view", tag, "--repo", repository, "--json", "isDraft")
	if err != nil {
		if releaseNotFound(err) {
			return false, nil
		}
		return false, err
	}

	view := struct {
		IsDraft bool `json:"isDraft"`
	}{}
	err = json.Unmarshal([]byte(output), &view)
	if err != nil {
		return false, fmt.Errorf("%s: failed to parse release %s: %w", mergePackageReleasePRCommandName, tag, err)
	}
	return !view.IsDraft, nil
}

func releaseNotFound(err error) bool {
	message := err.Error()
	return strings.Contains(message, "release not found") ||
		strings.Contains(message, "HTTP 404") ||
		strings.Contains(message, "Not Found")
}

// projectRunnerReleaseGate holds the package release pull request until the
// project runner its pin names is released. Holding settles the pass rather
// than polling: the runner is published by another, approval-gated workflow,
// whose post-publish dispatches release-please, and that run's merge step
// decides again.
func projectRunnerReleaseGate(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config mergePackageReleasePRConfig,
	releasePR mergePackageReleasePullRequest,
	pin packagePinConsistencyPin,
	deps mergePackageReleasePRDeps,
) (held bool, exitCode int) {
	runnerPRIsOpen, err := openProjectRunnerReleasePullRequestExists(ctx, deps.runOutput, config.repository, config.baseBranch)
	if err != nil {
		writeMergePackageReleasePRLine(stderr, err)
		return true, 1
	}
	if runnerPRIsOpen {
		writeMergePackageReleasePRLine(stdout, fmt.Sprintf(
			"PR #%d stays draft: a project runner release pull request is open; the project runner must be released first so the package pins it.",
			releasePR.Number))
		return true, 0
	}

	published, err := releasePublished(ctx, deps.runOutput, config.repository, projectRunnerReleaseTagPrefix+pin.ProjectRunnerVersion)
	if err != nil {
		writeMergePackageReleasePRLine(stderr, err)
		return true, 1
	}
	if !published {
		writeMergePackageReleasePRLine(stdout, fmt.Sprintf(
			"PR #%d head %s pins project runner %s, which is not published yet; waiting for the project runner release.",
			releasePR.Number, releasePR.HeadRefOID, pin.ProjectRunnerVersion))
		return true, 0
	}
	return false, 0
}
