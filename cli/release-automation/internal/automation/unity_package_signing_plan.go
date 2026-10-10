package automation

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"regexp"
	"strings"
)

const (
	planUnityPackageSigningCommandName = "plan-unity-package-signing"
	unityPackageManifestKey            = "Packages/src"
	unityPackageName                   = "io.github.hatayama.uloopmcp"
)

// unityPackageReleaseTagPattern accepts only the Unity package component's tags. The CLI
// components share the repository and its releases, so their tags must never select the package
// source for signing.
var unityPackageReleaseTagPattern = regexp.MustCompile(`^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$`)

// releaseCommitPattern matches the full commit SHA the release sync passes as a draft's target. A
// draft has no tag until it is published, so the target is the only record of the release commit.
var releaseCommitPattern = regexp.MustCompile(`^[0-9a-f]{40}$`)

// unityPackageSigningOptions selects the release whose signed tarball is planned. An empty Tag
// means the Unity package version recorded in the release-please manifest under RepoRoot. DryRun
// signs any existing release, draft or published, and never publishes, because it is how a new
// UPM CLI pin is validated when no draft is waiting.
type unityPackageSigningOptions struct {
	Repository string
	RepoRoot   string
	Tag        string
	DryRun     bool
}

type unityPackageSigningDeps struct {
	runOutput func(context.Context, string, ...string) (string, error)
}

// unityPackageSigningPlan says whether the release is signed and whether the signed tarball is
// then attached and the draft published. SourceRef is what the signing job checks out: the
// draft's target commit, or the tag of a published release. Warning marks a reason that needs a
// person's attention even though the run succeeds.
type unityPackageSigningPlan struct {
	Sign      bool
	Publish   bool
	Tag       string
	Version   string
	AssetName string
	SourceRef string
	Reason    string
	Warning   bool
}

type unityPackageRelease struct {
	IsDraft         bool                       `json:"isDraft"`
	TargetCommitish string                     `json:"targetCommitish"`
	Assets          []unityPackageReleaseAsset `json:"assets"`
}

type unityPackageReleaseAsset struct {
	Name string `json:"name"`
	Size int64  `json:"size"`
}

// RunPlanUnityPackageSigning prints GITHUB_OUTPUT lines (sign, publish, tag, version, asset-name,
// source-ref) that tell the signing workflow whether to sign the Unity package release and
// publish it. The release sync leaves the release a draft, and a published release is immutable,
// so the tarball OpenUPM republishes can only be attached before this workflow publishes it.
func RunPlanUnityPackageSigning(ctx context.Context, stdout io.Writer, stderr io.Writer, args []string) int {
	options, err := parseUnityPackageSigningOptions(ctx, args, os.LookupEnv)
	if err != nil {
		_, _ = fmt.Fprintln(stderr, planUnityPackageSigningCommandName+":", err)
		return 2
	}
	return runPlanUnityPackageSigningWithDeps(ctx, stdout, stderr, options, unityPackageSigningDeps{runOutput: runCommandOutput})
}

func parseUnityPackageSigningOptions(ctx context.Context, args []string, lookupEnv func(string) (string, bool)) (unityPackageSigningOptions, error) {
	flags := flag.NewFlagSet(planUnityPackageSigningCommandName, flag.ContinueOnError)
	flags.SetOutput(io.Discard)
	repository := flags.String("repo", "", "owner/name of the repository; defaults to GITHUB_REPOSITORY")
	repoRoot := flags.String("repo-root", "", "repository root holding the release-please manifest (default: the current git repository root)")
	tag := flags.String("tag", "", "Unity package release tag to plan (default: the manifest version)")
	dryRun := flags.Bool("dry-run", false, "sign any existing release without publishing it")
	if err := flags.Parse(args); err != nil {
		return unityPackageSigningOptions{}, err
	}

	if *repository == "" {
		*repository, _ = lookupEnv("GITHUB_REPOSITORY")
	}
	if *repository == "" {
		return unityPackageSigningOptions{}, errors.New("--repo or GITHUB_REPOSITORY is required")
	}
	if *repoRoot == "" {
		resolvedRoot, err := gitRepoRoot(ctx)
		if err != nil {
			return unityPackageSigningOptions{}, fmt.Errorf("failed to resolve the repository root: %w", err)
		}
		*repoRoot = resolvedRoot
	}
	return unityPackageSigningOptions{Repository: *repository, RepoRoot: *repoRoot, Tag: *tag, DryRun: *dryRun}, nil
}

func runPlanUnityPackageSigningWithDeps(ctx context.Context, stdout io.Writer, stderr io.Writer, options unityPackageSigningOptions, deps unityPackageSigningDeps) int {
	plan, err := planUnityPackageSigning(ctx, options, deps)
	if err != nil {
		_, _ = fmt.Fprintln(stderr, planUnityPackageSigningCommandName+":", err)
		return 1
	}
	// stdout is redirected into GITHUB_OUTPUT, so the warning command goes to stderr, which the
	// runner scans for workflow commands as well.
	if plan.Warning {
		_, _ = fmt.Fprintln(stderr, "::warning::"+plan.Reason)
	} else {
		_, _ = fmt.Fprintln(stderr, plan.Reason)
	}
	_, _ = fmt.Fprintf(stdout, "sign=%t\npublish=%t\ntag=%s\nversion=%s\nasset-name=%s\nsource-ref=%s\n",
		plan.Sign, plan.Publish, plan.Tag, plan.Version, plan.AssetName, plan.SourceRef)
	return 0
}

