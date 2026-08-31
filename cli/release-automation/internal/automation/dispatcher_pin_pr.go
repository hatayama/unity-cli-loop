package automation

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strconv"
	"strings"
)

const (
	dispatcherPinPRBranchPrefix = "chore/dispatcher-pin-"
	// The workflow bot identity GitHub assigns to GITHUB_TOKEN pushes, so the
	// stamp commit is attributed to automation instead of a human account.
	dispatcherPinPRAuthorName  = "github-actions[bot]"
	dispatcherPinPRAuthorEmail = "41898282+github-actions[bot]@users.noreply.github.com"
)

type dispatcherPinPRConfig struct {
	repository string
	tag        string
	baseBranch string
}

type dispatcherPinPRDeps struct {
	runOutput      func(ctx context.Context, name string, args ...string) (string, error)
	repositoryRoot func(ctx context.Context) (string, error)
	stampPin       func(ctx context.Context, pinPath string, releaseTag string) error
	verifySubjects func(ctx context.Context, packagePin []byte) error
	dispatchChecks func(ctx context.Context, stdout io.Writer, repository string, headRefName string, description string) error
}

type dispatcherPinPullRequest struct {
	Number int    `json:"number"`
	URL    string `json:"url"`
}

// RunOpenDispatcherPinPR stamps the package pin from a published stable
// dispatcher release and opens the review pull request that carries it.
func RunOpenDispatcherPinPR(ctx context.Context, stdout io.Writer, stderr io.Writer, args []string) int {
	config, err := parseDispatcherPinPRFlags(args)
	if err != nil {
		writeDispatcherPinPRLine(stderr, "open-dispatcher-pin-pr:", err)
		return 1
	}
	return runOpenDispatcherPinPRWithDeps(ctx, stdout, stderr, config, defaultDispatcherPinPRDeps())
}

func defaultDispatcherPinPRDeps() dispatcherPinPRDeps {
	return dispatcherPinPRDeps{
		runOutput:      runReleasePRCheckCommandOutput,
		repositoryRoot: dispatcherPinPRRepositoryRoot,
		stampPin:       StampDispatcherPin,
		verifySubjects: VerifyDispatcherPinSubjects,
		dispatchChecks: DispatchPullRequestChecksForHead,
	}
}

func parseDispatcherPinPRFlags(args []string) (dispatcherPinPRConfig, error) {
	flagSet := flag.NewFlagSet("open-dispatcher-pin-pr", flag.ContinueOnError)
	repository := flagSet.String("repo", "", "GitHub repository that owns the dispatcher release")
	tag := flagSet.String("tag", "", "dispatcher release tag such as dispatcher-v3.0.0")
	baseBranch := flagSet.String("base-branch", "", "branch the pull request targets")
	err := flagSet.Parse(args)
	if err != nil {
		return dispatcherPinPRConfig{}, err
	}
	if *repository == "" {
		return dispatcherPinPRConfig{}, fmt.Errorf("--repo is required")
	}
	if *baseBranch == "" {
		return dispatcherPinPRConfig{}, fmt.Errorf("--base-branch is required")
	}
	version, err := dispatcherVersionFromReleaseTag(*tag)
	if err != nil {
		return dispatcherPinPRConfig{}, err
	}
	// Pre-releases must never reach the pin on a stable branch: fresh installs
	// follow the pin, so stamping a pre-release would hand every new install a
	// pre-release dispatcher.
	if strings.Contains(version, "-") {
		return dispatcherPinPRConfig{}, fmt.Errorf("release tag %q is a pre-release and must not be stamped", *tag)
	}
	return dispatcherPinPRConfig{repository: *repository, tag: *tag, baseBranch: *baseBranch}, nil
}

