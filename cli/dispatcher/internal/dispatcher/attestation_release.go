package dispatcher

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"strings"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/attestation"
	sharedupdate "github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
)

const (
	dispatcherReleasesAPIPathTemplate = "/repos/%s/releases?per_page=100&page=%d"
	dispatcherReleaseAPIMaxPages      = 10
	dispatcherReleaseTagPrefix        = "dispatcher-v"
	betaVersionMarker                 = "-beta."
	updateArchiveManifestEnvName      = "ULOOP_ARCHIVE_MANIFEST"
)

// dispatcherAPIBaseURL is mutable so tests can point it at an httptest server.
// Production callers must not touch it.
var dispatcherAPIBaseURL = "https://api.github.com"

func dispatcherAPIBase() string { return dispatcherAPIBaseURL }

// fetchAttestationSubjectManifestFunc is the indirection tests use to stub the
// full network + Sigstore verify pipeline. Production callers must not reassign
// this outside the dispatcher package.
var fetchAttestationSubjectManifestFunc = fetchAttestationSubjectManifest

// resolveUpdateTargetVersionFunc is the indirection tests use to stub the
// GitHub /releases enumeration + channel-filter step. Production callers must
// not reassign this outside the dispatcher package.
var resolveUpdateTargetVersionFunc = resolveUpdateTargetVersion

type githubReleaseListEntry struct {
	TagName    string `json:"tag_name"`
	Draft      bool   `json:"draft"`
	Prerelease bool   `json:"prerelease"`
}

// resolveDispatcherLatestReleaseTag returns the release tag the update flow
// should target when the caller asks for latest / latest-beta. wantBeta=true
// picks the newest prerelease dispatcher tag containing "-beta."; wantBeta=false
// picks the newest non-prerelease dispatcher tag. The filter mirrors
// scripts/install.sh's release-selection logic so a dispatcher-side resolve and
// a script-side resolve always converge on the same tag.
//
// This resolve must happen dispatcher-side (not inside install.sh) so the
// attestation verifier can bind manifests + installer verification to a
// concrete tag rather than the /releases/latest endpoint — which in this repo
// silently returns the wrong (mixed package/dispatcher/runner) release.
func resolveDispatcherLatestReleaseTag(ctx context.Context, wantBeta bool) (string, error) {
	for page := 1; page <= dispatcherReleaseAPIMaxPages; page++ {
		entries, err := fetchDispatcherReleasePage(ctx, page)
		if err != nil {
			return "", err
		}
		if len(entries) == 0 {
			break
		}
		for _, entry := range entries {
			if entry.Draft {
				continue
			}
			if !strings.HasPrefix(entry.TagName, dispatcherReleaseTagPrefix) {
				continue
			}
			if wantBeta {
				if !entry.Prerelease || !strings.Contains(strings.ToLower(entry.TagName), betaVersionMarker) {
					continue
				}
			} else {
				if entry.Prerelease {
					continue
				}
			}
			return entry.TagName, nil
		}
		if len(entries) < 100 {
			break
		}
	}
	channel := "stable"
	if wantBeta {
		channel = "beta"
	}
	return "", fmt.Errorf("no %s dispatcher release available", channel)
}

func fetchDispatcherReleasePage(ctx context.Context, page int) ([]githubReleaseListEntry, error) {
	url := dispatcherAPIBase() + fmt.Sprintf(dispatcherReleasesAPIPathTemplate, dispatcherReleaseRepository, page)
	request, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return nil, err
	}
	request.Header.Set("Accept", "application/vnd.github+json")
	request.Header.Set("X-GitHub-Api-Version", "2022-11-28")
	if token := lookupGitHubAPIToken(); token != "" {
		request.Header.Set("Authorization", "Bearer "+token)
	}
	response, err := dispatcherHTTPClient.Do(request)
	if err != nil {
		return nil, err
	}
	defer func() {
		_, _ = io.Copy(io.Discard, response.Body)
		_ = response.Body.Close()
	}()
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return nil, fmt.Errorf("list releases: status %s", response.Status)
	}
	var entries []githubReleaseListEntry
	if err := json.NewDecoder(response.Body).Decode(&entries); err != nil {
		return nil, fmt.Errorf("decode releases: %v", err)
	}
	return entries, nil
}

