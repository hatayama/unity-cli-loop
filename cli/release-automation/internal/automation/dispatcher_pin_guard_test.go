package automation

import (
	"context"
	"errors"
	"testing"
)

func TestValidateDispatcherPinOfflineRejectsManifestWithoutRequiredArchive(t *testing.T) {
	// Verifies a pin cannot authorize bootstrap when one supported platform archive is absent.
	pin := []byte(`{"projectRunnerVersion":"3.0.0-beta.47","minimumDispatcherVersion":"3.0.1-beta.6","dispatcherReleaseTag":"dispatcher-v3.0.1-beta.6","dispatcherArchiveManifest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  install.ps1\ncccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc  uloop-dispatcher-darwin-arm64.tar.gz\ndddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd  uloop-dispatcher-darwin-amd64.tar.gz"}`)

	err := ValidateDispatcherPinOffline(pin, pin)

	if err == nil {
		t.Fatal("expected required Windows archive failure")
	}
}

func TestValidateDispatcherPinOfflineRejectsUnsortedManifest(t *testing.T) {
	// Verifies the guard requires canonical manifest order so a re-stamp is byte reproducible.
	pin := []byte(`{"projectRunnerVersion":"3.0.0-beta.47","minimumDispatcherVersion":"3.0.1-beta.6","dispatcherReleaseTag":"dispatcher-v3.0.1-beta.6","dispatcherArchiveManifest":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  install.ps1\naaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh\ncccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc  uloop-dispatcher-darwin-amd64.tar.gz\ndddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd  uloop-dispatcher-darwin-arm64.tar.gz\neeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee  uloop-dispatcher-windows-amd64.zip"}`)

	err := ValidateDispatcherPinOffline(pin, pin)

	if err == nil {
		t.Fatal("expected unsorted manifest failure")
	}
}

func TestValidateDispatcherPinOfflineRejectsMismatchedProjectCopy(t *testing.T) {
	// Verifies the project mirror cannot silently diverge from the package trust root.
	packagePin := validDispatcherPinGuardFixture()
	projectPin := []byte(`{"projectRunnerVersion":"3.0.0-beta.47"}`)

	err := ValidateDispatcherPinOffline(packagePin, projectPin)

	if err == nil {
		t.Fatal("expected mismatched project pin failure")
	}
}

func TestValidateDispatcherPinOfflineRejectsMalformedManifest(t *testing.T) {
	// Verifies malformed digest entries cannot become package bootstrap trust inputs.
	pin := []byte(`{"projectRunnerVersion":"3.0.0-beta.47","minimumDispatcherVersion":"3.0.1-beta.6","dispatcherReleaseTag":"dispatcher-v3.0.1-beta.6","dispatcherArchiveManifest":"not-a-digest  install.sh"}`)

	err := ValidateDispatcherPinOffline(pin, pin)

	if err == nil {
		t.Fatal("expected malformed manifest failure")
	}
}

func TestValidateDispatcherPinOfflineRejectsPinnedVersionBelowMinimum(t *testing.T) {
	// Verifies bootstrap never stamps a dispatcher release that the package immediately rejects.
	pin := []byte(`{"projectRunnerVersion":"3.0.0-beta.47","minimumDispatcherVersion":"3.0.2","dispatcherReleaseTag":"dispatcher-v3.0.1-beta.6","dispatcherArchiveManifest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  install.ps1\ncccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc  uloop-dispatcher-darwin-amd64.tar.gz\ndddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd  uloop-dispatcher-darwin-arm64.tar.gz\neeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee  uloop-dispatcher-windows-amd64.zip"}`)

	err := ValidateDispatcherPinOffline(pin, pin)

	if err == nil {
		t.Fatal("expected pinned version below minimum failure")
	}
}