func runOpenDispatcherPinPRWithDeps(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config dispatcherPinPRConfig,
	deps dispatcherPinPRDeps,
) int {
	repositoryRoot, err := deps.repositoryRoot(ctx)
	if err != nil {
		writeDispatcherPinPRLine(stderr, "open-dispatcher-pin-pr:", err)
		return 1
	}
	branch := dispatcherPinPRBranchPrefix + config.tag

	err = checkoutDispatcherPinPRBranch(ctx, repositoryRoot, config, branch, deps)
	if err != nil {
		writeDispatcherPinPRLine(stderr, "open-dispatcher-pin-pr:", err)
		return 1
	}
	err = stampAndVerifyDispatcherPin(ctx, repositoryRoot, config.tag, deps)
	if err != nil {
		writeDispatcherPinPRLine(stderr, "open-dispatcher-pin-pr:", err)
		return 1
	}

	changed, err := dispatcherPinFilesChanged(ctx, repositoryRoot, deps)
	if err != nil {
		writeDispatcherPinPRLine(stderr, "open-dispatcher-pin-pr:", err)
		return 1
	}
	if !changed {
		writeDispatcherPinPRLine(stdout, fmt.Sprintf("Dispatcher pin already records %s; no pull request is needed.", config.tag))
		return 0
	}

	err = commitAndPushDispatcherPin(ctx, stdout, repositoryRoot, config, branch, deps)
	if err != nil {
		writeDispatcherPinPRLine(stderr, "open-dispatcher-pin-pr:", err)
		return 1
	}
	return publishDispatcherPinPullRequest(ctx, stdout, stderr, config, branch, deps)
}

// checkoutDispatcherPinPRBranch branches from the base tip rather than the
// workflow's checked-out release commit, so the stamp lands on whatever main
// holds when the release is published.
func checkoutDispatcherPinPRBranch(
	ctx context.Context,
	repositoryRoot string,
	config dispatcherPinPRConfig,
	branch string,
	deps dispatcherPinPRDeps,
) error {
	baseRef := "refs/remotes/origin/" + config.baseBranch
	fetchRefspec := "+refs/heads/" + config.baseBranch + ":" + baseRef
	_, err := deps.runOutput(ctx, "git", "-C", repositoryRoot, "fetch", "origin", fetchRefspec)
	if err != nil {
		return err
	}
	_, err = deps.runOutput(ctx, "git", "-C", repositoryRoot, "checkout", "-B", branch, baseRef)
	return err
}

func stampAndVerifyDispatcherPin(ctx context.Context, repositoryRoot string, releaseTag string, deps dispatcherPinPRDeps) error {
	packagePinPath := filepath.Join(repositoryRoot, filepath.FromSlash(unityPackageCliPinFile))
	projectPinPath := filepath.Join(repositoryRoot, filepath.FromSlash(unityProjectCliPinFile))

	err := deps.stampPin(ctx, packagePinPath, releaseTag)
	if err != nil {
		return err
	}
	packagePin, err := os.ReadFile(packagePinPath)
	if err != nil {
		return fmt.Errorf("read stamped pin %s: %w", unityPackageCliPinFile, err)
	}
	// The two pins must stay byte-identical; the mirror is a copy, never a re-encode.
	err = os.WriteFile(projectPinPath, packagePin, 0o644)
	if err != nil {
		return fmt.Errorf("mirror stamped pin to %s: %w", unityProjectCliPinFile, err)
	}
	// Read the mirror back instead of trusting the write, so the guard sees the
	// same bytes CI will compare.
	projectPin, err := os.ReadFile(projectPinPath)
	if err != nil {
		return fmt.Errorf("read mirrored pin %s: %w", unityProjectCliPinFile, err)
	}
	err = ValidateDispatcherPinOffline(packagePin, projectPin)
	if err != nil {
		return err
	}
	return deps.verifySubjects(ctx, packagePin)
}

func dispatcherPinFilesChanged(ctx context.Context, repositoryRoot string, deps dispatcherPinPRDeps) (bool, error) {
	output, err := deps.runOutput(
		ctx, "git", "-C", repositoryRoot, "status", "--porcelain", "--",
		unityPackageCliPinFile, unityProjectCliPinFile)
	if err != nil {
		return false, err
	}
	return strings.TrimSpace(output) != "", nil
}

