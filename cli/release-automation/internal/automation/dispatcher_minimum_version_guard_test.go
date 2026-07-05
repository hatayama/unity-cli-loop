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

// Verifies release PRs pass when the current dispatcher release itself is the minimum dispatcher version.
func TestRunDispatcherMinimumVersionCheck_WhenMinimumIsCurrentRelease_Passes(t *testing.T) {
	result := runDispatcherMinimumVersionCheckCase(t, dispatcherMinimumVersionCase{
		currentProjectRunnerVersion: "3.0.0-beta.40",
		currentDispatcherVersion:    "1.0.0",
		minimumDispatcherVersion:    "1.0.0",
	})

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertDispatcherMinimumVersionLogContains(t, result.stdout, "Dispatcher minimum version guard passed.")
	assertDispatcherMinimumVersionLogDoesNotContain(t, result.gitLog, "dispatcher-v1.0.0:"+dispatcherContractFile)
}

// Verifies dispatcher releases published after the cli/ grouping move but
// before dispatchercontract existed fall back to the previous cli/dispatcher path.
func TestRunDispatcherMinimumVersionCheck_WhenMinimumReleaseIsPreviousCliGeneration_FallsBackToCliDispatcherRootPath(t *testing.T) {
	result := runDispatcherMinimumVersionCheckCase(t, dispatcherMinimumVersionCase{
		currentProjectRunnerVersion: "3.0.0-beta.40",
		currentDispatcherVersion:    "1.0.1",
		minimumDispatcherVersion:    "1.0.0",
		previousReleaseContract:     `{"dispatcherVersion":"1.0.0"}`,
	})

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertDispatcherMinimumVersionLogContains(t, result.stdout, "Dispatcher minimum version guard passed.")
	assertDispatcherMinimumVersionLogContains(t, result.gitLog, "dispatcher-v1.0.0:"+dispatcherContractFile)
	assertDispatcherMinimumVersionLogContains(t, result.gitLog, "dispatcher-v1.0.0:"+cliDispatcherRootContractFile)
	assertDispatcherMinimumVersionLogDoesNotContain(t, result.gitLog, "dispatcher-v1.0.0:"+rootModulesDispatcherContractFile)
}

// Verifies dispatcher releases published between the v3 module split and the
// later cli/ grouping move fall back to the middle-generation dispatcher path.
func TestRunDispatcherMinimumVersionCheck_WhenMinimumReleaseIsMiddleGeneration_FallsBackToRootModulesPath(t *testing.T) {
	result := runDispatcherMinimumVersionCheckCase(t, dispatcherMinimumVersionCase{
		currentProjectRunnerVersion: "3.0.0-beta.40",
		currentDispatcherVersion:    "1.0.1",
		minimumDispatcherVersion:    "1.0.0",
		middleReleaseContract:       `{"dispatcherVersion":"1.0.0"}`,
	})

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertDispatcherMinimumVersionLogContains(t, result.stdout, "Dispatcher minimum version guard passed.")
	assertDispatcherMinimumVersionLogContains(t, result.gitLog, "dispatcher-v1.0.0:"+dispatcherContractFile)
	assertDispatcherMinimumVersionLogContains(t, result.gitLog, "dispatcher-v1.0.0:"+cliDispatcherRootContractFile)
	assertDispatcherMinimumVersionLogContains(t, result.gitLog, "dispatcher-v1.0.0:"+rootModulesDispatcherContractFile)
}

