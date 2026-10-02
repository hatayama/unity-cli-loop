package automation

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestStampDispatcherPinWritesOnlyVerifiedReleaseSubjects(t *testing.T) {
	// Verifies a successful stamp preserves existing pin fields and writes only attestation-verified subjects.
	pinPath := writeDispatcherPinForStamp(t, `{"projectRunnerVersion":"3.0.0-beta.47","minimumDispatcherVersion":"3.0.1-beta.6"}`)
	deps := dispatcherPinStampDeps{
		fetchReleaseAssets: func(context.Context, string) ([]dispatcherReleaseAsset, error) {
			return []dispatcherReleaseAsset{
				{Name: "install.sh", URL: "https://example.test/install.sh"},
				{Name: "install.ps1", URL: "https://example.test/install.ps1"},
				{Name: "uloop-dispatcher-darwin-arm64.zip", URL: "https://example.test/uloop-dispatcher-darwin-arm64.zip"},
				{Name: "install.sh.sigstore.json", URL: "https://example.test/install.sh.sigstore.json"},
			}, nil
		},
		fetchBundle: func(context.Context, string) ([]byte, error) {
			return []byte("bundle"), nil
		},
		fetchTagCommitSHA: func(context.Context, string, string) (string, error) {
			return "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", nil
		},
		verifySubjects: func([]byte, string) (map[string]string, error) {
			return map[string]string{
				"install.sh":                        "1111111111111111111111111111111111111111111111111111111111111111",
				"install.ps1":                       "2222222222222222222222222222222222222222222222222222222222222222",
				"uloop-dispatcher-darwin-arm64.zip": "3333333333333333333333333333333333333333333333333333333333333333",
			}, nil
		},
	}

	err := stampDispatcherPin(context.Background(), pinPath, "dispatcher-v3.0.1-beta.6", deps)
	if err != nil {
		t.Fatalf("stampDispatcherPin failed: %v", err)
	}
	stamped := readDispatcherPinForStamp(t, pinPath)
	if stamped["projectRunnerVersion"] != "3.0.0-beta.47" {
		t.Fatalf("projectRunnerVersion changed: %v", stamped["projectRunnerVersion"])
	}
	if stamped["minimumDispatcherVersion"] != "3.0.1-beta.6" {
		t.Fatalf("minimumDispatcherVersion changed: %v", stamped["minimumDispatcherVersion"])
	}
	if stamped["dispatcherReleaseTag"] != "dispatcher-v3.0.1-beta.6" {
		t.Fatalf("dispatcherReleaseTag mismatch: %v", stamped["dispatcherReleaseTag"])
	}
	wantManifest := "1111111111111111111111111111111111111111111111111111111111111111  install.sh\n"
	wantManifest += "2222222222222222222222222222222222222222222222222222222222222222  install.ps1\n"
	wantManifest += "3333333333333333333333333333333333333333333333333333333333333333  uloop-dispatcher-darwin-arm64.zip"
	if stamped["dispatcherArchiveManifest"] != wantManifest {
		t.Fatalf("dispatcherArchiveManifest mismatch: %v", stamped["dispatcherArchiveManifest"])
	}
}

func TestStampDispatcherPinLeavesPinUnchangedWhenReleaseAssetIsNotAttested(t *testing.T) {
	// Verifies an unsigned release asset prevents a partial or checksum-only pin stamp.
	initialPin := `{"projectRunnerVersion":"3.0.0-beta.47","minimumDispatcherVersion":"3.0.1-beta.6"}`
	pinPath := writeDispatcherPinForStamp(t, initialPin)
	deps := dispatcherPinStampDeps{
		fetchReleaseAssets: func(context.Context, string) ([]dispatcherReleaseAsset, error) {
			return []dispatcherReleaseAsset{
				{Name: "install.sh", URL: "https://example.test/install.sh"},
				{Name: "install.ps1", URL: "https://example.test/install.ps1"},
			}, nil
		},
		fetchBundle: func(context.Context, string) ([]byte, error) {
			return []byte("bundle"), nil
		},
		fetchTagCommitSHA: func(context.Context, string, string) (string, error) {
			return "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", nil
		},
		verifySubjects: func([]byte, string) (map[string]string, error) {
			return map[string]string{
				"install.sh": "1111111111111111111111111111111111111111111111111111111111111111",
			}, nil
		},
	}

	err := stampDispatcherPin(context.Background(), pinPath, "dispatcher-v3.0.1-beta.6", deps)

	if err == nil {
		t.Fatal("expected missing attested asset to fail")
	}
	content, readErr := os.ReadFile(pinPath)
	if readErr != nil {
		t.Fatalf("read pin after failed stamp: %v", readErr)
	}
	if string(content) != initialPin {
		t.Fatalf("failed stamp changed pin: %s", content)
	}
}

