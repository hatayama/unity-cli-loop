package automation

import (
	"context"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"strings"
)

const (
	unityPackageManifestKey = "Packages/src"
	unityPackageName        = "io.github.hatayama.uloopmcp"
)

// unityPackageReleaseTagPattern accepts only the Unity package component's tags. The CLI
// components share the repository and its releases, so their tags must never select the package
// source for signing.
var unityPackageReleaseTagPattern = regexp.MustCompile(`^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$`)

// releaseCommitPattern matches a full commit SHA. The package release is signed from, and tagged
// at, its release commit, so anything that could resolve to another commit later (a branch, an
// abbreviation) is refused.
var releaseCommitPattern = regexp.MustCompile(`^[0-9a-f]{40}$`)

type unityPackageRelease struct {
	TagName string                     `json:"tag_name"`
	Draft   bool                       `json:"draft"`
	Assets  []unityPackageReleaseAsset `json:"assets"`
}

type unityPackageReleaseAsset struct {
	Name string `json:"name"`
	Size int64  `json:"size"`
}

// lookupPublishedUnityPackageRelease returns the published release of the tag, which is the one
// OpenUPM reads. GitHub answers this lookup with 404 for a missing tag and for a draft alike. Any
// other failure is returned, because treating an outage as "no release yet" would create a second
// release or leave the first unsigned.
func lookupPublishedUnityPackageRelease(ctx context.Context, runOutput func(context.Context, string, ...string) (string, error), repository string, tag string) (unityPackageRelease, bool, error) {
	output, err := runOutput(ctx, "gh", "api", "repos/"+repository+"/releases/tags/"+tag)
	if err != nil {
		if strings.Contains(err.Error(), "HTTP 404") {
			return unityPackageRelease{}, false, nil
		}
		return unityPackageRelease{}, false, err
	}
	var release unityPackageRelease
	if err := json.Unmarshal([]byte(output), &release); err != nil {
		return unityPackageRelease{}, false, fmt.Errorf("parse release %s: %w", tag, err)
	}
	if release.TagName != tag || release.Draft {
		return unityPackageRelease{}, false, fmt.Errorf("release lookup for %s answered with tag %q (draft=%t), not the published release", tag, release.TagName, release.Draft)
	}
	return release, true, nil
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