// Verifies dispatcher releases published before the original v3 module split
// remain readable through the oldest cli/dispatcher-contract.json path.
func TestRunDispatcherMinimumVersionCheck_WhenMinimumReleasePredatesDirectorySplit_FallsBackToLegacyPath(t *testing.T) {
	result := runDispatcherMinimumVersionCheckCase(t, dispatcherMinimumVersionCase{
		currentProjectRunnerVersion: "3.0.0-beta.40",
		currentDispatcherVersion:    "1.0.1",
		minimumDispatcherVersion:    "1.0.0",
		legacyReleaseContract:       `{"dispatcherVersion":"1.0.0"}`,
	})

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertDispatcherMinimumVersionLogContains(t, result.stdout, "Dispatcher minimum version guard passed.")
	assertDispatcherMinimumVersionLogContains(t, result.gitLog, "dispatcher-v1.0.0:"+dispatcherContractFile)
	assertDispatcherMinimumVersionLogContains(t, result.gitLog, "dispatcher-v1.0.0:"+cliDispatcherRootContractFile)
	assertDispatcherMinimumVersionLogContains(t, result.gitLog, "dispatcher-v1.0.0:"+rootModulesDispatcherContractFile)
	assertDispatcherMinimumVersionLogContains(t, result.gitLog, "dispatcher-v1.0.0:"+legacyDispatcherContractFile)
}

// Verifies committed pin files cannot drift from the C# minimum dispatcher version.
func TestRunDispatcherMinimumVersionCheck_WhenProjectPinDiffersFromPackagePin_Fails(t *testing.T) {
	result := runDispatcherMinimumVersionCheckCase(t, dispatcherMinimumVersionCase{
		currentProjectRunnerVersion:        "3.0.0-beta.40",
		currentDispatcherVersion:           "1.0.0",
		minimumDispatcherVersion:           "1.0.0",
		projectPinMinimumDispatcherVersion: "0.9.0",
	})

	if result.exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", result.exitCode, result.stdout)
	}
	assertDispatcherMinimumVersionLogContains(t, result.stderr, ".uloop/project-runner-pin.json minimumDispatcherVersion")
}

