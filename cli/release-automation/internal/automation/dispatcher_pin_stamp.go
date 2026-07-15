package automation

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"os"
	"sort"
	"strings"
	"time"

	"github.com/hatayama/unity-cli-loop/dispatcher/attestation"
)

const (
	dispatcherPinStampInstallerScript  = "install.sh"
	dispatcherPinStampPowerShellScript = "install.ps1"
	dispatcherPinStampAPIBaseURL       = "https://api.github.com"
)

// dispatcherReleaseAsset describes the subset of a GitHub Release asset used
// to build the provenance-pinned dispatcher manifest.
type dispatcherReleaseAsset struct {
	Name string `json:"name"`
	URL  string `json:"browser_download_url"`
}

type dispatcherPinStampDeps struct {
	fetchReleaseAssets func(context.Context, string) ([]dispatcherReleaseAsset, error)
	fetchBundle        func(context.Context, string) ([]byte, error)
	fetchTagCommitSHA  func(context.Context, string, string) (string, error)
	verifySubjects     func([]byte, string) (map[string]string, error)
}

// StampDispatcherPin verifies a dispatcher release's attestation and writes
// its exact subject manifest into the package pin.
func StampDispatcherPin(ctx context.Context, pinPath string, releaseTag string) error {
	return stampDispatcherPin(ctx, pinPath, releaseTag, defaultDispatcherPinStampDeps())
}

func defaultDispatcherPinStampDeps() dispatcherPinStampDeps {
	return dispatcherPinStampDeps{
		fetchReleaseAssets: fetchDispatcherReleaseAssets,
		fetchBundle:        attestation.FetchBundle,
		fetchTagCommitSHA:  attestation.FetchTagCommitSHA,
		verifySubjects: func(bundleData []byte, commitSHA string) (map[string]string, error) {
			trustedMaterial, err := attestation.LoadEmbeddedTrustedMaterial()
			if err != nil {
				return nil, fmt.Errorf("load dispatcher attestation trusted root: %w", err)
			}
			return attestation.VerifySubjects(trustedMaterial, attestation.SubjectsOptions{
				BundleData:        bundleData,
				ExpectedCommitSHA: commitSHA,
				Identity:          attestation.DispatcherPublishIdentity(),
			})
		},
	}
}

func stampDispatcherPin(
	ctx context.Context,
	pinPath string,
	releaseTag string,
	deps dispatcherPinStampDeps,
) error {
	if releaseTag == "" {
		return fmt.Errorf("dispatcher release tag is required")
	}
	assets, err := deps.fetchReleaseAssets(ctx, releaseTag)
	if err != nil {
		return fmt.Errorf("fetch dispatcher release assets: %w", err)
	}
	installScriptURL, err := findDispatcherReleaseAssetURL(assets, dispatcherPinStampInstallerScript)
	if err != nil {
		return err
	}
	if _, err := findDispatcherReleaseAssetURL(assets, dispatcherPinStampPowerShellScript); err != nil {
		return err
	}
	bundleData, err := deps.fetchBundle(ctx, installScriptURL+".sigstore.json")
	if err != nil {
		return fmt.Errorf("fetch dispatcher installer attestation bundle: %w", err)
	}
	commitSHA, err := deps.fetchTagCommitSHA(ctx, attestation.ReleaseRepository, releaseTag)
	if err != nil {
		return fmt.Errorf("resolve dispatcher release tag commit: %w", err)
	}
	subjects, err := deps.verifySubjects(bundleData, commitSHA)
	if err != nil {
		return fmt.Errorf("verify dispatcher release attestation: %w", err)
	}
	manifest, err := buildDispatcherArchiveManifest(assets, subjects)
	if err != nil {
		return err
	}
	return writeDispatcherPinStamp(pinPath, releaseTag, manifest)
}

func findDispatcherReleaseAssetURL(assets []dispatcherReleaseAsset, name string) (string, error) {
	for _, asset := range assets {
		if asset.Name != name {
			continue
		}
		if asset.URL == "" {
			return "", fmt.Errorf("dispatcher release asset %q has no download URL", name)
		}
		return asset.URL, nil
	}
	return "", fmt.Errorf("dispatcher release is missing required asset %q", name)
}

