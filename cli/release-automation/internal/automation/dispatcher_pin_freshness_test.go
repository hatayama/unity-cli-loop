package automation

import (
	"bytes"
	"context"
	"fmt"
	"strings"
	"testing"
)

type dispatcherPinFreshnessResult struct {
	exitCode int
	stdout   string
	stderr   string
}

// runDispatcherPinFreshnessCase exercises the guard with a fixed pin and a
// fixed release listing, so no repository checkout or network call is involved.
func runDispatcherPinFreshnessCase(
	t *testing.T,
	pinContent string,
	releases []dispatcherRelease,
	fetchError error,
) dispatcherPinFreshnessResult {
	t.Helper()
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	deps := dispatcherPinFreshnessDeps{
		fetchReleases: func(_ context.Context, _ string) ([]dispatcherRelease, error) {
			return releases, fetchError
		},
		readPin: func(string) ([]byte, error) {
			return []byte(pinContent), nil
		},
	}
	exitCode := runDispatcherPinFreshnessCheck(
		context.Background(),
		&stdout,
		&stderr,
		dispatcherPinFreshnessConfig{repository: "owner/repository", pinPath: "pin.json"},
		deps)
	return dispatcherPinFreshnessResult{exitCode: exitCode, stdout: stdout.String(), stderr: stderr.String()}
}

func dispatcherPinFreshnessPin(tag string) string {
	return fmt.Sprintf(`{"minimumDispatcherVersion":"3.0.0","dispatcherReleaseTag":%q}`, tag)
}

func stableDispatcherRelease(tag string) dispatcherRelease {
	return dispatcherRelease{TagName: tag}
}

func TestRunDispatcherPinFreshnessCheckFailsWhenPinLagsBehindNewestStableRelease(t *testing.T) {
	// Verifies a pin older than the newest stable dispatcher release fails with the remediation message.
	result := runDispatcherPinFreshnessCase(
		t,
		dispatcherPinFreshnessPin("dispatcher-v3.0.1"),
		[]dispatcherRelease{stableDispatcherRelease("dispatcher-v3.0.1"), stableDispatcherRelease("dispatcher-v3.1.0")},
		nil)

	if result.exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", result.exitCode, result.stdout)
	}
	expected := "check-dispatcher-pin-freshness: pin records dispatcher-v3.0.1 but the newest stable " +
		"dispatcher release is dispatcher-v3.1.0. Merge the automated pin-stamp pull request, " +
		"or run stamp-dispatcher-pin --tag dispatcher-v3.1.0."
	if strings.TrimSpace(result.stderr) != expected {
		t.Fatalf("expected stderr %q, got %q", expected, result.stderr)
	}
}

func TestRunDispatcherPinFreshnessCheckPassesWhenPinRecordsNewestStableRelease(t *testing.T) {
	// Verifies a pin equal to the newest stable dispatcher release passes.
	result := runDispatcherPinFreshnessCase(
		t,
		dispatcherPinFreshnessPin("dispatcher-v3.1.0"),
		[]dispatcherRelease{stableDispatcherRelease("dispatcher-v3.0.1"), stableDispatcherRelease("dispatcher-v3.1.0")},
		nil)

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	if !strings.Contains(result.stdout, "Dispatcher pin freshness guard passed") {
		t.Fatalf("expected a pass line, got %q", result.stdout)
	}
}

func TestRunDispatcherPinFreshnessCheckPassesWhenPinIsAheadOfPublishedReleases(t *testing.T) {
	// Verifies a pin newer than every published release passes instead of failing on the ordering.
	result := runDispatcherPinFreshnessCase(
		t,
		dispatcherPinFreshnessPin("dispatcher-v3.2.0"),
		[]dispatcherRelease{stableDispatcherRelease("dispatcher-v3.1.0")},
		nil)

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
}

func TestRunDispatcherPinFreshnessCheckIgnoresDraftPreReleaseAndOtherComponentReleases(t *testing.T) {
	// Verifies drafts, pre-releases, pre-release identifiers, and other components' tags never move the pin target.
	result := runDispatcherPinFreshnessCase(
		t,
		dispatcherPinFreshnessPin("dispatcher-v3.0.1"),
		[]dispatcherRelease{
			stableDispatcherRelease("dispatcher-v3.0.1"),
			{TagName: "dispatcher-v4.0.0", Draft: true},
			{TagName: "dispatcher-v5.0.0", Prerelease: true},
			stableDispatcherRelease("dispatcher-v6.0.0-beta.1"),
			stableDispatcherRelease("v7.0.0"),
			stableDispatcherRelease("project-runner-v8.0.0"),
		},
		nil)

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	if !strings.Contains(result.stdout, "the newest stable dispatcher release is dispatcher-v3.0.1") {
		t.Fatalf("expected dispatcher-v3.0.1 to remain the newest stable release, got %q", result.stdout)
	}
}

