package automation

import (
	"bytes"
	"context"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
)

// Verifies release PRs cannot point minimumDispatcherVersion at a CLI tag without dispatcher metadata.
func TestRunDispatcherMinimumVersionCheck_WhenMinimumReleaseLacksDispatcherContract_Fails(t *testing.T) {
	result := runDispatcherMinimumVersionCheckCase(t, dispatcherMinimumVersionCase{
		currentCliVersion:                "3.0.0-beta.40",
		currentDispatcherContractVersion: 1,
		minimumDispatcherVersion:         "3.0.0-beta.39",
		releaseContract:                  `{"schemaVersion":1,"protocolVersion":2,"cliVersion":"3.0.0-beta.39"}`,
	})

	if result.exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", result.exitCode, result.stdout)
	}
	assertDispatcherMinimumVersionLogContains(t, result.stderr, "does not define dispatcherContractVersion")
	assertDispatcherMinimumVersionLogContains(t, result.gitLog, "cli-v3.0.0-beta.39:cli/contract.json")
}

// Verifies release PRs pass when the pending CLI release itself is the minimum dispatcher version.
func TestRunDispatcherMinimumVersionCheck_WhenMinimumIsCurrentRelease_Passes(t *testing.T) {
	result := runDispatcherMinimumVersionCheckCase(t, dispatcherMinimumVersionCase{
		currentCliVersion:                "3.0.0-beta.40",
		currentDispatcherContractVersion: 1,
		minimumDispatcherVersion:         "3.0.0-beta.40",
	})

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertDispatcherMinimumVersionLogContains(t, result.stdout, "Dispatcher minimum version guard passed.")
	assertDispatcherMinimumVersionLogDoesNotContain(t, result.gitLog, "cli-v3.0.0-beta.40:cli/contract.json")
}

// Verifies committed pin files cannot drift from the C# minimum dispatcher version.
func TestRunDispatcherMinimumVersionCheck_WhenProjectPinDiffersFromPackagePin_Fails(t *testing.T) {
	result := runDispatcherMinimumVersionCheckCase(t, dispatcherMinimumVersionCase{
		currentCliVersion:                  "3.0.0-beta.40",
		currentDispatcherContractVersion:   1,
		minimumDispatcherVersion:           "3.0.0-beta.40",
		projectPinMinimumDispatcherVersion: "3.0.0-beta.39",
	})

	if result.exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", result.exitCode, result.stdout)
	}
	assertDispatcherMinimumVersionLogContains(t, result.stderr, ".uloop/cli-pin.json minimumDispatcherVersion")
}

type dispatcherMinimumVersionCase struct {
	currentCliVersion                  string
	currentDispatcherContractVersion   int
	minimumDispatcherVersion           string
	projectPinMinimumDispatcherVersion string
	releaseContract                    string
}

type dispatcherMinimumVersionRunResult struct {
	exitCode int
	stdout   string
	stderr   string
	gitLog   string
}

func runDispatcherMinimumVersionCheckCase(t *testing.T, testCase dispatcherMinimumVersionCase) dispatcherMinimumVersionRunResult {
	t.Helper()

	workDir := t.TempDir()
	mockBin := filepath.Join(workDir, "bin")
	err := os.MkdirAll(mockBin, 0o755)
	if err != nil {
		t.Fatalf("failed to create mock bin: %v", err)
	}

	gitLogPath := filepath.Join(workDir, "git.log")
	writeDispatcherMinimumVersionMockGit(t, filepath.Join(mockBin, "git"))
	prepareDispatcherMinimumVersionFiles(t, workDir, testCase)

	t.Setenv("PATH", mockBin+string(os.PathListSeparator)+os.Getenv("PATH"))
	t.Setenv("ULOOP_REPOSITORY_ROOT", workDir)
	t.Setenv("GIT_LOG", gitLogPath)
	if testCase.releaseContract != "" {
		releaseContractPath := filepath.Join(workDir, "release-contract.json")
		writeFile(t, releaseContractPath, testCase.releaseContract)
		t.Setenv("GIT_RELEASE_CONTRACT", releaseContractPath)
	}

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := RunDispatcherMinimumVersionCheck(context.Background(), &stdout, &stderr, "")

	return dispatcherMinimumVersionRunResult{
		exitCode: exitCode,
		stdout:   stdout.String(),
		stderr:   stderr.String(),
		gitLog:   readFile(t, gitLogPath),
	}
}

