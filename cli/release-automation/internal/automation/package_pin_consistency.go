package automation

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"strings"
)

const (
	packagePinConsistencyCommandName  = "check-package-pin-consistency"
	releasePleaseManifestRelativePath = ".release-please-manifest.json"
	releasePleaseDispatcherPackage    = "cli/dispatcher"
)

type packagePinConsistencyConfig struct {
	repoRoot string
	ref      string
}

type packagePinConsistencyPin struct {
	DispatcherReleaseTag string `json:"dispatcherReleaseTag"`
}

// RunPackagePinConsistencyCheck fails when a ref would release the Unity
// package with a pin that records a different dispatcher than the same ref's
// release manifest publishes. The package tag is created against the release
// commit, so a stale pin there ships an outdated dispatcher to every fresh
// install for a whole release cycle.
func RunPackagePinConsistencyCheck(ctx context.Context, stdout io.Writer, stderr io.Writer, args []string) int {
	config, err := parsePackagePinConsistencyFlags(ctx, args)
	if err != nil {
		writePackagePinConsistencyLine(stderr, err)
		return 1
	}

	dispatcherVersion, err := packagePinConsistencyDispatcherVersion(ctx, config)
	if err != nil {
		writePackagePinConsistencyLine(stderr, err)
		return 1
	}
	pinnedTag, err := packagePinConsistencyPinnedTag(ctx, config)
	if err != nil {
		writePackagePinConsistencyLine(stderr, err)
		return 1
	}

	expectedTag := dispatcherReleaseTagPrefix + dispatcherVersion
	if pinnedTag != expectedTag {
		writePackagePinConsistencyLine(stderr, fmt.Errorf(
			"%s: %s would release the Unity package with pin %s, but its manifest releases dispatcher %s. "+
				"Merge the dispatcher release pull request first and wait for the pin stamp (docs/dispatcher-pin-release-order.md)",
			packagePinConsistencyCommandName, config.ref, pinnedTag, expectedTag))
		return 1
	}

	writePackagePinConsistencyLine(stdout, fmt.Sprintf(
		"Package pin records %s, matching the dispatcher version released from %s.", pinnedTag, config.ref))
	return 0
}

func parsePackagePinConsistencyFlags(ctx context.Context, args []string) (packagePinConsistencyConfig, error) {
	flagSet := flag.NewFlagSet(packagePinConsistencyCommandName, flag.ContinueOnError)
	repoRoot := flagSet.String("repo-root", "", "repository root to inspect (default: the current git repository root)")
	ref := flagSet.String("ref", "HEAD", "git ref whose release manifest and package pin are compared")
	err := flagSet.Parse(args)
	if err != nil {
		return packagePinConsistencyConfig{}, err
	}

	resolvedRoot := *repoRoot
	if resolvedRoot == "" {
		resolvedRoot, err = gitRepoRoot(ctx)
		if err != nil {
			return packagePinConsistencyConfig{}, fmt.Errorf("%s: failed to resolve the repository root: %w", packagePinConsistencyCommandName, err)
		}
	}
	if *ref == "" {
		return packagePinConsistencyConfig{}, fmt.Errorf("%s: --ref must not be empty", packagePinConsistencyCommandName)
	}
	return packagePinConsistencyConfig{repoRoot: resolvedRoot, ref: *ref}, nil
}

func packagePinConsistencyDispatcherVersion(ctx context.Context, config packagePinConsistencyConfig) (string, error) {
	content, err := packagePinConsistencyFile(ctx, config, releasePleaseManifestRelativePath)
	if err != nil {
		return "", err
	}

	manifest := map[string]string{}
	err = json.Unmarshal(content, &manifest)
	if err != nil {
		return "", fmt.Errorf("%s: failed to parse %s at %s: %w",
			packagePinConsistencyCommandName, releasePleaseManifestRelativePath, config.ref, err)
	}

	version := manifest[releasePleaseDispatcherPackage]
	if strings.TrimSpace(version) == "" {
		return "", fmt.Errorf("%s: %s at %s has no %q version",
			packagePinConsistencyCommandName, releasePleaseManifestRelativePath, config.ref, releasePleaseDispatcherPackage)
	}
	if version != strings.TrimSpace(version) {
		return "", fmt.Errorf("%s: %s at %s releases %q as %q, which is not a bare version",
			packagePinConsistencyCommandName, releasePleaseManifestRelativePath, config.ref, releasePleaseDispatcherPackage, version)
	}
	return version, nil
}

func packagePinConsistencyPinnedTag(ctx context.Context, config packagePinConsistencyConfig) (string, error) {
	content, err := packagePinConsistencyFile(ctx, config, unityPackageCliPinFile)
	if err != nil {
		return "", err
	}

	pin := packagePinConsistencyPin{}
	err = json.Unmarshal(content, &pin)
	if err != nil {
		return "", fmt.Errorf("%s: failed to parse %s at %s: %w",
			packagePinConsistencyCommandName, unityPackageCliPinFile, config.ref, err)
	}

	pinnedTag := pin.DispatcherReleaseTag
	if strings.TrimSpace(pinnedTag) == "" {
		return "", fmt.Errorf("%s: %s at %s has no dispatcherReleaseTag",
			packagePinConsistencyCommandName, unityPackageCliPinFile, config.ref)
	}
	// Trimming instead of rejecting would let a padded tag pass this gate while
	// the merge automation, which compares the pin verbatim against the tag it
	// just published, waits out its whole timeout on the same release.
	if pinnedTag != strings.TrimSpace(pinnedTag) {
		return "", fmt.Errorf("%s: %s at %s records dispatcherReleaseTag %q, which is not a bare tag",
			packagePinConsistencyCommandName, unityPackageCliPinFile, config.ref, pinnedTag)
	}
	return pinnedTag, nil
}

// packagePinConsistencyFile reads a required input at the ref. An absent file
// is an error rather than a skip: the check exists to block a release, so an
// input it cannot read must never read as "consistent".
func packagePinConsistencyFile(ctx context.Context, config packagePinConsistencyConfig, path string) ([]byte, error) {
	content, found, err := gitFileAtRef(ctx, config.repoRoot, config.ref, path)
	if err != nil {
		return nil, fmt.Errorf("%s: %w", packagePinConsistencyCommandName, err)
	}
	if !found {
		return nil, fmt.Errorf("%s: %s is missing at %s", packagePinConsistencyCommandName, path, config.ref)
	}
	return content, nil
}

func writePackagePinConsistencyLine(writer io.Writer, values ...any) {
	// CI status output failures cannot be recovered after the command outcome is known.
	_, _ = fmt.Fprintln(writer, values...)
}