func commitAndPushDispatcherPin(
	ctx context.Context,
	stdout io.Writer,
	repositoryRoot string,
	config dispatcherPinPRConfig,
	branch string,
	deps dispatcherPinPRDeps,
) error {
	version := strings.TrimPrefix(config.tag, dispatcherPinTagPrefix)
	_, err := deps.runOutput(
		ctx, "git", "-C", repositoryRoot, "add", "--",
		unityPackageCliPinFile, unityProjectCliPinFile)
	if err != nil {
		return err
	}
	_, err = deps.runOutput(
		ctx, "git", "-C", repositoryRoot,
		"-c", "user.name="+dispatcherPinPRAuthorName,
		"-c", "user.email="+dispatcherPinPRAuthorEmail,
		"commit",
		"--message", dispatcherPinPRTitle(version),
		"--message", dispatcherPinPRCommitBody(config.tag))
	if err != nil {
		return err
	}
	err = pushDispatcherPinBranch(ctx, repositoryRoot, branch, deps)
	if err != nil {
		return err
	}
	writeDispatcherPinPRLine(stdout, fmt.Sprintf("Pushed the stamped dispatcher pin to %s.", branch))
	return nil
}

// pushDispatcherPinBranch replaces an earlier attempt's branch only when the
// remote still holds the commit this run observed, so a concurrent push is
// never discarded.
func pushDispatcherPinBranch(ctx context.Context, repositoryRoot string, branch string, deps dispatcherPinPRDeps) error {
	branchRef := "refs/heads/" + branch
	output, err := deps.runOutput(ctx, "git", "-C", repositoryRoot, "ls-remote", "--heads", "origin", branchRef)
	if err != nil {
		return err
	}
	remoteSHA := dispatcherPinRemoteBranchSHA(output)
	if remoteSHA == "" {
		_, err = deps.runOutput(ctx, "git", "-C", repositoryRoot, "push", "origin", "HEAD:"+branchRef)
		return err
	}
	_, err = deps.runOutput(
		ctx, "git", "-C", repositoryRoot, "push",
		"--force-with-lease="+branchRef+":"+remoteSHA,
		"origin", "HEAD:"+branchRef)
	return err
}

func dispatcherPinRemoteBranchSHA(lsRemoteOutput string) string {
	for _, line := range strings.Split(lsRemoteOutput, "\n") {
		sha, _, found := strings.Cut(strings.TrimSpace(line), "\t")
		if found && sha != "" {
			return sha
		}
	}
	return ""
}

func publishDispatcherPinPullRequest(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config dispatcherPinPRConfig,
	branch string,
	deps dispatcherPinPRDeps,
) int {
	pullRequest, err := ensureDispatcherPinPullRequest(ctx, config, branch, deps)
	if err != nil {
		writeDispatcherPinPRLine(stderr, "open-dispatcher-pin-pr:", err)
		return 1
	}
	writeDispatcherPinPRLine(stdout, fmt.Sprintf("Dispatcher pin pull request #%d is open: %s", pullRequest.Number, pullRequest.URL))

	description := fmt.Sprintf("dispatcher pin PR #%d: %s", pullRequest.Number, pullRequest.URL)
	err = deps.dispatchChecks(ctx, stdout, config.repository, branch, description)
	if err != nil {
		writeDispatcherPinPRLine(stderr, "open-dispatcher-pin-pr:", err)
		return 1
	}
	return 0
}

func ensureDispatcherPinPullRequest(
	ctx context.Context,
	config dispatcherPinPRConfig,
	branch string,
	deps dispatcherPinPRDeps,
) (dispatcherPinPullRequest, error) {
	version := strings.TrimPrefix(config.tag, dispatcherPinTagPrefix)
	bodyFile, cleanup, err := writePullRequestBodyFile(dispatcherPinPRBody(config.tag, version))
	if err != nil {
		return dispatcherPinPullRequest{}, err
	}
	defer cleanup()

	existing, found, err := findDispatcherPinPullRequest(ctx, config, branch, deps)
	if err != nil {
		return dispatcherPinPullRequest{}, err
	}
	if found {
		_, err = deps.runOutput(
			ctx, "gh", "pr", "edit", strconv.Itoa(existing.Number),
			"--repo", config.repository,
			"--title", dispatcherPinPRTitle(version),
			"--body-file", bodyFile)
		if err != nil {
			return dispatcherPinPullRequest{}, err
		}
		return existing, nil
	}

	_, err = deps.runOutput(
		ctx, "gh", "pr", "create",
		"--repo", config.repository,
		"--base", config.baseBranch,
		"--head", branch,
		"--title", dispatcherPinPRTitle(version),
		"--body-file", bodyFile)
	if err != nil {
		return dispatcherPinPullRequest{}, err
	}

	created, found, err := findDispatcherPinPullRequest(ctx, config, branch, deps)
	if err != nil {
		return dispatcherPinPullRequest{}, err
	}
	if !found {
		return dispatcherPinPullRequest{}, fmt.Errorf("created pull request for %s is not listed as open", branch)
	}
	return created, nil
}