func prepareDispatcherMinimumVersionFiles(t *testing.T, workDir string, testCase dispatcherMinimumVersionCase) {
	t.Helper()

	projectMinimumDispatcherVersion := testCase.projectPinMinimumDispatcherVersion
	if projectMinimumDispatcherVersion == "" {
		projectMinimumDispatcherVersion = testCase.minimumDispatcherVersion
	}

	writeDispatcherMinimumVersionFile(t, filepath.Join(workDir, "cli/contract.json"), buildDispatcherMinimumVersionContract(
		testCase.currentCliVersion,
		testCase.currentDispatcherContractVersion))
	writeDispatcherMinimumVersionFile(t, filepath.Join(workDir, protocolMinimumVersionFile), buildProtocolMinimumVersionConstants(
		2,
		testCase.minimumDispatcherVersion))
	writeDispatcherMinimumVersionFile(t, filepath.Join(workDir, unityPackageCliPinFile), buildDispatcherMinimumVersionPin(
		testCase.currentCliVersion,
		testCase.minimumDispatcherVersion))
	writeDispatcherMinimumVersionFile(t, filepath.Join(workDir, unityProjectCliPinFile), buildDispatcherMinimumVersionPin(
		testCase.currentCliVersion,
		projectMinimumDispatcherVersion))
}

func writeDispatcherMinimumVersionFile(t *testing.T, path string, content string) {
	t.Helper()
	err := os.MkdirAll(filepath.Dir(path), 0o755)
	if err != nil {
		t.Fatalf("failed to create parent directory for %s: %v", path, err)
	}
	writeFile(t, path, content)
}

func buildDispatcherMinimumVersionContract(cliVersion string, dispatcherContractVersion int) string {
	return `{"schemaVersion":1,"protocolVersion":2,"cliVersion":"` + cliVersion + `","dispatcherContractVersion":` +
		strconv.Itoa(dispatcherContractVersion) +
		`}`
}

func buildDispatcherMinimumVersionPin(cliVersion string, minimumDispatcherVersion string) string {
	return `{"schemaVersion":1,"packageName":"test.package","packageVersion":"3.0.0-beta.40","cliVersion":"` +
		cliVersion +
		`","requiredProtocolVersion":2,"minimumDispatcherVersion":"` +
		minimumDispatcherVersion +
		`"}`
}

func writeDispatcherMinimumVersionMockGit(t *testing.T, path string) {
	t.Helper()

	content := `#!/bin/sh
set -eu

printf '%s\n' "$*" >> "$GIT_LOG"

if [ "$1" = "rev-parse" ] && [ "$2" = "--show-toplevel" ]; then
  printf '%s\n' "$ULOOP_REPOSITORY_ROOT"
  exit 0
fi

if [ "$1" = "-C" ]; then
  shift 2
fi

if [ "$1" = "show" ]; then
  case "$2" in
    cli-v*:cli/contract.json)
      if [ -n "${GIT_RELEASE_CONTRACT:-}" ]; then
        cat "$GIT_RELEASE_CONTRACT"
      else
        echo "release not found" >&2
        exit 1
      fi
      ;;
    *) echo "unexpected git show ref: $2" >&2; exit 1 ;;
  esac
  exit 0
fi

echo "unexpected git command: $*" >&2
exit 1
`
	writeFile(t, path, content)
	err := os.Chmod(path, 0o755)
	if err != nil {
		t.Fatalf("failed to chmod mock git: %v", err)
	}
}

func assertDispatcherMinimumVersionLogContains(t *testing.T, actual string, expected string) {
	t.Helper()
	if !strings.Contains(actual, expected) {
		t.Fatalf("expected log to contain %q, got:\n%s", expected, actual)
	}
}

func assertDispatcherMinimumVersionLogDoesNotContain(t *testing.T, actual string, unexpected string) {
	t.Helper()
	if strings.Contains(actual, unexpected) {
		t.Fatalf("expected log not to contain %q, got:\n%s", unexpected, actual)
	}
}
