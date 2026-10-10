package automation

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"os"
	"path"
	"path/filepath"
	"strconv"
	"strings"
	"time"
)

const (
	createUnityPackageReleaseCommandName = "create-unity-package-release"
	// unityPackageReleaseRereadAttempts and unityPackageReleaseRereadInterval bound how long a
	// read that follows a write is retried: GitHub can answer it from a copy that does not have
	// the write yet.
	unityPackageReleaseRereadAttempts = 3
	unityPackageReleaseRereadInterval = 15 * time.Second
	unityPackageChangelogHeadingStart = "## ["
)

// unityPackageReleaseCreateOptions names the release to create: its tag, the release commit the
// tag must point at, and the signed tarball attached to it. RepoRoot is the git checkout the
// changelog of the release commit is read from.
type unityPackageReleaseCreateOptions struct {
	Repository string
	RepoRoot   string
	Tag        string
	Commit     string
	Asset      string
}

type unityPackageReleaseCreateDeps struct {
	runOutput func(context.Context, string, ...string) (string, error)
	sleep     func(time.Duration)
}

// unityPackageReleaseTagRef is the git ref lookup of a tag. A tag created through the refs API is
// lightweight, so its object is the commit itself.
type unityPackageReleaseTagRef struct {
	Ref    string `json:"ref"`
	Object struct {
		SHA  string `json:"sha"`
		Type string `json:"type"`
	} `json:"object"`
}

type unityPackageReleaseListing struct {
	ID      int64  `json:"id"`
	TagName string `json:"tag_name"`
	Draft   bool   `json:"draft"`
}

type unityPackageReleaseConfig struct {
	Packages map[string]struct {
		ChangelogPath string `json:"changelog-path"`
	} `json:"packages"`
}

// unityPackageReleaseState is what the creation learned before its first write.
type unityPackageReleaseState struct {
	tagExists bool
	draftIDs  []int64
	notes     string
}

// RunCreateUnityPackageRelease creates the Unity package release of --tag at --commit with the
// signed tarball --asset attached, and publishes it in the same step, because a published release
// is immutable. Every reason not to create it is found before the first write. A rerun after the
// release is published with the tarball does nothing.
func RunCreateUnityPackageRelease(ctx context.Context, stdout io.Writer, stderr io.Writer, args []string) int {
	options, err := parseUnityPackageReleaseCreateOptions(ctx, args, os.LookupEnv)
	if err != nil {
		_, _ = fmt.Fprintln(stderr, createUnityPackageReleaseCommandName+":", err)
		return 2
	}
	return runCreateUnityPackageReleaseWithDeps(ctx, stdout, stderr, options, unityPackageReleaseCreateDeps{runOutput: runCommandOutput, sleep: time.Sleep})
}

func parseUnityPackageReleaseCreateOptions(ctx context.Context, args []string, lookupEnv func(string) (string, bool)) (unityPackageReleaseCreateOptions, error) {
	flags := flag.NewFlagSet(createUnityPackageReleaseCommandName, flag.ContinueOnError)
	flags.SetOutput(io.Discard)
	repository := flags.String("repo", "", "owner/name of the repository; defaults to GITHUB_REPOSITORY")
	repoRoot := flags.String("repo-root", "", "git checkout holding the release commit (default: the current git repository root)")
	tag := flags.String("tag", "", "Unity package release tag to create")
	commit := flags.String("commit", "", "full SHA of the release commit the tag points at")
	asset := flags.String("asset", "", "signed tarball to attach")
	if err := flags.Parse(args); err != nil {
		return unityPackageReleaseCreateOptions{}, err
	}
	if *repository == "" {
		*repository, _ = lookupEnv("GITHUB_REPOSITORY")
	}
	if *repository == "" {
		return unityPackageReleaseCreateOptions{}, errors.New("--repo or GITHUB_REPOSITORY is required")
	}
	for _, required := range []struct{ name, value string }{{"--tag", *tag}, {"--commit", *commit}, {"--asset", *asset}} {
		if required.value == "" {
			return unityPackageReleaseCreateOptions{}, fmt.Errorf("%s is required", required.name)
		}
	}
	if *repoRoot == "" {
		resolvedRoot, err := gitRepoRoot(ctx)
		if err != nil {
			return unityPackageReleaseCreateOptions{}, fmt.Errorf("failed to resolve the repository root: %w", err)
		}
		*repoRoot = resolvedRoot
	}
	return unityPackageReleaseCreateOptions{Repository: *repository, RepoRoot: *repoRoot, Tag: *tag, Commit: *commit, Asset: *asset}, nil
}

