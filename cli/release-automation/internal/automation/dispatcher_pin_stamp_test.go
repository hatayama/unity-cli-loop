package automation

import (
	"context"
	"encoding/json"
	"os"
	"path/filepath"
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