// resolveUpdateTargetVersion promotes an implicit "latest" self-update request
// into an explicit target version. If TargetVersion is already set, it is
// returned untouched so tryHandleUpdateRequest's --to-version path still works.
// Otherwise the newest dispatcher release matching the caller's channel
// (stable vs beta, chosen by CurrentVersion) is resolved to a concrete
// version string like "3.0.1-beta.12".
func resolveUpdateTargetVersion(ctx context.Context, options sharedupdate.Options) (sharedupdate.Options, error) {
	if options.TargetVersion != "" {
		return options, nil
	}
	wantBeta := sharedupdate.IsBetaVersion(options.CurrentVersion)
	tag, err := resolveDispatcherLatestReleaseTag(ctx, wantBeta)
	if err != nil {
		return options, err
	}
	options.TargetVersion = strings.TrimPrefix(tag, dispatcherReleaseTagPrefix)
	return options, nil
}

// fetchAttestationSubjectManifest fetches and verifies the release's
// attestation bundle and returns a sha256sum-style manifest string mapping
// each release asset filename to its verified SHA-256 digest.
//
// The returned string has one line per subject in the format
// "<digest>  <filename>\n" so install.sh / install.ps1 can look up the asset
// name they downloaded and enforce the digest without inheriting any Go-side
// GOOS/GOARCH prediction that could diverge from the shell's uname-based
// detection (e.g. Rosetta running an amd64 dispatcher inside an arm64 host).
func fetchAttestationSubjectManifest(ctx context.Context, releaseTag string) (string, error) {
	if releaseTag == "" {
		return "", fmt.Errorf("%w: releaseTag required to fetch subject manifest", attestation.ErrVerificationFailed)
	}
	assetURL := dispatcherReleaseBaseURL + "/" + releaseTag + "/" + sharedupdate.PosixScriptName
	bundleData, err := attestation.FetchBundle(ctx, assetURL+".sigstore.json")
	if err != nil {
		return "", err
	}
	commitSHA, err := attestation.FetchTagCommitSHA(ctx, dispatcherReleaseRepository, releaseTag)
	if err != nil {
		return "", err
	}
	trustedMaterial, err := attestation.LoadEmbeddedTrustedMaterial()
	if err != nil {
		return "", fmt.Errorf("%w: load embedded trusted root: %v", attestation.ErrVerificationFailed, err)
	}
	subjects, err := attestation.VerifySubjects(trustedMaterial, attestation.SubjectsOptions{
		BundleData:        bundleData,
		ExpectedCommitSHA: commitSHA,
		Identity: attestation.Identity{
			Repository:   dispatcherReleaseRepository,
			WorkflowPath: attestationDispatcherPublishWorkflowPath,
			Refs:         attestationAllowedRefs,
		},
	})
	if err != nil {
		return "", err
	}
	var builder strings.Builder
	for name, digest := range subjects {
		if name == "" || digest == "" {
			continue
		}
		if strings.ContainsAny(name, "\n\r") {
			continue
		}
		builder.WriteString(digest)
		builder.WriteString("  ")
		builder.WriteString(name)
		builder.WriteByte('\n')
	}
	if builder.Len() == 0 {
		return "", fmt.Errorf("%w: subject manifest was empty after filtering", attestation.ErrVerificationFailed)
	}
	return builder.String(), nil
}

// lookupGitHubAPIToken reads the ambient GitHub token from the environment.
// GITHUB_TOKEN wins over GH_TOKEN so CI environments that set both behave
// consistently with attestation.setAuthorizationIfAvailable in fetcher.go.
func lookupGitHubAPIToken() string {
	for _, env := range []string{"GITHUB_TOKEN", "GH_TOKEN"} {
		if value := os.Getenv(env); value != "" {
			return value
		}
	}
	return ""
}
