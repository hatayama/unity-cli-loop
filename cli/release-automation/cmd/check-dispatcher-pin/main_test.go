package main

import "testing"

func TestDispatcherPinPathNeedsNetworkVerificationRecognizesEveryWatchedPath(t *testing.T) {
	// Verifies changes to the pin, installers, verifier, guard, or its workflow revalidate published subjects.
	paths := []string{
		"Packages/src/project-runner-pin.json",
		".uloop/project-runner-pin.json",
		"scripts/install.sh",
		"scripts/install.ps1",
		"cli/dispatcher/attestation/verifier.go",
		"cli/release-automation/internal/automation/dispatcher_pin_guard.go",
		".github/workflows/build-and-test.yml",
	}
	for _, path := range paths {
		if !dispatcherPinPathNeedsNetworkVerification(path) {
			t.Fatalf("expected watched path %q to require network verification", path)
		}
	}
}

func TestDispatcherPinPathNeedsNetworkVerificationSkipsUnrelatedPath(t *testing.T) {
	// Verifies unrelated source edits do not incur a network-dependent guard run.
	if dispatcherPinPathNeedsNetworkVerification("Packages/src/Editor/Domain/CliConstants.cs") {
		t.Fatal("expected unrelated path to skip network verification")
	}
}