func TestRunDispatcherPinFreshnessCheckPassesWhenNoStableDispatcherReleaseExists(t *testing.T) {
	// Verifies the initial state, before any stable dispatcher release exists, is not reported as stale.
	result := runDispatcherPinFreshnessCase(
		t,
		dispatcherPinFreshnessPin("dispatcher-v3.0.1"),
		[]dispatcherRelease{},
		nil)

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	if !strings.Contains(result.stdout, "No stable dispatcher release is published yet") {
		t.Fatalf("expected the initial-state pass line, got %q", result.stdout)
	}
}

func TestRunDispatcherPinFreshnessCheckFailsWhenReleaseListingFails(t *testing.T) {
	// Verifies an API failure fails the check instead of silently passing.
	result := runDispatcherPinFreshnessCase(
		t,
		dispatcherPinFreshnessPin("dispatcher-v3.0.1"),
		nil,
		fmt.Errorf("GitHub release list API returned 503 Service Unavailable"))

	if result.exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", result.exitCode, result.stdout)
	}
	if !strings.Contains(result.stderr, "503 Service Unavailable") {
		t.Fatalf("expected the fetch error in stderr, got %q", result.stderr)
	}
}

func TestRunDispatcherPinFreshnessCheckFailsWhenPinnedTagIsUnusable(t *testing.T) {
	// Verifies a pin whose dispatcherReleaseTag is not a dispatcher release tag fails the check.
	result := runDispatcherPinFreshnessCase(
		t,
		dispatcherPinFreshnessPin("v3.0.1"),
		[]dispatcherRelease{stableDispatcherRelease("dispatcher-v3.0.1")},
		nil)

	if result.exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", result.exitCode, result.stdout)
	}
	if !strings.Contains(result.stderr, "dispatcherReleaseTag is unusable") {
		t.Fatalf("expected an unusable-tag error, got %q", result.stderr)
	}
}

func TestRunDispatcherPinFreshnessCheckFailsWhenPinCannotBeRead(t *testing.T) {
	// Verifies a missing or unreadable pin file fails the check rather than passing on an error.
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	deps := dispatcherPinFreshnessDeps{
		fetchReleases: func(_ context.Context, _ string) ([]dispatcherRelease, error) {
			t.Fatal("release listing must not be requested when the pin is unreadable")
			return nil, nil
		},
		readPin: func(string) ([]byte, error) {
			return nil, fmt.Errorf("no such file")
		},
	}

	exitCode := runDispatcherPinFreshnessCheck(
		context.Background(),
		&stdout,
		&stderr,
		dispatcherPinFreshnessConfig{repository: "owner/repository", pinPath: "pin.json"},
		deps)

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", exitCode, stdout.String())
	}
	if !strings.Contains(stderr.String(), "read dispatcher pin") {
		t.Fatalf("expected a pin read error, got %q", stderr.String())
	}
}

func TestParseDispatcherPinFreshnessFlagsDefaultsToTheReleaseRepository(t *testing.T) {
	// Verifies the repository falls back to the release repository and the pin path stays repo-relative.
	t.Setenv("GITHUB_REPOSITORY", "")

	config, err := parseDispatcherPinFreshnessFlags(nil)
	if err != nil {
		t.Fatalf("expected flag parsing to succeed, got %v", err)
	}
	if config.repository == "" || !strings.Contains(config.repository, "/") {
		t.Fatalf("expected a default owner/repository, got %q", config.repository)
	}
	if !strings.HasSuffix(config.pinPath, "project-runner-pin.json") {
		t.Fatalf("expected the package pin path, got %q", config.pinPath)
	}
}

func TestParseDispatcherPinFreshnessFlagsPrefersTheExplicitRepository(t *testing.T) {
	// Verifies --repo overrides the GITHUB_REPOSITORY environment value.
	t.Setenv("GITHUB_REPOSITORY", "environment/repository")

	config, err := parseDispatcherPinFreshnessFlags([]string{"--repo", "flag/repository"})
	if err != nil {
		t.Fatalf("expected flag parsing to succeed, got %v", err)
	}
	if config.repository != "flag/repository" {
		t.Fatalf("expected the flag repository, got %q", config.repository)
	}
}