func TestVerifyDispatcherPinSubjectsRejectsManifestThatDoesNotExactlyMatchSubjects(t *testing.T) {
	// Verifies the network guard rejects both omitted and unverified release subjects.
	pin := validDispatcherPinGuardFixture()
	deps := validDispatcherPinGuardDeps()
	deps.verifySubjects = func([]byte, string) (map[string]string, error) {
		return map[string]string{
			"install.sh":                           "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
			"install.ps1":                          "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
			"uloop-dispatcher-darwin-amd64.tar.gz": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
			"uloop-dispatcher-darwin-arm64.tar.gz": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
			"uloop-dispatcher-windows-amd64.zip":   "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
			"uloop-dispatcher-unexpected.tar.gz":   "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
		}, nil
	}

	err := verifyDispatcherPinSubjects(context.Background(), pin, deps)

	if err == nil {
		t.Fatal("expected exact subject-set mismatch failure")
	}
}

func TestVerifyDispatcherPinSubjectsFailsClosedWhenReleaseLookupFails(t *testing.T) {
	// Verifies GitHub API failures cannot skip published subject verification.
	pin := validDispatcherPinGuardFixture()
	deps := validDispatcherPinGuardDeps()
	deps.fetchReleaseAssets = func(context.Context, string) ([]dispatcherReleaseAsset, error) {
		return nil, errors.New("GitHub unavailable")
	}

	err := verifyDispatcherPinSubjects(context.Background(), pin, deps)

	if err == nil {
		t.Fatal("expected GitHub API failure")
	}
}

func TestDispatcherPinScriptDriftWarningsReportsButDoesNotFailForChangedSourceScript(t *testing.T) {
	// Verifies installer source drift is review information until a subsequent release can carry the new digest.
	warnings, err := DispatcherPinScriptDriftWarnings(validDispatcherPinGuardFixture(), map[string][]byte{
		"install.sh":  []byte("changed"),
		"install.ps1": []byte("changed"),
	})
	if err != nil {
		t.Fatalf("DispatcherPinScriptDriftWarnings failed: %v", err)
	}
	if len(warnings) != 2 {
		t.Fatalf("warning count = %d, want 2", len(warnings))
	}
}

func validDispatcherPinGuardDeps() dispatcherPinStampDeps {
	return dispatcherPinStampDeps{
		fetchReleaseAssets: func(context.Context, string) ([]dispatcherReleaseAsset, error) {
			return []dispatcherReleaseAsset{
				{Name: "install.sh", URL: "https://example.invalid/install.sh"},
				{Name: "install.ps1", URL: "https://example.invalid/install.ps1"},
				{Name: "uloop-dispatcher-darwin-amd64.tar.gz", URL: "https://example.invalid/darwin-amd64.tar.gz"},
				{Name: "uloop-dispatcher-darwin-arm64.tar.gz", URL: "https://example.invalid/darwin-arm64.tar.gz"},
				{Name: "uloop-dispatcher-windows-amd64.zip", URL: "https://example.invalid/windows-amd64.zip"},
			}, nil
		},
		fetchBundle:       func(context.Context, string) ([]byte, error) { return []byte("bundle"), nil },
		fetchTagCommitSHA: func(context.Context, string, string) (string, error) { return "commit", nil },
		verifySubjects: func([]byte, string) (map[string]string, error) {
			return map[string]string{
				"install.sh":                           "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
				"install.ps1":                          "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
				"uloop-dispatcher-darwin-amd64.tar.gz": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
				"uloop-dispatcher-darwin-arm64.tar.gz": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
				"uloop-dispatcher-windows-amd64.zip":   "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
			}, nil
		},
	}
}

func validDispatcherPinGuardFixture() []byte {
	return []byte(`{"projectRunnerVersion":"3.0.0-beta.47","minimumDispatcherVersion":"3.0.1-beta.6","dispatcherReleaseTag":"dispatcher-v3.0.1-beta.6","dispatcherArchiveManifest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  install.ps1\ncccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc  uloop-dispatcher-darwin-amd64.tar.gz\ndddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd  uloop-dispatcher-darwin-arm64.tar.gz\neeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee  uloop-dispatcher-windows-amd64.zip"}`)
}
