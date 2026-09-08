package automation

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"net/url"
	"strings"
)

const dispatcherReleasePRComponent = "dispatcher"

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
	headBranch := "release-please--branches--" + baseBranch + "--components--" + dispatcherReleasePRComponent
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
		return false, fmt.Errorf("%s: failed to parse dispatcher release PR list: %w",
			mergePackageReleasePRCommandName, err)
	}
	return len(releasePRs) > 0, nil
}
