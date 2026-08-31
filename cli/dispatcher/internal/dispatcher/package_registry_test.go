package dispatcher

import (
	"context"
	"net/http"
	"net/http/httptest"
	"testing"
)

// Verifies dist-tags.latest is returned from the OpenUPM package metadata endpoint.
func TestResolveLatestPackageVersionReadsDistTags(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		if request.URL.Path != "/"+dispatcherUnityPackageName {
			t.Fatalf("unexpected path: %s", request.URL.Path)
		}
		writer.Header().Set("Content-Type", "application/json")
		_, _ = writer.Write([]byte(`{"dist-tags":{"latest":"9.8.7"}}`))
	}))
	t.Cleanup(server.Close)

	previousURL := openUPMRegistryBaseURL
	previousClient := packageRegistryHTTPClient
	openUPMRegistryBaseURL = server.URL
	packageRegistryHTTPClient = server.Client()
	t.Cleanup(func() {
		openUPMRegistryBaseURL = previousURL
		packageRegistryHTTPClient = previousClient
	})

	version, err := resolveLatestPackageVersion(context.Background())
	if err != nil {
		t.Fatalf("resolveLatestPackageVersion failed: %v", err)
	}
	if version != "9.8.7" {
		t.Fatalf("version mismatch: %s", version)
	}
}

// Verifies non-2xx registry responses surface as errors.
func TestResolveLatestPackageVersionReportsHTTPFailure(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, _ *http.Request) {
		writer.WriteHeader(http.StatusNotFound)
	}))
	t.Cleanup(server.Close)

	previousURL := openUPMRegistryBaseURL
	previousClient := packageRegistryHTTPClient
	openUPMRegistryBaseURL = server.URL
	packageRegistryHTTPClient = server.Client()
	t.Cleanup(func() {
		openUPMRegistryBaseURL = previousURL
		packageRegistryHTTPClient = previousClient
	})

	_, err := resolveLatestPackageVersion(context.Background())
	if err == nil {
		t.Fatal("expected HTTP failure error")
	}
}

// Verifies an empty dist-tags.latest value is rejected.
func TestResolveLatestPackageVersionRejectsEmptyLatest(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, _ *http.Request) {
		writer.Header().Set("Content-Type", "application/json")
		_, _ = writer.Write([]byte(`{"dist-tags":{}}`))
	}))
	t.Cleanup(server.Close)

	previousURL := openUPMRegistryBaseURL
	previousClient := packageRegistryHTTPClient
	openUPMRegistryBaseURL = server.URL
	packageRegistryHTTPClient = server.Client()
	t.Cleanup(func() {
		openUPMRegistryBaseURL = previousURL
		packageRegistryHTTPClient = previousClient
	})

	_, err := resolveLatestPackageVersion(context.Background())
	if err == nil {
		t.Fatal("expected empty latest error")
	}
}