func writeDispatcherPinForStamp(t *testing.T, content string) string {
	t.Helper()
	pinPath := filepath.Join(t.TempDir(), "project-runner-pin.json")
	if err := os.WriteFile(pinPath, []byte(content), 0o644); err != nil {
		t.Fatalf("write pin: %v", err)
	}
	return pinPath
}

func readDispatcherPinForStamp(t *testing.T, pinPath string) map[string]string {
	t.Helper()
	content, err := os.ReadFile(pinPath)
	if err != nil {
		t.Fatalf("read pin: %v", err)
	}
	values := map[string]string{}
	if err := json.Unmarshal(content, &values); err != nil {
		t.Fatalf("parse stamped pin: %v", err)
	}
	return values
}

func TestStampDispatcherPinRejectsEmptyReleaseTag(t *testing.T) {
	// Verifies the exported entry point fails before any network call when no release tag is given.
	pinPath := writeDispatcherPinForStamp(t, `{"projectRunnerVersion":"3.0.0"}`)

	err := StampDispatcherPin(context.Background(), pinPath, "")

	if err == nil || !strings.Contains(err.Error(), "release tag is required") {
		t.Fatalf("expected missing release tag error, got %v", err)
	}
}

func TestStampDispatcherPinReportsEachDependencyFailure(t *testing.T) {
	// Verifies every fetch and verification failure aborts the stamp with a step-specific error and leaves the pin untouched.
	cases := []struct {
		name    string
		mutate  func(*dispatcherPinStampDeps)
		wantErr string
	}{
		{"release assets", func(deps *dispatcherPinStampDeps) {
			deps.fetchReleaseAssets = func(context.Context, string) ([]dispatcherReleaseAsset, error) {
				return nil, errors.New("api down")
			}
		}, "fetch dispatcher release assets: api down"},
		{"missing installer", func(deps *dispatcherPinStampDeps) {
			deps.fetchReleaseAssets = func(context.Context, string) ([]dispatcherReleaseAsset, error) {
				return []dispatcherReleaseAsset{{Name: "install.ps1", URL: "https://example.test/install.ps1"}}, nil
			}
		}, `missing required asset "install.sh"`},
		{"installer without URL", func(deps *dispatcherPinStampDeps) {
			deps.fetchReleaseAssets = func(context.Context, string) ([]dispatcherReleaseAsset, error) {
				return []dispatcherReleaseAsset{{Name: "install.sh"}}, nil
			}
		}, `asset "install.sh" has no download URL`},
		{"missing powershell installer", func(deps *dispatcherPinStampDeps) {
			deps.fetchReleaseAssets = func(context.Context, string) ([]dispatcherReleaseAsset, error) {
				return []dispatcherReleaseAsset{{Name: "install.sh", URL: "https://example.test/install.sh"}}, nil
			}
		}, `missing required asset "install.ps1"`},
		{"bundle", func(deps *dispatcherPinStampDeps) {
			deps.fetchBundle = func(context.Context, string) ([]byte, error) { return nil, errors.New("no bundle") }
		}, "fetch dispatcher installer attestation bundle: no bundle"},
		{"tag commit", func(deps *dispatcherPinStampDeps) {
			deps.fetchTagCommitSHA = func(context.Context, string, string) (string, error) { return "", errors.New("no tag") }
		}, "resolve dispatcher release tag commit: no tag"},
		{"verification", func(deps *dispatcherPinStampDeps) {
			deps.verifySubjects = func([]byte, string) (map[string]string, error) { return nil, errors.New("bad signature") }
		}, "verify dispatcher release attestation: bad signature"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			initialPin := `{"projectRunnerVersion":"3.0.0"}`
			pinPath := writeDispatcherPinForStamp(t, initialPin)
			deps := validDispatcherPinStampDeps()
			testCase.mutate(&deps)

			err := stampDispatcherPin(context.Background(), pinPath, "dispatcher-v3.0.1", deps)

			if err == nil || !strings.Contains(err.Error(), testCase.wantErr) {
				t.Fatalf("expected error containing %q, got %v", testCase.wantErr, err)
			}
			assertDispatcherPinFileContent(t, pinPath, initialPin)
		})
	}
}