type dispatcherMinimumVersionCase struct {
	currentProjectRunnerVersion        string
	currentDispatcherVersion           string
	minimumDispatcherVersion           string
	projectPinMinimumDispatcherVersion string
	releaseContract                    string
	previousReleaseContract            string
	middleReleaseContract              string
	legacyReleaseContract              string
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
	if testCase.previousReleaseContract != "" {
		previousReleaseContractPath := filepath.Join(workDir, "previous-release-contract.json")
		writeFile(t, previousReleaseContractPath, testCase.previousReleaseContract)
		t.Setenv("GIT_PREVIOUS_RELEASE_CONTRACT", previousReleaseContractPath)
	}
	if testCase.middleReleaseContract != "" {
		middleReleaseContractPath := filepath.Join(workDir, "middle-release-contract.json")
		writeFile(t, middleReleaseContractPath, testCase.middleReleaseContract)
		t.Setenv("GIT_MIDDLE_RELEASE_CONTRACT", middleReleaseContractPath)
	}
	if testCase.legacyReleaseContract != "" {
		legacyReleaseContractPath := filepath.Join(workDir, "legacy-release-contract.json")
		writeFile(t, legacyReleaseContractPath, testCase.legacyReleaseContract)
		t.Setenv("GIT_LEGACY_RELEASE_CONTRACT", legacyReleaseContractPath)
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
	currentDispatcherVersion := testCase.currentDispatcherVersion
	if currentDispatcherVersion == "" {
		currentDispatcherVersion = "1.0.0"
	}

	writeDispatcherMinimumVersionFile(t, filepath.Join(workDir, cliContractFile), buildDispatcherMinimumVersionCliContract(
		testCase.currentProjectRunnerVersion))
	writeDispatcherMinimumVersionFile(t, filepath.Join(workDir, dispatcherContractFile), buildDispatcherMinimumVersionContract(
		currentDispatcherVersion))
	writeDispatcherMinimumVersionFile(t, filepath.Join(workDir, protocolMinimumVersionFile), buildDispatcherMinimumVersionConstants(
		2,
		testCase.currentProjectRunnerVersion,
		testCase.minimumDispatcherVersion))
	writeDispatcherMinimumVersionFile(t, filepath.Join(workDir, unityPackageCliPinFile), buildDispatcherMinimumVersionPin(
		testCase.currentProjectRunnerVersion,
		testCase.minimumDispatcherVersion))
	writeDispatcherMinimumVersionFile(t, filepath.Join(workDir, unityProjectCliPinFile), buildDispatcherMinimumVersionPin(
		testCase.currentProjectRunnerVersion,
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

func buildDispatcherMinimumVersionCliContract(projectRunnerVersion string) string {
	return `{"protocolVersion":2,"projectRunnerVersion":"` + projectRunnerVersion + `"}`
}

func buildDispatcherMinimumVersionContract(dispatcherVersion string) string {
	return `{"dispatcherVersion":"` + dispatcherVersion + `"}`
}

func buildDispatcherMinimumVersionConstants(
	requiredProtocolVersion int,
	minimumProjectRunnerVersion string,
	minimumDispatcherVersion string,
) string {
	return `namespace Tests {
public static class CliConstants {
public const int REQUIRED_CLI_PROTOCOL_VERSION = ` +
		strconv.Itoa(requiredProtocolVersion) +
		`;
public const string MINIMUM_REQUIRED_PROJECT_RUNNER_VERSION = "` + minimumProjectRunnerVersion + `";
public const string MINIMUM_REQUIRED_DISPATCHER_VERSION = "` + minimumDispatcherVersion + `";
}
}`
}

func buildDispatcherMinimumVersionPin(projectRunnerVersion string, minimumDispatcherVersion string) string {
	return `{"projectRunnerVersion":"` +
		projectRunnerVersion +
		`","minimumDispatcherVersion":"` +
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
    dispatcher-v*:cli/dispatcher/dispatchercontract/dispatcher-contract.json)
      if [ -n "${GIT_RELEASE_CONTRACT:-}" ]; then
        cat "$GIT_RELEASE_CONTRACT"
      else
        echo "fatal: path 'cli/dispatcher/dispatchercontract/dispatcher-contract.json' exists on disk, but not in '$2'" >&2
        exit 1
      fi
      ;;
    dispatcher-v*:cli/dispatcher/dispatcher-contract.json)
      if [ -n "${GIT_PREVIOUS_RELEASE_CONTRACT:-}" ]; then
        cat "$GIT_PREVIOUS_RELEASE_CONTRACT"
      else
        echo "previous release not found" >&2
        exit 1
      fi
      ;;
    dispatcher-v*:dispatcher/dispatcher-contract.json)
      if [ -n "${GIT_MIDDLE_RELEASE_CONTRACT:-}" ]; then
        cat "$GIT_MIDDLE_RELEASE_CONTRACT"
      else
        echo "middle release not found" >&2
        exit 1
      fi
      ;;
    dispatcher-v*:cli/dispatcher-contract.json)
      if [ -n "${GIT_LEGACY_RELEASE_CONTRACT:-}" ]; then
        cat "$GIT_LEGACY_RELEASE_CONTRACT"
      else
        echo "release not found" >&2
        exit 1
      fi
      ;;
    *) echo "unexpected git show ref: $2" >&2; exit 1 ;;
  esac
  exit 0
fi

if [ "$1" = "cat-file" ] && [ "$2" = "-e" ]; then
  case "$3" in
    dispatcher-v*:cli/dispatcher/dispatchercontract/dispatcher-contract.json)
      [ -n "${GIT_RELEASE_CONTRACT:-}" ] && exit 0
      exit 1
      ;;
    dispatcher-v*:cli/dispatcher/dispatcher-contract.json)
      [ -n "${GIT_PREVIOUS_RELEASE_CONTRACT:-}" ] && exit 0
      exit 1
      ;;
    dispatcher-v*:dispatcher/dispatcher-contract.json)
      [ -n "${GIT_MIDDLE_RELEASE_CONTRACT:-}" ] && exit 0
      exit 1
      ;;
    dispatcher-v*:cli/dispatcher-contract.json)
      [ -n "${GIT_LEGACY_RELEASE_CONTRACT:-}" ] && exit 0
      exit 1
      ;;
    *) echo "unexpected cat-file target: $3" >&2; exit 1 ;;
  esac
fi

if [ "$1" = "rev-parse" ] && [ "$2" = "--verify" ]; then
  # Dispatcher release refs used in these tests are always considered
  # resolvable; the release publishing tests set up the associated fixtures.
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