func findDispatcherPinPullRequest(
	ctx context.Context,
	config dispatcherPinPRConfig,
	branch string,
	deps dispatcherPinPRDeps,
) (dispatcherPinPullRequest, bool, error) {
	output, err := deps.runOutput(
		ctx, "gh", "pr", "list",
		"--repo", config.repository,
		"--state", "open",
		"--base", config.baseBranch,
		"--head", branch,
		"--json", "number,url")
	if err != nil {
		return dispatcherPinPullRequest{}, false, err
	}
	pullRequests := []dispatcherPinPullRequest{}
	err = json.Unmarshal([]byte(output), &pullRequests)
	if err != nil {
		return dispatcherPinPullRequest{}, false, fmt.Errorf("failed to parse dispatcher pin pull request list: %w", err)
	}
	if len(pullRequests) == 0 {
		return dispatcherPinPullRequest{}, false, nil
	}
	if len(pullRequests) > 1 {
		return dispatcherPinPullRequest{}, false, fmt.Errorf("expected one open pull request for %s, found %d", branch, len(pullRequests))
	}
	return pullRequests[0], true, nil
}

func dispatcherPinPRTitle(version string) string {
	return fmt.Sprintf("chore: update dispatcher pin to the %s stable release", version)
}

func dispatcherPinPRCommitBody(releaseTag string) string {
	return "Fresh installs resolve the dispatcher named by this pin, so until it records " +
		releaseTag + " every new install keeps landing on the previously pinned release.\n\n" +
		"minimumDispatcherVersion stays as it is: the package does not require a newer " +
		"dispatcher, and raising the floor would lock out working installs."
}

func dispatcherPinPRBody(releaseTag string, version string) string {
	return strings.Join([]string{
		"## Summary",
		"- Fresh CLI installs now resolve the " + version + " stable dispatcher instead of the previously pinned release.",
		"",
		"## User Impact",
		"- Before: `scripts/install.sh`, `scripts/install.ps1`, and Unity's Install CLI button all follow this pin, so a fresh install lands on the dispatcher release recorded before " + releaseTag + ".",
		"- After: fresh installs resolve `" + releaseTag + "` with its attestation-verified asset digests.",
		"",
		"## Changes",
		"- Stamped `dispatcherReleaseTag` and `dispatcherArchiveManifest` from the published `" + releaseTag + "` release.",
		"- `" + unityPackageCliPinFile + "` and `" + unityProjectCliPinFile + "` kept byte-identical.",
		"- `minimumDispatcherVersion` intentionally unchanged: the package does not require a newer dispatcher, and raising the floor would lock out working installs.",
		"",
		"## Verification",
		"- The stamp verified the published release attestations while writing the manifest.",
		"- Offline pin validation and a re-verification against the published release subjects both passed before this pull request was opened.",
		"- Required checks are dispatched by automation, because a pull request created with `GITHUB_TOKEN` does not trigger `pull_request` workflows.",
		"",
		"Opened automatically by the `dispatcher-publish` workflow after publishing `" + releaseTag + "`.",
		"",
	}, "\n")
}

func dispatcherPinPRRepositoryRoot(ctx context.Context) (string, error) {
	output, err := runReleasePRCheckCommandOutput(ctx, "git", "rev-parse", "--show-toplevel")
	if err != nil {
		return "", fmt.Errorf("resolve repository root: %w", err)
	}
	root := strings.TrimSpace(output)
	if root == "" {
		return "", fmt.Errorf("git returned an empty repository root")
	}
	return root, nil
}

func writeDispatcherPinPRLine(writer io.Writer, values ...any) {
	// CI status output failures cannot be recovered after the command outcome is known.
	_, _ = fmt.Fprintln(writer, values...)
}