func runCreateUnityPackageReleaseWithDeps(ctx context.Context, stdout io.Writer, stderr io.Writer, options unityPackageReleaseCreateOptions, deps unityPackageReleaseCreateDeps) int {
	if err := validateUnityPackageReleaseCreateOptions(options); err != nil {
		_, _ = fmt.Fprintln(stderr, createUnityPackageReleaseCommandName+":", err)
		return 2
	}
	if err := createUnityPackageRelease(ctx, stdout, options, deps); err != nil {
		_, _ = fmt.Fprintln(stderr, createUnityPackageReleaseCommandName+":", err)
		return 1
	}
	return 0
}

func validateUnityPackageReleaseCreateOptions(options unityPackageReleaseCreateOptions) error {
	if !unityPackageReleaseTagPattern.MatchString(options.Tag) {
		return fmt.Errorf("%q is not a Unity package release tag (expected v<major>.<minor>.<patch>)", options.Tag)
	}
	if !releaseCommitPattern.MatchString(options.Commit) {
		return fmt.Errorf("--commit %q is not a full commit SHA", options.Commit)
	}
	wantName := signedUnityPackageAssetName(strings.TrimPrefix(options.Tag, "v"))
	if filepath.Base(options.Asset) != wantName {
		return fmt.Errorf("--asset %s is not named %s, the asset OpenUPM looks for", options.Asset, wantName)
	}
	info, err := os.Stat(options.Asset)
	if err != nil {
		return fmt.Errorf("--asset: %w", err)
	}
	if !info.Mode().IsRegular() || info.Size() == 0 {
		return fmt.Errorf("--asset %s is not a non-empty file", options.Asset)
	}
	return nil
}

func createUnityPackageRelease(ctx context.Context, stdout io.Writer, options unityPackageReleaseCreateOptions, deps unityPackageReleaseCreateDeps) error {
	assetName := filepath.Base(options.Asset)
	release, published, err := lookupPublishedUnityPackageRelease(ctx, deps.runOutput, options.Repository, options.Tag)
	if err != nil {
		return err
	}
	if published {
		if release.hasNonEmptyAsset(assetName) {
			_, _ = fmt.Fprintf(stdout, "Release %s is already published with %s; nothing to do.\n", options.Tag, assetName)
			return nil
		}
		return fmt.Errorf("release %s is published without %s; a published release is immutable, so it cannot be recreated", options.Tag, assetName)
	}

	state, err := readUnityPackageReleaseState(ctx, options, deps)
	if err != nil {
		return err
	}
	if err := prepareUnityPackageReleaseTag(ctx, stdout, options, state, deps); err != nil {
		return err
	}
	if _, err := deps.runOutput(ctx, "gh", unityPackageReleaseCreateArgs(options, state.notes)...); err != nil {
		return err
	}
	return confirmUnityPackageReleasePublished(ctx, stdout, options, assetName, deps)
}

// readUnityPackageReleaseState performs every read the creation depends on, so that a tag at the
// wrong commit, a failed lookup, or missing release notes stop the run before it writes anything.
func readUnityPackageReleaseState(ctx context.Context, options unityPackageReleaseCreateOptions, deps unityPackageReleaseCreateDeps) (unityPackageReleaseState, error) {
	tagCommit, tagExists, err := lookupUnityPackageReleaseTag(ctx, deps, options.Repository, options.Tag)
	if err != nil {
		return unityPackageReleaseState{}, err
	}
	if tagExists && tagCommit != options.Commit {
		return unityPackageReleaseState{}, fmt.Errorf("tag %s points at %s, not release commit %s; tags cannot be moved, so this release cannot be created", options.Tag, tagCommit, options.Commit)
	}
	draftIDs, err := listUnityPackageReleaseDrafts(ctx, deps, options.Repository, options.Tag)
	if err != nil {
		return unityPackageReleaseState{}, err
	}
	notes, err := readUnityPackageReleaseNotes(ctx, deps, options)
	if err != nil {
		return unityPackageReleaseState{}, err
	}
	return unityPackageReleaseState{tagExists: tagExists, draftIDs: draftIDs, notes: notes}, nil
}

