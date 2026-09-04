package automation

import (
	"context"
	"flag"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
)

const (
	dispatcherPinPushCommandName = "push-dispatcher-pin"
	// The workflow bot identity GitHub assigns to GITHUB_TOKEN pushes, so the
	// stamp commit is attributed to automation instead of a human account. The
	// push itself travels over a GitHub App token that the branch ruleset lets
	// bypass the pull request requirement; the App identity is the pusher,
	// the bot identity stays the author.
	dispatcherPinPushAuthorName  = "github-actions[bot]"
	dispatcherPinPushAuthorEmail = "41898282+github-actions[bot]@users.noreply.github.com"
)

type dispatcherPinPushConfig struct {
	tag        string
	baseBranch string
}

type dispatcherPinPushDeps struct {
	runOutput      func(ctx context.Context, name string, args ...string) (string, error)
	repositoryRoot func(ctx context.Context) (string, error)
	stampPin       func(ctx context.Context, pinPath string, releaseTag string) error
	verifySubjects func(ctx context.Context, packagePin []byte) error
}

// RunPushDispatcherPin stamps the package pin from a published stable
// dispatcher release and pushes the stamp commit straight to the base branch.
// Fresh installs follow the pin, so the stamp must reach the base branch
// without waiting for a human to merge it.
func RunPushDispatcherPin(ctx context.Context, stdout io.Writer, stderr io.Writer, args []string) int {
	config, err := parseDispatcherPinPushFlags(args)
	if err != nil {
		writeDispatcherPinPushLine(stderr, dispatcherPinPushCommandName+":", err)
		return 1
	}
	return runPushDispatcherPinWithDeps(ctx, stdout, stderr, config, defaultDispatcherPinPushDeps())
}

func defaultDispatcherPinPushDeps() dispatcherPinPushDeps {
	return dispatcherPinPushDeps{
		runOutput:      runReleasePRCheckCommandOutput,
		repositoryRoot: dispatcherPinPushRepositoryRoot,
		stampPin:       StampDispatcherPin,
		verifySubjects: VerifyDispatcherPinSubjects,
	}
}

func parseDispatcherPinPushFlags(args []string) (dispatcherPinPushConfig, error) {
	flagSet := flag.NewFlagSet(dispatcherPinPushCommandName, flag.ContinueOnError)
	tag := flagSet.String("tag", "", "dispatcher release tag such as dispatcher-v3.0.0")
	baseBranch := flagSet.String("base-branch", "", "branch that receives the stamp commit")
	err := flagSet.Parse(args)
	if err != nil {
		return dispatcherPinPushConfig{}, err
	}
	if *baseBranch == "" {
		return dispatcherPinPushConfig{}, fmt.Errorf("--base-branch is required")
	}
	version, err := dispatcherVersionFromReleaseTag(*tag)
	if err != nil {
		return dispatcherPinPushConfig{}, err
	}
	// Pre-releases must never reach the pin on a stable branch: fresh installs
	// follow the pin, so stamping a pre-release would hand every new install a
	// pre-release dispatcher.
	if strings.Contains(version, "-") {
		return dispatcherPinPushConfig{}, fmt.Errorf("release tag %q is a pre-release and must not be stamped", *tag)
	}
	return dispatcherPinPushConfig{tag: *tag, baseBranch: *baseBranch}, nil
}

func runPushDispatcherPinWithDeps(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config dispatcherPinPushConfig,
	deps dispatcherPinPushDeps,
) int {
	repositoryRoot, err := deps.repositoryRoot(ctx)
	if err != nil {
		writeDispatcherPinPushLine(stderr, dispatcherPinPushCommandName+":", err)
		return 1
	}

	err = checkoutDispatcherPinBaseTip(ctx, repositoryRoot, config, deps)
	if err != nil {
		writeDispatcherPinPushLine(stderr, dispatcherPinPushCommandName+":", err)
		return 1
	}
	err = stampAndVerifyDispatcherPin(ctx, repositoryRoot, config.tag, deps)
	if err != nil {
		writeDispatcherPinPushLine(stderr, dispatcherPinPushCommandName+":", err)
		return 1
	}

	changed, err := dispatcherPinFilesChanged(ctx, repositoryRoot, deps)
	if err != nil {
		writeDispatcherPinPushLine(stderr, dispatcherPinPushCommandName+":", err)
		return 1
	}
	if !changed {
		writeDispatcherPinPushLine(stdout, fmt.Sprintf("Dispatcher pin already records %s; nothing to push.", config.tag))
		return 0
	}

	err = commitAndPushDispatcherPin(ctx, stdout, repositoryRoot, config, deps)
	if err != nil {
		writeDispatcherPinPushLine(stderr, dispatcherPinPushCommandName+":", err)
		return 1
	}
	return 0
}

