package automation

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strings"
)

const (
	defaultUnityReleaseAPIBaseURL    = "https://services.api.unity.com"
	unityEditorReleasesPath          = "/unity/editor/release/v1/releases"
	unityReleaseQueryVersion         = "version"
	unityReleaseQueryOrder           = "order"
	unityReleaseQueryLimit           = "limit"
	unityReleaseOrderReleaseDateDesc = "RELEASE_DATE_DESC"
	unityReleaseLimitLatest          = "1"
	downloadPlatformLinux            = "LINUX"
	downloadArchitectureX86_64       = "X86_64"
	downloadTypeTarXz                = "TAR_XZ"
	githubOutputVersion              = "version"
	githubOutputChangeset            = "changeset"
	githubOutputEditorURL            = "editorUrl"
)

// UnityRelease is the newest editor in a version series plus its Linux download URL.
type UnityRelease struct {
	Version   string
	Changeset string
	EditorURL string
}

// ResolveUnityReleaseRequest is the caller-supplied series and optional HTTP seam for tests.
type ResolveUnityReleaseRequest struct {
	Series     string
	APIBaseURL string
	HTTPClient *http.Client
}

type unityReleaseList struct {
	Results []unityReleaseResult `json:"results"`
}

type unityReleaseResult struct {
	Version       string                 `json:"version"`
	ShortRevision string                 `json:"shortRevision"`
	Downloads     []unityReleaseDownload `json:"downloads"`
}

type unityReleaseDownload struct {
	Platform     string `json:"platform"`
	Architecture string `json:"architecture"`
	Type         string `json:"type"`
	URL          string `json:"url"`
}

// ResolveUnityRelease fetches the newest editor release for series in one query.
func ResolveUnityRelease(ctx context.Context, request ResolveUnityReleaseRequest) (UnityRelease, error) {
	if request.Series == "" {
		return UnityRelease{}, fmt.Errorf("--series is required")
	}

	client := request.HTTPClient
	if client == nil {
		client = http.DefaultClient
	}

	apiBaseURL := request.APIBaseURL
	if apiBaseURL == "" {
		apiBaseURL = defaultUnityReleaseAPIBaseURL
	}

	requestURL, err := unityReleasesRequestURL(apiBaseURL, request.Series)
	if err != nil {
		return UnityRelease{}, err
	}

	httpRequest, err := http.NewRequestWithContext(ctx, http.MethodGet, requestURL, nil)
	if err != nil {
		return UnityRelease{}, fmt.Errorf("build unity release request: %w", err)
	}

	response, err := client.Do(httpRequest)
	if err != nil {
		return UnityRelease{}, fmt.Errorf("unity release API request failed: %w", err)
	}
	defer func() { _ = response.Body.Close() }()

	if response.StatusCode != http.StatusOK {
		return UnityRelease{}, fmt.Errorf("unity release API returned HTTP %d", response.StatusCode)
	}

	body, err := io.ReadAll(response.Body)
	if err != nil {
		return UnityRelease{}, fmt.Errorf("read unity release API response: %w", err)
	}

	return selectUnityRelease(body)
}

// WriteGitHubOutput writes version, changeset, and editorUrl as GITHUB_OUTPUT assignments.
func WriteGitHubOutput(writer io.Writer, release UnityRelease) error {
	_, err := fmt.Fprintf(
		writer,
		"%s=%s\n%s=%s\n%s=%s\n",
		githubOutputVersion,
		release.Version,
		githubOutputChangeset,
		release.Changeset,
		githubOutputEditorURL,
		release.EditorURL,
	)
	return err
}

// RunResolveUnityRelease parses CLI args, resolves the series, and prints GITHUB_OUTPUT lines.
func RunResolveUnityRelease(stdout io.Writer, stderr io.Writer, args []string) int {
	flags := flag.NewFlagSet("resolve-unity-release", flag.ContinueOnError)
	flags.SetOutput(stderr)
	series := flags.String("series", "", "Unity editor version series to resolve, for example 6000.7")
	parseErr := flags.Parse(args)
	if parseErr != nil {
		return 1
	}
	if *series == "" {
		_, _ = fmt.Fprintln(stderr, "resolve-unity-release: --series is required")
		return 1
	}

	release, err := ResolveUnityRelease(context.Background(), ResolveUnityReleaseRequest{Series: *series})
	if err != nil {
		_, _ = fmt.Fprintln(stderr, "resolve-unity-release:", err)
		return 1
	}

	writeErr := WriteGitHubOutput(stdout, release)
	if writeErr != nil {
		_, _ = fmt.Fprintln(stderr, "resolve-unity-release:", writeErr)
		return 1
	}
	return 0
}

func selectUnityRelease(body []byte) (UnityRelease, error) {
	var payload unityReleaseList
	if err := json.Unmarshal(body, &payload); err != nil {
		return UnityRelease{}, fmt.Errorf("parse unity release response: %w", err)
	}
	if len(payload.Results) == 0 {
		return UnityRelease{}, fmt.Errorf("no unity editor releases matched the requested series")
	}
	return releaseFromAPIResult(payload.Results[0])
}

func releaseFromAPIResult(result unityReleaseResult) (UnityRelease, error) {
	if result.Version == "" || result.ShortRevision == "" {
		return UnityRelease{}, fmt.Errorf("unity release is missing version or shortRevision")
	}
	editorURL, err := linuxEditorDownloadURL(result.Downloads)
	if err != nil {
		return UnityRelease{}, err
	}
	return UnityRelease{
		Version:   result.Version,
		Changeset: result.ShortRevision,
		EditorURL: editorURL,
	}, nil
}

func linuxEditorDownloadURL(downloads []unityReleaseDownload) (string, error) {
	for _, download := range downloads {
		if !isLinuxEditorArchive(download) {
			continue
		}
		if download.URL == "" {
			return "", fmt.Errorf("unity release LINUX X86_64 TAR_XZ download is missing url")
		}
		return download.URL, nil
	}
	return "", fmt.Errorf("unity release has no LINUX X86_64 TAR_XZ editor download")
}

func isLinuxEditorArchive(download unityReleaseDownload) bool {
	return download.Platform == downloadPlatformLinux &&
		download.Architecture == downloadArchitectureX86_64 &&
		download.Type == downloadTypeTarXz
}

func unityReleasesRequestURL(apiBaseURL string, series string) (string, error) {
	parsed, err := url.Parse(apiBaseURL)
	if err != nil {
		return "", fmt.Errorf("parse unity release API base URL: %w", err)
	}
	parsed = parsed.JoinPath(strings.TrimPrefix(unityEditorReleasesPath, "/"))
	query := parsed.Query()
	query.Set(unityReleaseQueryVersion, series)
	query.Set(unityReleaseQueryOrder, unityReleaseOrderReleaseDateDesc)
	query.Set(unityReleaseQueryLimit, unityReleaseLimitLatest)
	parsed.RawQuery = query.Encode()
	return parsed.String(), nil
}
