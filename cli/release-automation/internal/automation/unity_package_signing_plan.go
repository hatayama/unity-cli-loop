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

// unityPackageSigningOptions selects the release whose signed tarball is planned. An empty Tag
// means the Unity package version recorded in the release-please manifest under RepoRoot. DryRun
// signs a published release even when it already carries the tarball, because a dry run never
// uploads and is how a new UPM CLI pin is validated once every release is signed.
type unityPackageSigningOptions struct {
	Repository string
	RepoRoot   string
	Tag        string
	DryRun     bool
}

type unityPackageSigningDeps struct {
	runOutput func(context.Context, string, ...string) (string, error)
}

// unityPackageSigningPlan says whether the release still needs its signed tarball, and names the
// tag, package version, and release asset the signing and publish jobs work with.
type unityPackageSigningPlan struct {
	Sign      bool
	Tag       string
	Version   string
	AssetName string
	Reason    string
}

type unityPackageRelease struct {
	IsDraft bool                       `json:"isDraft"`
	Assets  []unityPackageReleaseAsset `json:"assets"`
}

type unityPackageReleaseAsset struct {
	Name string `json:"name"`
	Size int64  `json:"size"`
}

// RunPlanUnityPackageSigning prints GITHUB_OUTPUT lines (sign, tag, version, asset-name) that tell
// the signing workflow whether the Unity package release still lacks its signed tarball. OpenUPM
// republishes that tarball unchanged, so a release without it never reaches users as signed.
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
	dryRun := flags.Bool("dry-run", false, "sign a published release even when it already carries the signed tarball")
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
	_, _ = fmt.Fprintln(stderr, plan.Reason)
	_, _ = fmt.Fprintf(stdout, "sign=%t\ntag=%s\nversion=%s\nasset-name=%s\n", plan.Sign, plan.Tag, plan.Version, plan.AssetName)
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
	switch {
	case !found:
		plan.Reason = fmt.Sprintf("Release %s does not exist yet; the release sync creates it before it can be signed.", tag)
	case release.IsDraft:
		plan.Reason = fmt.Sprintf("Release %s is still a draft; it is signed once the release sync publishes it.", tag)
	case options.DryRun:
		plan.Sign = true
		plan.Reason = fmt.Sprintf("Dry run: signing release %s without attaching the tarball.", tag)
	case release.hasNonEmptyAsset(plan.AssetName):
		plan.Reason = fmt.Sprintf("Release %s already carries %s.", tag, plan.AssetName)
	default:
		plan.Sign = true
		plan.Reason = fmt.Sprintf("Release %s lacks %s; signing it.", tag, plan.AssetName)
	}
	return plan, nil
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
	output, err := deps.runOutput(ctx, "gh", "release", "view", tag, "--repo", repository, "--json", "isDraft,assets")
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

// hasNonEmptyAsset ignores a zero-byte asset so that a tarball left empty by an interrupted
// upload is replaced instead of being published to OpenUPM.
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
