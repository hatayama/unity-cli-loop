package dispatcher

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"strings"
	"time"
)

// Overridable in tests (httptest): see attestation/fetcher.go for the same pattern.
var (
	packageRegistryHTTPClient = &http.Client{Timeout: 30 * time.Second}
	openUPMRegistryBaseURL    = openUPMRegistryURL
)

func resolveLatestPackageVersion(ctx context.Context) (string, error) {
	requestURL := strings.TrimRight(openUPMRegistryBaseURL, "/") + "/" + dispatcherUnityPackageName
	request, err := http.NewRequestWithContext(ctx, http.MethodGet, requestURL, nil)
	if err != nil {
		return "", err
	}

	response, err := packageRegistryHTTPClient.Do(request)
	if err != nil {
		return "", err
	}
	defer func() { _ = response.Body.Close() }()

	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return "", fmt.Errorf("OpenUPM registry returned HTTP %d", response.StatusCode)
	}

	var payload struct {
		DistTags struct {
			Latest string `json:"latest"`
		} `json:"dist-tags"`
	}
	if err := json.NewDecoder(response.Body).Decode(&payload); err != nil {
		return "", fmt.Errorf("decode OpenUPM registry response: %w", err)
	}
	if payload.DistTags.Latest == "" {
		return "", fmt.Errorf("OpenUPM registry response missing dist-tags.latest")
	}
	return payload.DistTags.Latest, nil
}