func planUnityPackageSigning(ctx context.Context, options unityPackageSigningOptions, deps unityPackageSigningDeps) (unityPackageSigningPlan, error) {
	tag, err := resolveUnityPackageReleaseTag(options)
	if err != nil {
		return unityPackageSigningPlan{}, err
	}
	version := strings.TrimPrefix(tag, "v")
	plan := unityPackageSigningPlan{Tag: tag, Version: version, AssetName: signedUnityPackageAssetName(version)}

	release, found, err := viewUnityPackageRelease(ctx, options.Repository, tag, deps)
	if err != nil {
		return unityPackageSigningPlan{}, err
	}
	if !found {
		plan.Reason = fmt.Sprintf("Release %s does not exist yet; the release sync creates it as a draft.", tag)
		return plan, nil
	}
	if release.IsDraft {
		return planDraftUnityPackageSigning(plan, release, options.DryRun)
	}
	return planPublishedUnityPackageSigning(plan, release, options.DryRun), nil
}

// planDraftUnityPackageSigning signs every draft and publishes it unless this is a dry run. A
// draft that already carries the tarball is one whose publish step failed after the upload, so it
// is signed again and the upload replaces the earlier tarball.
func planDraftUnityPackageSigning(plan unityPackageSigningPlan, release unityPackageRelease, dryRun bool) (unityPackageSigningPlan, error) {
	if !releaseCommitPattern.MatchString(release.TargetCommitish) {
		return unityPackageSigningPlan{}, fmt.Errorf("draft release %s targets %q, not a full commit SHA, so its release commit is unknown", plan.Tag, release.TargetCommitish)
	}
	plan.Sign = true
	plan.Publish = !dryRun
	plan.SourceRef = release.TargetCommitish
	if dryRun {
		plan.Reason = fmt.Sprintf("Dry run: signing draft release %s without publishing it.", plan.Tag)
		return plan, nil
	}
	plan.Reason = fmt.Sprintf("Release %s is a draft; signing it, attaching %s, and publishing it.", plan.Tag, plan.AssetName)
	return plan, nil
}

// planPublishedUnityPackageSigning never publishes: a published release is immutable. A missing
// tarball is reported as a warning, not a failure, because no run can attach it any more, and
// failing would repeat on every release-please run until the next version is released.
func planPublishedUnityPackageSigning(plan unityPackageSigningPlan, release unityPackageRelease, dryRun bool) unityPackageSigningPlan {
	plan.SourceRef = "refs/tags/" + plan.Tag
	switch {
	case dryRun:
		plan.Sign = true
		plan.Reason = fmt.Sprintf("Dry run: signing published release %s without attaching the tarball.", plan.Tag)
	case release.hasNonEmptyAsset(plan.AssetName):
		plan.Reason = fmt.Sprintf("Release %s already carries %s.", plan.Tag, plan.AssetName)
	default:
		plan.Warning = true
		plan.Reason = fmt.Sprintf("Release %s was published without %s; a published release is immutable, so it stays unsigned.", plan.Tag, plan.AssetName)
	}
	return plan
}

func resolveUnityPackageReleaseTag(options unityPackageSigningOptions) (string, error) {
	tag := options.Tag
	if tag == "" {
		version, err := readUnityPackageManifestVersion(options.RepoRoot)
		if err != nil {
			return "", err
		}
		tag = "v" + version
	}
	if !unityPackageReleaseTagPattern.MatchString(tag) {
		return "", fmt.Errorf("%q is not a Unity package release tag (expected v<major>.<minor>.<patch>)", tag)
	}
	return tag, nil
}

func readUnityPackageManifestVersion(repoRoot string) (string, error) {
	content, err := os.ReadFile(filepath.Join(repoRoot, releasePleaseManifestRelativePath))
	if err != nil {
		return "", fmt.Errorf("read release-please manifest: %w", err)
	}
	var manifest map[string]string
	if err := json.Unmarshal(content, &manifest); err != nil {
		return "", fmt.Errorf("parse release-please manifest: %w", err)
	}
	version := manifest[unityPackageManifestKey]
	if version == "" {
		return "", fmt.Errorf("release-please manifest has no version for %s", unityPackageManifestKey)
	}
	return version, nil
}

// viewUnityPackageRelease reports found=false only for a missing release. Any other gh failure is
// returned, because treating an outage as "nothing to sign" would leave the release unsigned.
func viewUnityPackageRelease(ctx context.Context, repository string, tag string, deps unityPackageSigningDeps) (unityPackageRelease, bool, error) {
	output, err := deps.runOutput(ctx, "gh", "release", "view", tag, "--repo", repository, "--json", "isDraft,targetCommitish,assets")
	if err != nil {
		if isGhReleaseNotFound(err) {
			return unityPackageRelease{}, false, nil
		}
		return unityPackageRelease{}, false, err
	}
	var release unityPackageRelease
	if err := json.Unmarshal([]byte(output), &release); err != nil {
		return unityPackageRelease{}, false, fmt.Errorf("parse release %s: %w", tag, err)
	}
	return release, true, nil
}

func isGhReleaseNotFound(err error) bool {
	message := err.Error()
	return strings.Contains(message, "release not found") || strings.Contains(message, "HTTP 404")
}

// hasNonEmptyAsset ignores a zero-byte asset, which an interrupted upload can leave behind and
// which OpenUPM could not install.
func (release unityPackageRelease) hasNonEmptyAsset(name string) bool {
	for _, asset := range release.Assets {
		if asset.Name == name && asset.Size > 0 {
			return true
		}
	}
	return false
}

// signedUnityPackageAssetName is the release asset OpenUPM picks up; its fixed prefix is what the
// OpenUPM package metadata matches with githubReleaseAssetName.
func signedUnityPackageAssetName(version string) string {
	return unityPackageName + "-" + version + ".tgz"
}
