package automation

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"time"

	sharedversion "github.com/hatayama/unity-cli-loop/common/version"
	"github.com/hatayama/unity-cli-loop/dispatcher/attestation"
)

const (
	dispatcherPinFreshnessCommandName = "check-dispatcher-pin-freshness"
	dispatcherPinFreshnessPageSize    = 100
	// A listing this long can only mean the API is paging in a loop; failing is
	// safer than reporting a "newest" release derived from a truncated listing.
	dispatcherPinFreshnessMaxPages       = 50
	dispatcherPinFreshnessRequestTimeout = 30 * time.Second
)

// dispatcherRelease describes the subset of a GitHub Release needed to decide
// whether it is a published stable dispatcher release.
type dispatcherRelease struct {
	TagName    string `json:"tag_name"`
	Draft      bool   `json:"draft"`
	Prerelease bool   `json:"prerelease"`
}

type dispatcherPinFreshnessConfig struct {
	repository string
	pinPath    string
}

type dispatcherPinFreshnessDeps struct {
	fetchReleases func(ctx context.Context, repository string) ([]dispatcherRelease, error)
	readPin       func(path string) ([]byte, error)
}

// RunDispatcherPinFreshnessCheck fails when the package pin still records an
// older dispatcher release than the newest published stable one, because fresh
// installs follow the pin and would keep landing on the superseded release.
func RunDispatcherPinFreshnessCheck(ctx context.Context, stdout io.Writer, stderr io.Writer, args []string) int {
	config, err := parseDispatcherPinFreshnessFlags(args)
	if err != nil {
		writeDispatcherPinFreshnessLine(stderr, dispatcherPinFreshnessCommandName+":", err)
		return 1
	}
	return runDispatcherPinFreshnessCheck(ctx, stdout, stderr, config, defaultDispatcherPinFreshnessDeps())
}

func defaultDispatcherPinFreshnessDeps() dispatcherPinFreshnessDeps {
	return dispatcherPinFreshnessDeps{
		fetchReleases: fetchDispatcherReleases,
		readPin:       os.ReadFile,
	}
}

func parseDispatcherPinFreshnessFlags(args []string) (dispatcherPinFreshnessConfig, error) {
	flagSet := flag.NewFlagSet(dispatcherPinFreshnessCommandName, flag.ContinueOnError)
	repository := flagSet.String("repo", "", "GitHub repository that publishes dispatcher releases")
	err := flagSet.Parse(args)
	if err != nil {
		return dispatcherPinFreshnessConfig{}, err
	}
	return dispatcherPinFreshnessConfig{
		repository: resolveDispatcherPinFreshnessRepository(*repository),
		// The command runs from cli/release-automation, matching check-dispatcher-pin.
		pinPath: filepath.Join("..", "..", filepath.FromSlash(unityPackageCliPinFile)),
	}, nil
}

// resolveDispatcherPinFreshnessRepository prefers the explicit flag, then the
// workflow's repository, and finally the repository that owns the releases the
// pin is stamped from.
func resolveDispatcherPinFreshnessRepository(flagValue string) string {
	if flagValue != "" {
		return flagValue
	}
	if environmentValue := os.Getenv("GITHUB_REPOSITORY"); environmentValue != "" {
		return environmentValue
	}
	return attestation.ReleaseRepository
}

func runDispatcherPinFreshnessCheck(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config dispatcherPinFreshnessConfig,
	deps dispatcherPinFreshnessDeps,
) int {
	pinnedTag, pinnedVersion, err := readPinnedDispatcherRelease(config, deps)
	if err != nil {
		writeDispatcherPinFreshnessLine(stderr, dispatcherPinFreshnessCommandName+":", err)
		return 1
	}
	releases, err := deps.fetchReleases(ctx, config.repository)
	if err != nil {
		writeDispatcherPinFreshnessLine(stderr, dispatcherPinFreshnessCommandName+":", err)
		return 1
	}
	newestTag, newestVersion := newestStableDispatcherRelease(releases)
	if newestVersion == "" {
		writeDispatcherPinFreshnessLine(stdout, fmt.Sprintf(
			"No stable dispatcher release is published yet; pin %s is accepted.", pinnedTag))
		return 0
	}
	if sharedversion.IsLessThan(pinnedVersion, newestVersion) {
		writeDispatcherPinFreshnessLine(stderr, fmt.Sprintf(
			"%s: pin records %s but the newest stable dispatcher release is %s. "+
				"Merge the automated pin-stamp pull request, or run stamp-dispatcher-pin --tag %s.",
			dispatcherPinFreshnessCommandName, pinnedTag, newestTag, newestTag))
		return 1
	}
	writeDispatcherPinFreshnessLine(stdout, fmt.Sprintf(
		"Dispatcher pin freshness guard passed: pin records %s and the newest stable dispatcher release is %s.",
		pinnedTag, newestTag))
	return 0
}

