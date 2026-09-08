package automation

import (
	"bytes"
	"context"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// writePackagePinConsistencyRepo builds a git repository whose HEAD carries the
// given manifest and pin file contents, so the check can be exercised against a
// real ref instead of a stubbed reader.
func writePackagePinConsistencyRepo(t *testing.T, manifest string, pin string) string {
	t.Helper()

	repoRoot := t.TempDir()
	runGitInRepo(t, repoRoot, "init", "-b", "main")

	if manifest != "" {
		writePackagePinConsistencyFile(t, repoRoot, releasePleaseManifestRelativePath, manifest)
	}
	if pin != "" {
		writePackagePinConsistencyFile(t, repoRoot, unityPackageCliPinFile, pin)
	}
	// A commit needs at least one tracked file even when both inputs are absent.
	writePackagePinConsistencyFile(t, repoRoot, "README.md", "package pin consistency fixture\n")

	runGitInRepo(t, repoRoot, "add", "-A")
	runGitInRepo(t, repoRoot, "commit", "-m", "fixture")
	return repoRoot
}

func writePackagePinConsistencyFile(t *testing.T, repoRoot string, relativePath string, content string) {
	t.Helper()

	absolutePath := filepath.Join(repoRoot, filepath.FromSlash(relativePath))
	err := os.MkdirAll(filepath.Dir(absolutePath), 0o755)
	if err != nil {
		t.Fatalf("failed to create directory for %s: %v", relativePath, err)
	}
	err = os.WriteFile(absolutePath, []byte(content), 0o644)
	if err != nil {
		t.Fatalf("failed to write %s: %v", relativePath, err)
	}
}

func runPackagePinConsistencyCheck(t *testing.T, repoRoot string) (int, string, string) {
	t.Helper()

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := RunPackagePinConsistencyCheck(
		context.Background(), &stdout, &stderr, []string{"--repo-root", repoRoot, "--ref", "HEAD"})
	return exitCode, stdout.String(), stderr.String()
}

// Verifies a ref whose pin records exactly the dispatcher its manifest releases passes the check.
func TestPackagePinConsistencyAcceptsMatchingDispatcherTag(t *testing.T) {
	repoRoot := writePackagePinConsistencyRepo(t,
		`{"Packages/src":"3.6.0","cli/dispatcher":"3.4.0","cli/project-runner":"3.6.0"}`,
		`{"dispatcherReleaseTag":"dispatcher-v3.4.0","projectRunnerVersion":"3.6.0"}`)

	exitCode, stdout, stderr := runPackagePinConsistencyCheck(t, repoRoot)

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	if !strings.Contains(stdout, "dispatcher-v3.4.0") {
		t.Fatalf("expected the matching tag in stdout, got %q", stdout)
	}
}

// Verifies a ref whose pin still records the previous dispatcher fails, because releasing the package there would ship a stale dispatcher.
func TestPackagePinConsistencyRejectsStaleDispatcherTag(t *testing.T) {
	repoRoot := writePackagePinConsistencyRepo(t,
		`{"Packages/src":"3.6.0","cli/dispatcher":"3.4.0","cli/project-runner":"3.6.0"}`,
		`{"dispatcherReleaseTag":"dispatcher-v3.3.1","projectRunnerVersion":"3.6.0"}`)

	exitCode, stdout, stderr := runPackagePinConsistencyCheck(t, repoRoot)

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", exitCode, stdout)
	}
	if !strings.Contains(stderr, "dispatcher-v3.3.1") || !strings.Contains(stderr, "dispatcher-v3.4.0") {
		t.Fatalf("expected both tags in the failure message, got %q", stderr)
	}
}

// Verifies a missing pin file fails closed instead of being read as "nothing to compare".
func TestPackagePinConsistencyFailsWhenPinIsMissing(t *testing.T) {
	repoRoot := writePackagePinConsistencyRepo(t,
		`{"Packages/src":"3.6.0","cli/dispatcher":"3.4.0"}`,
		"")

	exitCode, stdout, stderr := runPackagePinConsistencyCheck(t, repoRoot)

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", exitCode, stdout)
	}
	if !strings.Contains(stderr, unityPackageCliPinFile) {
		t.Fatalf("expected the missing pin path in the failure message, got %q", stderr)
	}
}

// Verifies a pin whose dispatcherReleaseTag key is absent fails closed rather than comparing an empty tag.
func TestPackagePinConsistencyFailsWhenDispatcherReleaseTagIsMissing(t *testing.T) {
	repoRoot := writePackagePinConsistencyRepo(t,
		`{"Packages/src":"3.6.0","cli/dispatcher":"3.4.0"}`,
		`{"projectRunnerVersion":"3.6.0"}`)

	exitCode, stdout, stderr := runPackagePinConsistencyCheck(t, repoRoot)

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", exitCode, stdout)
	}
	if !strings.Contains(stderr, "dispatcherReleaseTag") {
		t.Fatalf("expected the missing key in the failure message, got %q", stderr)
	}
}

// Verifies malformed JSON fails closed instead of defaulting to an empty tag comparison.
func TestPackagePinConsistencyFailsWhenPinJSONIsInvalid(t *testing.T) {
	repoRoot := writePackagePinConsistencyRepo(t,
		`{"Packages/src":"3.6.0","cli/dispatcher":"3.4.0"}`,
		`{"dispatcherReleaseTag":`)

	exitCode, stdout, stderr := runPackagePinConsistencyCheck(t, repoRoot)

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", exitCode, stdout)
	}
	if !strings.Contains(stderr, unityPackageCliPinFile) {
		t.Fatalf("expected the unparsable pin path in the failure message, got %q", stderr)
	}
}

// Verifies a pin tag padded with whitespace is rejected rather than trimmed, since the merge automation compares the pin verbatim.
func TestPackagePinConsistencyRejectsPaddedDispatcherReleaseTag(t *testing.T) {
	repoRoot := writePackagePinConsistencyRepo(t,
		`{"Packages/src":"3.6.0","cli/dispatcher":"3.4.0"}`,
		`{"dispatcherReleaseTag":" dispatcher-v3.4.0 ","projectRunnerVersion":"3.6.0"}`)

	exitCode, stdout, stderr := runPackagePinConsistencyCheck(t, repoRoot)

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", exitCode, stdout)
	}
	if !strings.Contains(stderr, "not a bare tag") {
		t.Fatalf("expected the non-canonical tag to be named in the failure message, got %q", stderr)
	}
}

// Verifies a manifest version padded with whitespace is rejected rather than trimmed into a matching tag.
func TestPackagePinConsistencyRejectsPaddedManifestVersion(t *testing.T) {
	repoRoot := writePackagePinConsistencyRepo(t,
		`{"Packages/src":"3.6.0","cli/dispatcher":" 3.4.0 "}`,
		`{"dispatcherReleaseTag":"dispatcher-v3.4.0","projectRunnerVersion":"3.6.0"}`)

	exitCode, stdout, stderr := runPackagePinConsistencyCheck(t, repoRoot)

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", exitCode, stdout)
	}
	if !strings.Contains(stderr, "not a bare version") {
		t.Fatalf("expected the non-canonical version to be named in the failure message, got %q", stderr)
	}
}