// prepareUnityPackageReleaseTag deletes the drafts an interrupted run left behind and creates the
// tag at the release commit. The tag is created explicitly rather than by the release, because
// the release then attaches to a tag already known to point at the signed commit.
func prepareUnityPackageReleaseTag(ctx context.Context, stdout io.Writer, options unityPackageReleaseCreateOptions, state unityPackageReleaseState, deps unityPackageReleaseCreateDeps) error {
	for _, draftID := range state.draftIDs {
		if _, err := deps.runOutput(ctx, "gh", "api", "-X", "DELETE", "repos/"+options.Repository+"/releases/"+strconv.FormatInt(draftID, 10)); err != nil {
			return err
		}
		_, _ = fmt.Fprintf(stdout, "Deleted leftover draft release %d of %s.\n", draftID, options.Tag)
	}
	if state.tagExists {
		return nil
	}
	if _, err := deps.runOutput(ctx, "gh", "api", "repos/"+options.Repository+"/git/refs", "-f", "ref=refs/tags/"+options.Tag, "-f", "sha="+options.Commit); err != nil {
		return err
	}
	_, _ = fmt.Fprintf(stdout, "Created tag %s at %s.\n", options.Tag, options.Commit)
	visible, err := rereadUnityPackageRelease(deps, func() (bool, error) {
		tagCommit, tagExists, err := lookupUnityPackageReleaseTag(ctx, deps, options.Repository, options.Tag)
		if err != nil || !tagExists {
			return false, err
		}
		if tagCommit != options.Commit {
			return false, fmt.Errorf("tag %s points at %s right after it was created at %s", options.Tag, tagCommit, options.Commit)
		}
		return true, nil
	})
	if err != nil {
		return err
	}
	if !visible {
		return fmt.Errorf("tag %s is still not visible after it was created; rerun this workflow to create the release", options.Tag)
	}
	return nil
}

// unityPackageReleaseCreateArgs uploads the tarball as part of the creation: gh creates the
// release as a draft, uploads the asset, and only then publishes it, deleting the draft if the
// upload fails, so the release is never published without the tarball. The notes are passed in
// the flag's own argument, so text that starts with a dash is never read as another flag.
func unityPackageReleaseCreateArgs(options unityPackageReleaseCreateOptions, notes string) []string {
	args := []string{
		"release", "create", options.Tag, options.Asset, "--repo", options.Repository,
		"--verify-tag", "--title", options.Tag, "--notes=" + notes, "--target", options.Commit,
	}
	if strings.Contains(options.Tag, "-") {
		args = append(args, "--prerelease")
	}
	return args
}

// confirmUnityPackageReleasePublished checks the postcondition the workflow promises: the release
// OpenUPM reads is published and carries a non-empty tarball.
func confirmUnityPackageReleasePublished(ctx context.Context, stdout io.Writer, options unityPackageReleaseCreateOptions, assetName string, deps unityPackageReleaseCreateDeps) error {
	confirmed, err := rereadUnityPackageRelease(deps, func() (bool, error) {
		release, published, err := lookupPublishedUnityPackageRelease(ctx, deps.runOutput, options.Repository, options.Tag)
		if err != nil {
			return false, err
		}
		return published && release.hasNonEmptyAsset(assetName), nil
	})
	if err != nil {
		return err
	}
	if !confirmed {
		return fmt.Errorf("release %s was created but is not published with a non-empty %s", options.Tag, assetName)
	}
	_, _ = fmt.Fprintf(stdout, "Published release %s at %s with %s.\n", options.Tag, options.Commit, assetName)
	return nil
}

// rereadUnityPackageRelease retries a read that follows a write until it reports the write. A
// failed read is returned at once: only a stale answer is worth waiting out.
func rereadUnityPackageRelease(deps unityPackageReleaseCreateDeps, read func() (bool, error)) (bool, error) {
	for attempt := 1; ; attempt++ {
		done, err := read()
		if err != nil || done {
			return done, err
		}
		if attempt == unityPackageReleaseRereadAttempts {
			return false, nil
		}
		deps.sleep(unityPackageReleaseRereadInterval)
	}
}