func TestStampDispatcherPinFetchesInstallerBundleAndTagCommit(t *testing.T) {
	// Verifies the bundle is fetched from the installer URL and the tag commit is resolved for the requested tag before verification.
	pinPath := writeDispatcherPinForStamp(t, `{}`)
	deps := validDispatcherPinStampDeps()
	var bundleURL, resolvedTag, verifiedCommit string
	deps.fetchBundle = func(_ context.Context, url string) ([]byte, error) {
		bundleURL = url
		return []byte("bundle"), nil
	}
	deps.fetchTagCommitSHA = func(_ context.Context, _ string, tag string) (string, error) {
		resolvedTag = tag
		return "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", nil
	}
	baseVerify := deps.verifySubjects
	deps.verifySubjects = func(bundle []byte, commit string) (map[string]string, error) {
		verifiedCommit = commit
		return baseVerify(bundle, commit)
	}

	if err := stampDispatcherPin(context.Background(), pinPath, "dispatcher-v3.0.1", deps); err != nil {
		t.Fatalf("stampDispatcherPin failed: %v", err)
	}
	if bundleURL != "https://example.test/install.sh.sigstore.json" {
		t.Fatalf("bundle URL = %q", bundleURL)
	}
	if resolvedTag != "dispatcher-v3.0.1" {
		t.Fatalf("resolved tag = %q", resolvedTag)
	}
	if verifiedCommit != "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" {
		t.Fatalf("verified commit = %q", verifiedCommit)
	}
}

func TestBuildDispatcherArchiveManifestRejectsInconsistentReleases(t *testing.T) {
	// Verifies the manifest builder refuses invalid names, duplicates, bad digests, empty releases, and extra subjects.
	validDigest := strings.Repeat("a", 64)
	cases := []struct {
		name     string
		assets   []dispatcherReleaseAsset
		subjects map[string]string
		wantErr  string
	}{
		{"empty name", []dispatcherReleaseAsset{{Name: ""}}, map[string]string{}, "invalid asset name"},
		{"newline in name", []dispatcherReleaseAsset{{Name: "a\nb"}}, map[string]string{}, "invalid asset name"},
		{"duplicate", []dispatcherReleaseAsset{{Name: "a.zip"}, {Name: "a.zip"}}, map[string]string{"a.zip": validDigest}, `duplicate asset "a.zip"`},
		{"short digest", []dispatcherReleaseAsset{{Name: "a.zip"}}, map[string]string{"a.zip": "abc"}, "invalid attested SHA-256 digest"},
		{"non-hex digest", []dispatcherReleaseAsset{{Name: "a.zip"}}, map[string]string{"a.zip": strings.Repeat("g", 64)}, "invalid attested SHA-256 digest"},
		{"only bundles", []dispatcherReleaseAsset{{Name: "install.sh.sigstore.json"}}, map[string]string{}, "no attested assets"},
		{"extra subject", []dispatcherReleaseAsset{{Name: "a.zip"}}, map[string]string{"a.zip": validDigest, "b.zip": validDigest}, "do not exactly match"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			manifest, err := buildDispatcherArchiveManifest(testCase.assets, testCase.subjects)

			if err == nil || !strings.Contains(err.Error(), testCase.wantErr) {
				t.Fatalf("expected error containing %q, got manifest %q and error %v", testCase.wantErr, manifest, err)
			}
		})
	}
}

func TestBuildDispatcherArchiveManifestAcceptsUppercaseDigests(t *testing.T) {
	// Verifies uppercase hexadecimal digests are accepted and kept verbatim in the manifest.
	digest := strings.Repeat("AF09", 16)

	manifest, err := buildDispatcherArchiveManifest(
		[]dispatcherReleaseAsset{{Name: "a.zip"}},
		map[string]string{"a.zip": digest})
	if err != nil {
		t.Fatalf("buildDispatcherArchiveManifest failed: %v", err)
	}
	if manifest != digest+"  a.zip" {
		t.Fatalf("manifest = %q", manifest)
	}
}

func TestWriteDispatcherPinStampReportsUnreadableAndInvalidPins(t *testing.T) {
	// Verifies a missing pin, a malformed pin, and an unwritable pin each fail with a path-specific error.
	directory := t.TempDir()
	missingPath := filepath.Join(directory, "missing.json")
	if err := writeDispatcherPinStamp(missingPath, "tag", "manifest"); err == nil || !strings.Contains(err.Error(), "read dispatcher pin") {
		t.Fatalf("expected read error, got %v", err)
	}

	invalidPath := writeDispatcherPinForStamp(t, "{not json")
	if err := writeDispatcherPinStamp(invalidPath, "tag", "manifest"); err == nil || !strings.Contains(err.Error(), "parse dispatcher pin") {
		t.Fatalf("expected parse error, got %v", err)
	}

	readOnlyPath := writeDispatcherPinForStamp(t, `{}`)
	if err := os.Chmod(readOnlyPath, 0o444); err != nil {
		t.Fatalf("chmod pin: %v", err)
	}
	if err := writeDispatcherPinStamp(readOnlyPath, "tag", "manifest"); err == nil || !strings.Contains(err.Error(), "write stamped dispatcher pin") {
		t.Fatalf("expected write error, got %v", err)
	}
}