func readPinnedDispatcherRelease(
	config dispatcherPinFreshnessConfig,
	deps dispatcherPinFreshnessDeps,
) (string, string, error) {
	content, err := deps.readPin(config.pinPath)
	if err != nil {
		return "", "", fmt.Errorf("read dispatcher pin %s: %w", unityPackageCliPinFile, err)
	}
	values := dispatcherPinGuardValues{}
	if err := json.Unmarshal(content, &values); err != nil {
		return "", "", fmt.Errorf("%s is invalid JSON: %w", unityPackageCliPinFile, err)
	}
	version, err := dispatcherVersionFromReleaseTag(values.DispatcherReleaseTag)
	if err != nil {
		return "", "", fmt.Errorf("%s dispatcherReleaseTag is unusable: %w", unityPackageCliPinFile, err)
	}
	return values.DispatcherReleaseTag, version, nil
}

// newestStableDispatcherRelease returns the highest stable dispatcher release.
// Tags that are not dispatcher releases belong to the other components released
// from this repository, so they are skipped rather than treated as an error.
func newestStableDispatcherRelease(releases []dispatcherRelease) (string, string) {
	newestTag := ""
	newestVersion := ""
	for _, release := range releases {
		if release.Draft || release.Prerelease {
			continue
		}
		version, err := dispatcherVersionFromReleaseTag(release.TagName)
		if err != nil {
			continue
		}
		// A pre-release identifier can appear on a release GitHub does not flag
		// as a pre-release, and the pin must never move to one.
		if strings.Contains(version, "-") {
			continue
		}
		if newestVersion != "" && !sharedversion.IsLessThan(newestVersion, version) {
			continue
		}
		newestTag = release.TagName
		newestVersion = version
	}
	return newestTag, newestVersion
}

func fetchDispatcherReleases(ctx context.Context, repository string) ([]dispatcherRelease, error) {
	client := &http.Client{Timeout: dispatcherPinFreshnessRequestTimeout}
	releases := []dispatcherRelease{}
	for page := 1; page <= dispatcherPinFreshnessMaxPages; page++ {
		pageReleases, err := fetchDispatcherReleasePage(ctx, client, repository, page)
		if err != nil {
			return nil, err
		}
		releases = append(releases, pageReleases...)
		if len(pageReleases) < dispatcherPinFreshnessPageSize {
			return releases, nil
		}
	}
	return nil, fmt.Errorf("release listing for %s exceeded %d pages", repository, dispatcherPinFreshnessMaxPages)
}

func fetchDispatcherReleasePage(
	ctx context.Context,
	client *http.Client,
	repository string,
	page int,
) ([]dispatcherRelease, error) {
	requestURL := fmt.Sprintf(
		"%s/repos/%s/releases?per_page=%d&page=%d",
		dispatcherPinStampAPIBaseURL,
		repository,
		dispatcherPinFreshnessPageSize,
		page)
	request, err := http.NewRequestWithContext(ctx, http.MethodGet, requestURL, nil)
	if err != nil {
		return nil, fmt.Errorf("build GitHub release list request: %w", err)
	}
	request.Header.Set("Accept", "application/vnd.github+json")
	request.Header.Set("X-GitHub-Api-Version", "2022-11-28")
	if token := dispatcherPinStampGitHubToken(); token != "" {
		request.Header.Set("Authorization", "Bearer "+token)
	}
	response, err := client.Do(request)
	if err != nil {
		return nil, err
	}
	defer func() {
		_ = response.Body.Close()
	}()
	if response.StatusCode < http.StatusOK || response.StatusCode >= http.StatusMultipleChoices {
		return nil, fmt.Errorf("GitHub release list API returned %s", response.Status)
	}
	pageReleases := []dispatcherRelease{}
	if err := json.NewDecoder(response.Body).Decode(&pageReleases); err != nil {
		return nil, fmt.Errorf("decode GitHub release list: %w", err)
	}
	return pageReleases, nil
}

func writeDispatcherPinFreshnessLine(writer io.Writer, values ...any) {
	// CI status output failures cannot be recovered after the command outcome is known.
	_, _ = fmt.Fprintln(writer, values...)
}
