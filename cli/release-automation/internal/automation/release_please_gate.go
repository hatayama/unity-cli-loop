package automation

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"sort"
	"strings"
)

const releasePleaseGateCommandName = "check-release-please-gate"

type releasePleaseGateConfig struct {
	repository string
	ref        string
}

// RunReleasePleaseGateCheck writes skip=true to stdout while a version the
// release manifest at the ref names has no published release yet. release-please
// finds the previous release by tag, so rebuilding the release pull requests
// before that release is out would carry the previous changelog entries again.
func RunReleasePleaseGateCheck(ctx context.Context, stdout io.Writer, stderr io.Writer, args []string) int {
	return RunReleasePleaseGateCheckWithDeps(ctx, stdout, stderr, args, runCommandOutput)
}

func RunReleasePleaseGateCheckWithDeps(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	args []string,
	runOutput func(context.Context, string, ...string) (string, error),
) int {
	config, err := parseReleasePleaseGateFlags(args)
	if err != nil {
		writeMergePackageReleasePRLine(stderr, err)
		return 1
	}

	// A release that cannot be read fails the run instead of choosing a side:
	// running anyway repeats the previous changelog, and skipping silently would
	// leave the release pull requests unbuilt for good.
	unpublished, err := unpublishedManifestReleases(ctx, runOutput, config.repository, config.ref)
	if err != nil {
		writeMergePackageReleasePRLine(stderr, err)
		return 1
	}

	if len(unpublished) > 0 {
		writeMergePackageReleasePRLine(stderr, fmt.Sprintf(
			"release-please is skipped: %s not published yet; rebuilding the release pull requests now would repeat the previous changelog entries",
			strings.Join(unpublished, ", ")))
		writeMergePackageReleasePRLine(stdout, "skip=true")
		return 0
	}
	writeMergePackageReleasePRLine(stderr, fmt.Sprintf(
		"every release the manifest at %s names is published; release-please may run", config.ref))
	writeMergePackageReleasePRLine(stdout, "skip=false")
	return 0
}

func parseReleasePleaseGateFlags(args []string) (releasePleaseGateConfig, error) {
	flagSet := flag.NewFlagSet(releasePleaseGateCommandName, flag.ContinueOnError)
	flagSet.SetOutput(io.Discard)
	repository := flagSet.String("repo", "", "GitHub repository whose releases are checked")
	ref := flagSet.String("ref", "", "commit SHA or branch whose release manifest is read")
	err := flagSet.Parse(args)
	if err != nil {
		return releasePleaseGateConfig{}, fmt.Errorf("%s: %w", releasePleaseGateCommandName, err)
	}
	if *repository == "" {
		return releasePleaseGateConfig{}, errors.New(releasePleaseGateCommandName + ": --repo is required")
	}
	if *ref == "" {
		return releasePleaseGateConfig{}, errors.New(releasePleaseGateCommandName + ": --ref is required")
	}
	return releasePleaseGateConfig{repository: *repository, ref: *ref}, nil
}

// unpublishedManifestReleases lists the release tags the manifest at the ref
// names that are missing or still a draft, in manifest path order. While any
// is listed, release-please does not run on that ref.
func unpublishedManifestReleases(
	ctx context.Context,
	runOutput func(context.Context, string, ...string) (string, error),
	repository string,
	ref string,
) ([]string, error) {
	tags, err := releasePleaseManifestReleaseTags(ctx, runOutput, repository, ref)
	if err != nil {
		return nil, err
	}

	unpublished := []string{}
	for _, tag := range tags {
		published, err := releasePublished(ctx, runOutput, repository, tag)
		if err != nil {
			return nil, err
		}
		if !published {
			unpublished = append(unpublished, tag)
		}
	}
	return unpublished, nil
}

// releasePleaseManifestReleaseTags lists the release tag of every version the
// manifest at the ref names, in manifest path order.
func releasePleaseManifestReleaseTags(
	ctx context.Context,
	runOutput func(context.Context, string, ...string) (string, error),
	repository string,
	ref string,
) ([]string, error) {
	content, err := githubFileContentAtRef(ctx, runOutput, repository, releasePleaseManifestRelativePath, ref)
	if err != nil {
		return nil, err
	}

	manifest := map[string]string{}
	err = json.Unmarshal(content, &manifest)
	if err != nil {
		return nil, fmt.Errorf("%s: failed to parse %s at %s: %w",
			releasePleaseGateCommandName, releasePleaseManifestRelativePath, ref, err)
	}

	paths := make([]string, 0, len(manifest))
	for path := range manifest {
		paths = append(paths, path)
	}
	sort.Strings(paths)

	tags := []string{}
	for _, path := range paths {
		tag, err := releasePleaseManifestReleaseTag(ref, path, manifest[path])
		if err != nil {
			return nil, err
		}
		tags = append(tags, tag)
	}
	return tags, nil
}

// releasePleaseManifestReleaseTag maps one manifest entry to the tag its
// publish workflow creates. An unknown path fails rather than being skipped,
// since a component the gate does not know about could be released unseen.
func releasePleaseManifestReleaseTag(ref string, path string, version string) (string, error) {
	// A padded version would build a tag no release ever carries, so the gate
	// would skip release-please for good instead of reporting the bad manifest.
	if version == "" || version != strings.TrimSpace(version) {
		return "", fmt.Errorf("%s: %s at %s releases %q as %q, which is not a bare version",
			releasePleaseGateCommandName, releasePleaseManifestRelativePath, ref, path, version)
	}
	switch path {
	case "Packages/src":
		return "v" + version, nil
	case releasePleaseDispatcherPackage:
		return dispatcherReleaseTagPrefix + version, nil
	case "cli/project-runner":
		return projectRunnerReleaseTagPrefix + version, nil
	}
	return "", fmt.Errorf("%s: unknown release manifest path %q", releasePleaseGateCommandName, path)
}