// lookupUnityPackageReleaseTag returns the commit the tag points at. A 404 means the tag does not
// exist; any other failure is returned, because creating a tag that exists elsewhere would fail
// only after the drafts are already deleted.
func lookupUnityPackageReleaseTag(ctx context.Context, deps unityPackageReleaseCreateDeps, repository string, tag string) (string, bool, error) {
	output, err := deps.runOutput(ctx, "gh", "api", "repos/"+repository+"/git/ref/tags/"+tag)
	if err != nil {
		if strings.Contains(err.Error(), "HTTP 404") {
			return "", false, nil
		}
		return "", false, err
	}
	var ref unityPackageReleaseTagRef
	if err := json.Unmarshal([]byte(output), &ref); err != nil {
		return "", false, fmt.Errorf("parse tag %s: %w", tag, err)
	}
	if ref.Ref != "refs/tags/"+tag || ref.Object.Type != "commit" {
		return "", false, fmt.Errorf("tag lookup for %s answered with %s (%s), not a lightweight tag of a commit", tag, ref.Ref, ref.Object.Type)
	}
	return ref.Object.SHA, true, nil
}

// listUnityPackageReleaseDrafts returns the IDs of the drafts of the tag. A draft does not own its
// tag, so an interrupted creation can leave more than one; only the listing shows drafts. The
// output is decoded as it comes: --paginate prints one JSON array per page back to back.
func listUnityPackageReleaseDrafts(ctx context.Context, deps unityPackageReleaseCreateDeps, repository string, tag string) ([]int64, error) {
	output, err := deps.runOutput(ctx, "gh", "api", "--paginate", "repos/"+repository+"/releases?per_page=100")
	if err != nil {
		return nil, err
	}
	var draftIDs []int64
	decoder := json.NewDecoder(strings.NewReader(output))
	for {
		var page []unityPackageReleaseListing
		err := decoder.Decode(&page)
		if errors.Is(err, io.EOF) {
			return draftIDs, nil
		}
		if err != nil {
			return nil, fmt.Errorf("parse release listing: %w", err)
		}
		for _, release := range page {
			if release.Draft && release.TagName == tag {
				draftIDs = append(draftIDs, release.ID)
			}
		}
	}
}

// readUnityPackageReleaseNotes returns the changelog section of the version as the release commit
// recorded it, read from that commit rather than the checkout so a later changelog edit cannot
// change what the release says.
func readUnityPackageReleaseNotes(ctx context.Context, deps unityPackageReleaseCreateDeps, options unityPackageReleaseCreateOptions) (string, error) {
	configContent, err := deps.runOutput(ctx, "git", "-C", options.RepoRoot, "show", options.Commit+":release-please-config.json")
	if err != nil {
		return "", err
	}
	var config unityPackageReleaseConfig
	if err := json.Unmarshal([]byte(configContent), &config); err != nil {
		return "", fmt.Errorf("parse release-please config at %s: %w", options.Commit, err)
	}
	changelogPath := config.Packages[unityPackageManifestKey].ChangelogPath
	if changelogPath == "" {
		return "", fmt.Errorf("release-please config at %s has no changelog-path for %s", options.Commit, unityPackageManifestKey)
	}
	// release-please resolves a changelog path that starts with "/" from the repository root and
	// any other from the package directory.
	if strings.HasPrefix(changelogPath, "/") {
		changelogPath = strings.TrimPrefix(changelogPath, "/")
	} else {
		changelogPath = path.Join(unityPackageManifestKey, changelogPath)
	}
	changelog, err := deps.runOutput(ctx, "git", "-C", options.RepoRoot, "show", options.Commit+":"+changelogPath)
	if err != nil {
		return "", err
	}
	return unityPackageChangelogSection(changelog, strings.TrimPrefix(options.Tag, "v"))
}

// unityPackageChangelogSection returns the lines from the version's heading up to the next
// version heading. The heading is matched as text, not as a pattern, so the dots of the version
// match only dots.
func unityPackageChangelogSection(changelog string, version string) (string, error) {
	heading := unityPackageChangelogHeadingStart + version + "]"
	lines := strings.Split(strings.ReplaceAll(changelog, "\r\n", "\n"), "\n")
	start := -1
	for index, line := range lines {
		if strings.HasPrefix(line, heading) {
			start = index
			break
		}
	}
	if start < 0 {
		return "", fmt.Errorf("changelog has no %s heading", heading)
	}
	end := len(lines)
	for index := start + 1; index < len(lines); index++ {
		if strings.HasPrefix(lines[index], unityPackageChangelogHeadingStart) {
			end = index
			break
		}
	}
	return strings.TrimRight(strings.Join(lines[start:end], "\n"), "\n") + "\n", nil
}