func buildDispatcherArchiveManifest(assets []dispatcherReleaseAsset, subjects map[string]string) (string, error) {
	entries := make([]string, 0, len(assets))
	seenAssets := make(map[string]struct{}, len(assets))
	for _, asset := range assets {
		if strings.HasSuffix(asset.Name, ".sigstore.json") {
			continue
		}
		if asset.Name == "" || strings.ContainsAny(asset.Name, "\r\n") {
			return "", fmt.Errorf("dispatcher release has an invalid asset name %q", asset.Name)
		}
		if _, alreadySeen := seenAssets[asset.Name]; alreadySeen {
			return "", fmt.Errorf("dispatcher release lists duplicate asset %q", asset.Name)
		}
		seenAssets[asset.Name] = struct{}{}
		digest, ok := subjects[asset.Name]
		if !ok {
			return "", fmt.Errorf("dispatcher release asset %q is not attested", asset.Name)
		}
		if !isSHA256Digest(digest) {
			return "", fmt.Errorf("dispatcher release asset %q has invalid attested SHA-256 digest", asset.Name)
		}
		entries = append(entries, digest+"  "+asset.Name)
	}
	if len(entries) == 0 {
		return "", fmt.Errorf("dispatcher release has no attested assets")
	}
	if len(subjects) != len(seenAssets) {
		return "", fmt.Errorf("dispatcher release attestation subjects do not exactly match release assets")
	}
	for subjectName := range subjects {
		if _, exists := seenAssets[subjectName]; !exists {
			return "", fmt.Errorf("dispatcher release attestation has unexpected subject %q", subjectName)
		}
	}
	sort.Strings(entries)
	return strings.Join(entries, "\n"), nil
}

func isSHA256Digest(digest string) bool {
	if len(digest) != 64 {
		return false
	}
	for _, character := range digest {
		if character >= '0' && character <= '9' {
			continue
		}
		if character >= 'a' && character <= 'f' {
			continue
		}
		if character >= 'A' && character <= 'F' {
			continue
		}
		return false
	}
	return true
}

func writeDispatcherPinStamp(pinPath string, releaseTag string, manifest string) error {
	content, err := os.ReadFile(pinPath)
	if err != nil {
		return fmt.Errorf("read dispatcher pin %s: %w", pinPath, err)
	}
	pin := map[string]json.RawMessage{}
	if err := json.Unmarshal(content, &pin); err != nil {
		return fmt.Errorf("parse dispatcher pin %s: %w", pinPath, err)
	}
	releaseTagJSON, err := json.Marshal(releaseTag)
	if err != nil {
		return fmt.Errorf("encode dispatcher release tag: %w", err)
	}
	manifestJSON, err := json.Marshal(manifest)
	if err != nil {
		return fmt.Errorf("encode dispatcher archive manifest: %w", err)
	}
	pin["dispatcherReleaseTag"] = releaseTagJSON
	pin["dispatcherArchiveManifest"] = manifestJSON
	stampedContent, err := json.MarshalIndent(pin, "", "  ")
	if err != nil {
		return fmt.Errorf("encode stamped dispatcher pin: %w", err)
	}
	stampedContent = append(stampedContent, '\n')
	if err := os.WriteFile(pinPath, stampedContent, 0o644); err != nil {
		return fmt.Errorf("write stamped dispatcher pin %s: %w", pinPath, err)
	}
	return nil
}

func fetchDispatcherReleaseAssets(ctx context.Context, releaseTag string) ([]dispatcherReleaseAsset, error) {
	requestURL := fmt.Sprintf(
		"%s/repos/%s/releases/tags/%s",
		dispatcherPinStampAPIBaseURL,
		attestation.ReleaseRepository,
		releaseTag)
	request, err := http.NewRequestWithContext(ctx, http.MethodGet, requestURL, nil)
	if err != nil {
		return nil, fmt.Errorf("build GitHub release request: %w", err)
	}
	request.Header.Set("Accept", "application/vnd.github+json")
	request.Header.Set("X-GitHub-Api-Version", "2022-11-28")
	if token := dispatcherPinStampGitHubToken(); token != "" {
		request.Header.Set("Authorization", "Bearer "+token)
	}
	client := &http.Client{Timeout: 30 * time.Second}
	response, err := client.Do(request)
	if err != nil {
		return nil, err
	}
	defer func() {
		_ = response.Body.Close()
	}()
	if response.StatusCode < http.StatusOK || response.StatusCode >= http.StatusMultipleChoices {
		return nil, fmt.Errorf("GitHub release API returned %s", response.Status)
	}
	payload := struct {
		Assets []dispatcherReleaseAsset `json:"assets"`
	}{}
	if err := json.NewDecoder(response.Body).Decode(&payload); err != nil {
		return nil, fmt.Errorf("decode GitHub release assets: %w", err)
	}
	return payload.Assets, nil
}

func dispatcherPinStampGitHubToken() string {
	if token := os.Getenv("GITHUB_TOKEN"); token != "" {
		return token
	}
	return os.Getenv("GH_TOKEN")
}