func TestDispatcherPinStampGitHubTokenPrefersGitHubToken(t *testing.T) {
	// Verifies GITHUB_TOKEN wins over GH_TOKEN and GH_TOKEN is the fallback.
	t.Setenv("GITHUB_TOKEN", "primary")
	t.Setenv("GH_TOKEN", "fallback")
	if token := dispatcherPinStampGitHubToken(); token != "primary" {
		t.Fatalf("token = %q, want primary", token)
	}

	t.Setenv("GITHUB_TOKEN", "")
	if token := dispatcherPinStampGitHubToken(); token != "fallback" {
		t.Fatalf("token = %q, want fallback", token)
	}
}

func validDispatcherPinStampDeps() dispatcherPinStampDeps {
	return dispatcherPinStampDeps{
		fetchReleaseAssets: func(context.Context, string) ([]dispatcherReleaseAsset, error) {
			return []dispatcherReleaseAsset{
				{Name: "install.sh", URL: "https://example.test/install.sh"},
				{Name: "install.ps1", URL: "https://example.test/install.ps1"},
			}, nil
		},
		fetchBundle: func(context.Context, string) ([]byte, error) {
			return []byte("bundle"), nil
		},
		fetchTagCommitSHA: func(context.Context, string, string) (string, error) {
			return "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", nil
		},
		verifySubjects: func([]byte, string) (map[string]string, error) {
			return map[string]string{
				"install.sh":  strings.Repeat("1", 64),
				"install.ps1": strings.Repeat("2", 64),
			}, nil
		},
	}
}

func assertDispatcherPinFileContent(t *testing.T, pinPath string, want string) {
	t.Helper()
	content, err := os.ReadFile(pinPath)
	if err != nil {
		t.Fatalf("read pin: %v", err)
	}
	if string(content) != want {
		t.Fatalf("pin content = %s, want %s", content, want)
	}
}

func TestFetchDispatcherReleaseAssetsReadsTheReleaseByTag(t *testing.T) {
	// Verifies the release is requested by tag with the API headers and bearer token, and its assets are decoded.
	t.Setenv("GITHUB_TOKEN", "test-token")
	var requestPath, authorization, apiVersion string
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		requestPath = request.URL.Path
		authorization = request.Header.Get("Authorization")
		apiVersion = request.Header.Get("X-GitHub-Api-Version")
		_, _ = writer.Write([]byte(`{"assets":[{"name":"install.sh","browser_download_url":"https://example.test/install.sh"}]}`))
	}))
	defer server.Close()

	assets, err := fetchDispatcherReleaseAssets(context.Background(), server.URL, "dispatcher-v3.0.1")
	if err != nil {
		t.Fatalf("fetchDispatcherReleaseAssets failed: %v", err)
	}
	if len(assets) != 1 || assets[0].Name != "install.sh" || assets[0].URL != "https://example.test/install.sh" {
		t.Fatalf("assets = %+v", assets)
	}
	if !strings.HasSuffix(requestPath, "/releases/tags/dispatcher-v3.0.1") {
		t.Fatalf("request path = %q", requestPath)
	}
	if authorization != "Bearer test-token" || apiVersion != "2022-11-28" {
		t.Fatalf("authorization = %q, api version = %q", authorization, apiVersion)
	}
}

func TestFetchDispatcherReleaseAssetsReportsUnusableResponses(t *testing.T) {
	// Verifies an error status and an undecodable body are reported instead of returning an empty asset list.
	cases := []struct {
		name    string
		status  int
		body    string
		wantErr string
	}{
		{"not found", http.StatusNotFound, `{}`, "GitHub release API returned 404"},
		{"invalid body", http.StatusOK, "{", "decode GitHub release assets"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			t.Setenv("GITHUB_TOKEN", "")
			t.Setenv("GH_TOKEN", "")
			server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, _ *http.Request) {
				writer.WriteHeader(testCase.status)
				_, _ = writer.Write([]byte(testCase.body))
			}))
			defer server.Close()

			assets, err := fetchDispatcherReleaseAssets(context.Background(), server.URL, "dispatcher-v3.0.1")

			if err == nil || !strings.Contains(err.Error(), testCase.wantErr) {
				t.Fatalf("expected error containing %q, got assets %+v and error %v", testCase.wantErr, assets, err)
			}
		})
	}
}

func TestFetchDispatcherReleaseAssetsReportsRequestFailures(t *testing.T) {
	// Verifies an unbuildable request URL and an unreachable server both fail instead of returning assets.
	if _, err := fetchDispatcherReleaseAssets(context.Background(), "http://bad host", "tag"); err == nil || !strings.Contains(err.Error(), "build GitHub release request") {
		t.Fatalf("expected a request build error, got %v", err)
	}

	server := httptest.NewServer(http.NotFoundHandler())
	closedURL := server.URL
	server.Close()
	if _, err := fetchDispatcherReleaseAssets(context.Background(), closedURL, "tag"); err == nil {
		t.Fatal("expected an unreachable server to fail")
	}
}
