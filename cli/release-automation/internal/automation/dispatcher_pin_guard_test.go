package automation

import "testing"

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

func validDispatcherPinGuardFixture() []byte {
	return []byte(`{"projectRunnerVersion":"3.0.0-beta.47","minimumDispatcherVersion":"3.0.1-beta.6","dispatcherReleaseTag":"dispatcher-v3.0.1-beta.6","dispatcherArchiveManifest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  install.ps1\ncccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc  uloop-dispatcher-darwin-amd64.tar.gz\ndddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd  uloop-dispatcher-darwin-arm64.tar.gz\neeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee  uloop-dispatcher-windows-amd64.zip"}`)
}
