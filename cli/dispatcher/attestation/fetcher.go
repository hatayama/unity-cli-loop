package attestation

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"path"
	"time"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/githubapi"
)

// DefaultHTTPClient is the http.Client used for bundle and tag-ref fetches.
// It is a var so tests can substitute a client wired to httptest servers.
var DefaultHTTPClient = &http.Client{Timeout: 30 * time.Second}

// githubAPIBaseURL is mutable so tests can point it at an httptest server;
// production callers never touch it directly.
var githubAPIBaseURL = "https://api.github.com"

func githubAPIBase() string       { return githubAPIBaseURL }
func setGithubAPIBase(url string) { githubAPIBaseURL = url }

const (
	githubReleaseBaseURL   = "https://github.com/%s/releases/download/%s/%s"
	envAuthTokenPrimary    = "GITHUB_TOKEN"
	envAuthTokenSecondary  = "GH_TOKEN"
	acceptHeaderGitHubJSON = "application/vnd.github+json"
	apiVersionHeaderValue  = "2022-11-28"
)

// BundleAssetURL builds the release download URL for <asset>.sigstore.json.
// Callers pass the base asset name (without the .sigstore.json suffix).
func BundleAssetURL(repo, tag, assetName string) string {
	return fmt.Sprintf(githubReleaseBaseURL, repo, tag, url.PathEscape(assetName+".sigstore.json"))
}

// FetchBundle downloads the <asset>.sigstore.json body from the release. It
// wraps every failure in ErrBundleFetch so the caller fails closed. 403/429
// are treated the same way as any other non-2xx: verification is not attempted
// because we cannot distinguish rate-limit denial from an attacker denying the
// bundle to skip verification.
//
// No Authorization header is set here: release download URLs
// (github.com/.../releases/download) are not subject to the API rate limits
// that GITHUB_TOKEN would relax, and Go's http.Client strips Authorization
// across the cross-host redirect to objects.githubusercontent.com anyway, so
// leaking the token would be pointless. Least-privilege: keep the token on
// api.github.com calls only (see fetchGitRef).
func FetchBundle(ctx context.Context, bundleURL string) ([]byte, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, bundleURL, nil)
	if err != nil {
		return nil, fmt.Errorf("%w: build request: %v", ErrBundleFetch, err)
	}
	resp, err := DefaultHTTPClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("%w: %v", ErrBundleFetch, err)
	}
	defer func() {
		_ = resp.Body.Close()
	}()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return nil, fmt.Errorf("%w: status %s from %s", ErrBundleFetch, resp.Status, bundleURL)
	}
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, fmt.Errorf("%w: read body: %v", ErrBundleFetch, err)
	}
	if len(body) == 0 {
		return nil, fmt.Errorf("%w: empty body from %s", ErrBundleFetch, bundleURL)
	}
	return body, nil
}

// TagRefResponse mirrors the git ref API payload shape for the fields we
// consume.
type TagRefResponse struct {
	Object struct {
		SHA  string `json:"sha"`
		Type string `json:"type"`
	} `json:"object"`
}

// FetchTagCommitSHA resolves a release tag to the git commit SHA it points at
// via the GitHub git-refs REST API. The verifier binds this SHA to the cert's
// Source Repository Digest extension so a stolen OIDC token cannot be reused
// on a tag it did not produce. We follow one level of "tag" indirection so
// annotated tags resolve to the same commit SHA as lightweight ones.
func FetchTagCommitSHA(ctx context.Context, repo, tag string) (string, error) {
	initialURL := fmt.Sprintf("%s/repos/%s/git/ref/tags/%s", githubAPIBase(), repo, url.PathEscape(tag))
	sha, objectType, err := fetchGitRef(ctx, initialURL)
	if err != nil {
		return "", err
	}
	if objectType == "tag" {
		tagURL := fmt.Sprintf("%s/repos/%s/git/tags/%s", githubAPIBase(), repo, url.PathEscape(sha))
		sha, objectType, err = fetchGitRef(ctx, tagURL)
		if err != nil {
			return "", err
		}
	}
	if objectType != "commit" {
		return "", fmt.Errorf("%w: unexpected object type %q for tag %s", ErrTagRefFetch, objectType, tag)
	}
	if !isHexCommitSHA(sha) {
		return "", fmt.Errorf("%w: bad commit SHA %q for tag %s", ErrTagRefFetch, sha, tag)
	}
	return sha, nil
}

func fetchGitRef(ctx context.Context, apiURL string) (string, string, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, apiURL, nil)
	if err != nil {
		return "", "", fmt.Errorf("%w: build request: %v", ErrTagRefFetch, err)
	}
	req.Header.Set("Accept", acceptHeaderGitHubJSON)
	req.Header.Set("X-GitHub-Api-Version", apiVersionHeaderValue)
	authenticated := setAuthorizationIfAvailable(req)
	resp, err := DefaultHTTPClient.Do(req)
	if err != nil {
		return "", "", fmt.Errorf("%w: %v", ErrTagRefFetch, err)
	}
	defer func() {
		_ = resp.Body.Close()
	}()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return "", "", tagRefStatusError(resp, apiURL, authenticated)
	}
	var payload TagRefResponse
	if err := json.NewDecoder(resp.Body).Decode(&payload); err != nil {
		return "", "", fmt.Errorf("%w: decode payload: %v", ErrTagRefFetch, err)
	}
	return payload.Object.SHA, payload.Object.Type, nil
}

// tagRefStatusError keeps the rate-limit case recoverable through errors.As
// so the dispatcher can tell the user about GH_TOKEN instead of a bare 403.
func tagRefStatusError(resp *http.Response, apiURL string, authenticated bool) error {
	if rateLimit, ok := githubapi.DetectRateLimit(resp, authenticated); ok {
		return fmt.Errorf("%w: %w (from %s)", ErrTagRefFetch, rateLimit, apiURL)
	}
	return fmt.Errorf("%w: status %s from %s", ErrTagRefFetch, resp.Status, apiURL)
}

// setAuthorizationIfAvailable reports whether a token was attached, so a
// later rate-limit refusal can skip the "set a token" guidance.
func setAuthorizationIfAvailable(req *http.Request) bool {
	token := os.Getenv(envAuthTokenPrimary)
	if token == "" {
		token = os.Getenv(envAuthTokenSecondary)
	}
	if token == "" {
		return false
	}
	req.Header.Set("Authorization", "Bearer "+token)
	return true
}

// AssetNameFromReleaseAsset strips any query fragments and returns the base
// asset name, so callers can pass a full URL and receive the file name.
func AssetNameFromReleaseAsset(assetURL string) string {
	parsed, err := url.Parse(assetURL)
	if err != nil {
		return path.Base(assetURL)
	}
	return path.Base(parsed.Path)
}