// checkoutDispatcherPinBaseTip detaches at the base tip rather than the
// workflow's checked-out release commit, so the stamp lands on whatever the
// base branch holds when the release is published.
func checkoutDispatcherPinBaseTip(
	ctx context.Context,
	repositoryRoot string,
	config dispatcherPinPushConfig,
	deps dispatcherPinPushDeps,
) error {
	baseRef := "refs/remotes/origin/" + config.baseBranch
	fetchRefspec := "+refs/heads/" + config.baseBranch + ":" + baseRef
	_, err := deps.runOutput(ctx, "git", "-C", repositoryRoot, "fetch", "origin", fetchRefspec)
	if err != nil {
		return err
	}
	_, err = deps.runOutput(ctx, "git", "-C", repositoryRoot, "checkout", "--detach", baseRef)
	return err
}

func stampAndVerifyDispatcherPin(ctx context.Context, repositoryRoot string, releaseTag string, deps dispatcherPinPushDeps) error {
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

func dispatcherPinFilesChanged(ctx context.Context, repositoryRoot string, deps dispatcherPinPushDeps) (bool, error) {
	output, err := deps.runOutput(
		ctx, "git", "-C", repositoryRoot, "status", "--porcelain", "--",
		unityPackageCliPinFile, unityProjectCliPinFile)
	if err != nil {
		return false, err
	}
	return strings.TrimSpace(output) != "", nil
}

// commitAndPushDispatcherPin pushes without force: a rejected push means the
// base branch moved after the fetch, and a re-run of the job stamps the new
// tip. The freshness guard keeps failing on the base branch until that happens.
func commitAndPushDispatcherPin(
	ctx context.Context,
	stdout io.Writer,
	repositoryRoot string,
	config dispatcherPinPushConfig,
	deps dispatcherPinPushDeps,
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
		"-c", "user.name="+dispatcherPinPushAuthorName,
		"-c", "user.email="+dispatcherPinPushAuthorEmail,
		"commit",
		"--message", dispatcherPinCommitTitle(version),
		"--message", dispatcherPinCommitBody(config.tag))
	if err != nil {
		return err
	}
	_, err = deps.runOutput(ctx, "git", "-C", repositoryRoot, "push", "origin", "HEAD:refs/heads/"+config.baseBranch)
	if err != nil {
		return err
	}
	writeDispatcherPinPushLine(stdout, fmt.Sprintf("Pushed the stamped dispatcher pin for %s to %s.", config.tag, config.baseBranch))
	return nil
}

func dispatcherPinCommitTitle(version string) string {
	return fmt.Sprintf("chore: update dispatcher pin to the %s stable release", version)
}

func dispatcherPinCommitBody(releaseTag string) string {
	return "Fresh installs resolve the dispatcher named by this pin, so until it records " +
		releaseTag + " every new install keeps landing on the previously pinned release.\n\n" +
		"minimumDispatcherVersion stays as it is: the package does not require a newer " +
		"dispatcher, and raising the floor would lock out working installs.\n\n" +
		"Stamped and verified against the published release attestations by the " +
		"dispatcher-publish workflow."
}

func dispatcherPinPushRepositoryRoot(ctx context.Context) (string, error) {
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

func writeDispatcherPinPushLine(writer io.Writer, values ...any) {
	// CI status output failures cannot be recovered after the command outcome is known.
	_, _ = fmt.Fprintln(writer, values...)
}
