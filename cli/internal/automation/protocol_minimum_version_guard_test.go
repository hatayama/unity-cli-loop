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

func TestAnalyzeProtocolMinimumVersionGuard_WhenProtocolChangesWithoutMinimumVersionChange_Warns(t *testing.T) {
	// Verifies protocol bumps cannot leave the installer target on the old CLI release.
	result := AnalyzeProtocolMinimumVersionGuard(
		ProtocolMinimumVersionValues{
			RequiredProtocolVersion: 1,
			HasRequiredProtocol:     true,
			MinimumCliVersion:       "3.0.0-beta.32",
		},
		ProtocolMinimumVersionValues{
			RequiredProtocolVersion: 2,
			HasRequiredProtocol:     true,
			MinimumCliVersion:       "3.0.0-beta.32",
		})

	if !result.NeedsMinimumVersionUpdate {
		t.Fatalf("expected warning: %#v", result)
	}
}

func TestAnalyzeProtocolMinimumVersionGuard_WhenProtocolAndMinimumVersionChange_DoesNotWarn(t *testing.T) {
	// Verifies paired protocol and installer target updates clear the update-omission warning.
	result := AnalyzeProtocolMinimumVersionGuard(
		ProtocolMinimumVersionValues{
			RequiredProtocolVersion: 1,
			HasRequiredProtocol:     true,
			MinimumCliVersion:       "3.0.0-beta.32",
		},
		ProtocolMinimumVersionValues{
			RequiredProtocolVersion: 2,
			HasRequiredProtocol:     true,
			MinimumCliVersion:       "3.0.0-beta.33",
		})

	if result.NeedsMinimumVersionUpdate {
		t.Fatalf("unexpected warning: %#v", result)
	}
}

func TestAnalyzeProtocolMinimumVersionGuard_WhenProtocolDoesNotChange_DoesNotWarn(t *testing.T) {
	// Verifies ordinary package edits do not force a CLI installer target bump.
	result := AnalyzeProtocolMinimumVersionGuard(
		ProtocolMinimumVersionValues{
			RequiredProtocolVersion: 2,
			HasRequiredProtocol:     true,
			MinimumCliVersion:       "3.0.0-beta.32",
		},
		ProtocolMinimumVersionValues{
			RequiredProtocolVersion: 2,
			HasRequiredProtocol:     true,
			MinimumCliVersion:       "3.0.0-beta.32",
		})

	if result.NeedsMinimumVersionUpdate {
		t.Fatalf("unexpected warning: %#v", result)
	}
}

func TestRunProtocolMinimumVersionGuard_WhenMinimumReleaseMatches_Passes(t *testing.T) {
	// Verifies protocol bump PRs pass only after the selected CLI release advertises the new protocol.
	result := runProtocolMinimumVersionGuardCase(t, protocolMinimumVersionRefCase{
		baseContent:    buildProtocolMinimumVersionConstants(1, "3.0.0-beta.32"),
		headContent:    buildProtocolMinimumVersionConstants(2, "3.0.0-beta.33"),
		releaseContent: `{"schemaVersion":1,"protocolVersion":2,"cliVersion":"3.0.0-beta.33"}`,
	})

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertProtocolMinimumVersionLogContains(t, result.stdout, "Protocol minimum version guard passed.")
	assertProtocolMinimumVersionLogContains(t, result.gitLog, "cli-v3.0.0-beta.33:cli/contract.json")
}

func TestRunProtocolMinimumVersionGuard_WhenMinimumReleaseProtocolDiffers_Fails(t *testing.T) {
	// Verifies changing the minimum version text is not enough when the release uses the old protocol.
	result := runProtocolMinimumVersionGuardCase(t, protocolMinimumVersionRefCase{
		baseContent:    buildProtocolMinimumVersionConstants(1, "3.0.0-beta.32"),
		headContent:    buildProtocolMinimumVersionConstants(2, "3.0.0-beta.33"),
		releaseContent: `{"schemaVersion":1,"protocolVersion":1,"cliVersion":"3.0.0-beta.33"}`,
	})

	if result.exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", result.exitCode, result.stdout)
	}
	assertProtocolMinimumVersionLogContains(t, result.stderr, "does not point to a published CLI release")
	assertProtocolMinimumVersionLogContains(t, result.stderr, "advertises protocol 1")
}

func TestRunProtocolMinimumVersionGuard_WhenMinimumReleaseIsDraft_Fails(t *testing.T) {
	// Verifies protocol bump PRs wait for a published CLI release, not only a git tag.
	result := runProtocolMinimumVersionGuardCase(t, protocolMinimumVersionRefCase{
		baseContent:    buildProtocolMinimumVersionConstants(1, "3.0.0-beta.32"),
		headContent:    buildProtocolMinimumVersionConstants(2, "3.0.0-beta.33"),
		releaseContent: `{"schemaVersion":1,"protocolVersion":2,"cliVersion":"3.0.0-beta.33"}`,
		releaseView:    `{"isDraft":true,"assets":[]}`,
	})

	if result.exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", result.exitCode, result.stdout)
	}
	assertProtocolMinimumVersionLogContains(t, result.stderr, "does not point to a published CLI release")
	assertProtocolMinimumVersionLogContains(t, result.stderr, "is still draft")
}

func TestRunProtocolMinimumVersionGuard_WhenMinimumReleaseAssetsAreMissing_Fails(t *testing.T) {
	// Verifies protocol bump PRs wait for installable native CLI release assets.
	result := runProtocolMinimumVersionGuardCase(t, protocolMinimumVersionRefCase{
		baseContent:    buildProtocolMinimumVersionConstants(1, "3.0.0-beta.32"),
		headContent:    buildProtocolMinimumVersionConstants(2, "3.0.0-beta.33"),
		releaseContent: `{"schemaVersion":1,"protocolVersion":2,"cliVersion":"3.0.0-beta.33"}`,
		releaseView:    `{"isDraft":false,"assets":[]}`,
	})

	if result.exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", result.exitCode, result.stdout)
	}
	assertProtocolMinimumVersionLogContains(t, result.stderr, "does not point to a published CLI release")
	assertProtocolMinimumVersionLogContains(t, result.stderr, "is missing release asset")
}

func TestRunProtocolMinimumVersionGuard_WhenOnlyMinimumReleaseProtocolDiffers_Fails(t *testing.T) {
	// Verifies installer target changes are validated even without a protocol declaration bump.
	result := runProtocolMinimumVersionGuardCase(t, protocolMinimumVersionRefCase{
		baseContent:    buildProtocolMinimumVersionConstants(2, "3.0.0-beta.32"),
		headContent:    buildProtocolMinimumVersionConstants(2, "3.0.0-beta.33"),
		releaseContent: `{"schemaVersion":1,"protocolVersion":1,"cliVersion":"3.0.0-beta.33"}`,
	})

	if result.exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", result.exitCode, result.stdout)
	}
	assertProtocolMinimumVersionLogContains(t, result.stderr, "`MINIMUM_REQUIRED_CLI_VERSION` changed")
	assertProtocolMinimumVersionLogContains(t, result.stderr, "advertises protocol 1")
}

func TestVerifyMinimumCliReleaseProtocol_WhenTagProtocolMatches_Passes(t *testing.T) {
	// Verifies release validation accepts a CLI tag that advertises the required protocol.
	err := VerifyMinimumCliReleaseProtocol(ProtocolMinimumVersionValues{
		RequiredProtocolVersion: 2,
		HasRequiredProtocol:     true,
		MinimumCliVersion:       "3.0.0-beta.33",
	}, []byte(`{"schemaVersion":1,"protocolVersion":2,"cliVersion":"3.0.0-beta.33"}`))
	if err != nil {
		t.Fatalf("expected matching release protocol, got %v", err)
	}
}

func TestVerifyMinimumCliReleaseProtocol_WhenTagProtocolIsMissing_Fails(t *testing.T) {
	// Verifies release validation rejects CLI tags that predate protocol metadata.
	err := VerifyMinimumCliReleaseProtocol(ProtocolMinimumVersionValues{
		RequiredProtocolVersion: 2,
		HasRequiredProtocol:     true,
		MinimumCliVersion:       "3.0.0-beta.32",
	}, []byte(`{"schemaVersion":1,"cliVersion":"3.0.0-beta.32"}`))

	if err == nil {
		t.Fatal("expected missing protocolVersion to fail")
	}
	if !strings.Contains(err.Error(), "protocolVersion") {
		t.Fatalf("expected protocolVersion error, got %v", err)
	}
}

func TestVerifyMinimumCliReleaseProtocol_WhenTagProtocolDiffers_Fails(t *testing.T) {
	// Verifies release validation rejects published CLIs from a different protocol generation.
	err := VerifyMinimumCliReleaseProtocol(ProtocolMinimumVersionValues{
		RequiredProtocolVersion: 3,
		HasRequiredProtocol:     true,
		MinimumCliVersion:       "3.0.0-beta.33",
	}, []byte(`{"schemaVersion":1,"protocolVersion":2,"cliVersion":"3.0.0-beta.33"}`))

	if err == nil {
		t.Fatal("expected mismatched protocolVersion to fail")
	}
	if !strings.Contains(err.Error(), "requires protocol 3") {
		t.Fatalf("expected mismatch error, got %v", err)
	}
}

func TestRunMinimumCliReleaseProtocolCheck_WhenRefIsProvided_ReadsValuesAtRef(t *testing.T) {
	// Verifies release backfill checks validate the release commit instead of the current checkout.
	workDir := t.TempDir()
	mockBin := filepath.Join(workDir, "bin")
	err := os.MkdirAll(mockBin, 0o755)
	if err != nil {
		t.Fatalf("failed to create mock bin: %v", err)
	}

	gitLogPath := filepath.Join(workDir, "git.log")
	ghLogPath := filepath.Join(workDir, "gh.log")
	writeProtocolMinimumVersionMockGit(t, filepath.Join(mockBin, "git"))
	writeProtocolMinimumVersionMockGH(t, filepath.Join(mockBin, "gh"))
	prepareProtocolMinimumVersionGitContents(t, workDir, protocolMinimumVersionRefCase{
		baseContent:    buildProtocolMinimumVersionConstants(1, "3.0.0-beta.32"),
		headContent:    buildProtocolMinimumVersionConstants(2, "3.0.0-beta.33"),
		releaseContent: `{"schemaVersion":1,"protocolVersion":2,"cliVersion":"3.0.0-beta.33"}`,
	})

	t.Setenv("PATH", mockBin+string(os.PathListSeparator)+os.Getenv("PATH"))
	t.Setenv("ULOOP_REPOSITORY_ROOT", workDir)
	t.Setenv("GIT_LOG", gitLogPath)
	t.Setenv("GH_LOG", ghLogPath)

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := RunMinimumCliReleaseProtocolCheck(
		context.Background(),
		&stdout,
		&stderr,
		"protocol-release")

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr.String())
	}
	assertProtocolMinimumVersionLogContains(t, stdout.String(), "Minimum CLI release cli-v3.0.0-beta.33 advertises protocol 2.")
	assertProtocolMinimumVersionLogContains(t, readFile(t, gitLogPath), "protocol-release:"+protocolMinimumVersionFile)
	assertProtocolMinimumVersionLogContains(t, readFile(t, ghLogPath), "release view cli-v3.0.0-beta.33")
}

func TestRunProtocolMinimumVersionComment_WhenWarningExists_UpsertsComment(t *testing.T) {
	// Verifies PR comments explain protocol bump installer target omissions.
	result := runProtocolMinimumVersionCommentCase(t, protocolMinimumVersionCommentCase{
		baseContent: buildProtocolMinimumVersionConstants(1, "3.0.0-beta.32"),
		headContent: buildProtocolMinimumVersionConstants(2, "3.0.0-beta.32"),
		commentIDs:  "123",
	})

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertProtocolMinimumVersionLogContains(t, result.stdout, "Updated protocol minimum version comment.")
	assertProtocolMinimumVersionLogContains(t, result.ghLog, "api --method PATCH repos/owner/repository/issues/comments/123 --input")
}

func TestRunProtocolMinimumVersionComment_WhenWarningIsResolved_DeletesComment(t *testing.T) {
	// Verifies stale protocol minimum comments are removed after the PR is fixed.
	result := runProtocolMinimumVersionCommentCase(t, protocolMinimumVersionCommentCase{
		baseContent:    buildProtocolMinimumVersionConstants(1, "3.0.0-beta.32"),
		headContent:    buildProtocolMinimumVersionConstants(2, "3.0.0-beta.33"),
		releaseContent: `{"schemaVersion":1,"protocolVersion":2,"cliVersion":"3.0.0-beta.33"}`,
		commentIDs:     "123",
	})

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertProtocolMinimumVersionLogContains(t, result.stdout, "Deleted resolved protocol minimum version comment.")
	assertProtocolMinimumVersionLogContains(t, result.ghLog, "api --method DELETE repos/owner/repository/issues/comments/123")
}

func TestRunProtocolMinimumVersionComment_WhenMinimumReleaseProtocolDiffers_UpsertsComment(t *testing.T) {
	// Verifies stale protocol minimum comments stay open until the selected release protocol matches.
	result := runProtocolMinimumVersionCommentCase(t, protocolMinimumVersionCommentCase{
		baseContent:    buildProtocolMinimumVersionConstants(1, "3.0.0-beta.32"),
		headContent:    buildProtocolMinimumVersionConstants(2, "3.0.0-beta.33"),
		releaseContent: `{"schemaVersion":1,"protocolVersion":1,"cliVersion":"3.0.0-beta.33"}`,
		commentIDs:     "123",
	})

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertProtocolMinimumVersionLogContains(t, result.stdout, "Updated protocol minimum version comment.")
	assertProtocolMinimumVersionLogContains(t, result.ghLog, "api --method PATCH repos/owner/repository/issues/comments/123 --input")
}

type protocolMinimumVersionRefCase struct {
	baseContent    string
	headContent    string
	releaseContent string
	releaseView    string
}

type protocolMinimumVersionGuardRunResult struct {
	exitCode int
	stdout   string
	stderr   string
	gitLog   string
}

func runProtocolMinimumVersionGuardCase(t *testing.T, testCase protocolMinimumVersionRefCase) protocolMinimumVersionGuardRunResult {
	t.Helper()

	workDir := t.TempDir()
	mockBin := filepath.Join(workDir, "bin")
	err := os.MkdirAll(mockBin, 0o755)
	if err != nil {
		t.Fatalf("failed to create mock bin: %v", err)
	}

	gitLogPath := filepath.Join(workDir, "git.log")
	ghLogPath := filepath.Join(workDir, "gh.log")
	writeProtocolMinimumVersionMockGit(t, filepath.Join(mockBin, "git"))
	writeProtocolMinimumVersionMockGH(t, filepath.Join(mockBin, "gh"))
	prepareProtocolMinimumVersionGitContents(t, workDir, testCase)

	t.Setenv("PATH", mockBin+string(os.PathListSeparator)+os.Getenv("PATH"))
	t.Setenv("ULOOP_REPOSITORY_ROOT", workDir)
	t.Setenv("GIT_LOG", gitLogPath)
	t.Setenv("GH_LOG", ghLogPath)
	if testCase.releaseView != "" {
		t.Setenv("GH_RELEASE_VIEW", testCase.releaseView)
	}

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := RunProtocolMinimumVersionGuard(
		context.Background(),
		&stdout,
		&stderr,
		ProtocolMinimumVersionGuardConfig{
			BaseRef: "origin/v3-beta",
			HeadRef: "protocol-pr-head",
		})

	return protocolMinimumVersionGuardRunResult{
		exitCode: exitCode,
		stdout:   stdout.String(),
		stderr:   stderr.String(),
		gitLog:   readFile(t, gitLogPath),
	}
}

type protocolMinimumVersionCommentCase struct {
	baseContent    string
	headContent    string
	releaseContent string
	commentIDs     string
}

type protocolMinimumVersionCommentResult struct {
	exitCode int
	stdout   string
	stderr   string
	ghLog    string
}

func runProtocolMinimumVersionCommentCase(t *testing.T, testCase protocolMinimumVersionCommentCase) protocolMinimumVersionCommentResult {
	t.Helper()

	workDir := t.TempDir()
	mockBin := filepath.Join(workDir, "bin")
	err := os.MkdirAll(mockBin, 0o755)
	if err != nil {
		t.Fatalf("failed to create mock bin: %v", err)
	}

	gitLogPath := filepath.Join(workDir, "git.log")
	ghLogPath := filepath.Join(workDir, "gh.log")
	writeProtocolMinimumVersionMockGit(t, filepath.Join(mockBin, "git"))
	writeProtocolMinimumVersionMockGH(t, filepath.Join(mockBin, "gh"))
	prepareProtocolMinimumVersionGitContents(t, workDir, protocolMinimumVersionRefCase{
		baseContent:    testCase.baseContent,
		headContent:    testCase.headContent,
		releaseContent: testCase.releaseContent,
	})

	t.Setenv("PATH", mockBin+string(os.PathListSeparator)+os.Getenv("PATH"))
	t.Setenv("ULOOP_REPOSITORY_ROOT", workDir)
	t.Setenv("PR_NUMBER", "456")
	t.Setenv("GITHUB_REPOSITORY", "owner/repository")
	t.Setenv("GITHUB_BASE_REF", "v3-beta")
	t.Setenv("PROTOCOL_MINIMUM_VERSION_HEAD_REF", "protocol-pr-head")
	t.Setenv("GIT_LOG", gitLogPath)
	t.Setenv("GH_LOG", ghLogPath)
	t.Setenv("GH_COMMENT_IDS", testCase.commentIDs)

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := RunProtocolMinimumVersionComment(context.Background(), &stdout, &stderr)
	ghLog := readFile(t, ghLogPath)

	return protocolMinimumVersionCommentResult{
		exitCode: exitCode,
		stdout:   stdout.String(),
		stderr:   stderr.String(),
		ghLog:    ghLog,
	}
}

func prepareProtocolMinimumVersionGitContents(t *testing.T, workDir string, testCase protocolMinimumVersionRefCase) {
	t.Helper()

	baseContentPath := filepath.Join(workDir, "base.cs")
	headContentPath := filepath.Join(workDir, "head.cs")
	releaseContentPath := filepath.Join(workDir, "release-contract.json")
	writeFile(t, baseContentPath, testCase.baseContent)
	writeFile(t, headContentPath, testCase.headContent)
	if testCase.releaseContent != "" {
		writeFile(t, releaseContentPath, testCase.releaseContent)
		t.Setenv("GIT_RELEASE_CONTENT", releaseContentPath)
	}
	t.Setenv("GIT_BASE_CONTENT", baseContentPath)
	t.Setenv("GIT_HEAD_CONTENT", headContentPath)
}

func buildProtocolMinimumVersionConstants(requiredProtocolVersion int, minimumCliVersion string) string {
	return `namespace Tests {
public static class CliConstants {
public const int REQUIRED_CLI_PROTOCOL_VERSION = ` +
		strconv.Itoa(requiredProtocolVersion) +
		`;
public const string MINIMUM_REQUIRED_CLI_VERSION = "` + minimumCliVersion + `";
}
}`
}

func writeProtocolMinimumVersionMockGit(t *testing.T, path string) {
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
    origin/v3-beta:*) cat "$GIT_BASE_CONTENT" ;;
    protocol-pr-head:*) cat "$GIT_HEAD_CONTENT" ;;
    protocol-release:*) cat "$GIT_HEAD_CONTENT" ;;
    cli-v*:cli/contract.json)
      if [ -n "${GIT_RELEASE_CONTENT:-}" ]; then
        cat "$GIT_RELEASE_CONTENT"
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

func writeProtocolMinimumVersionMockGH(t *testing.T, path string) {
	t.Helper()

	content := `#!/bin/sh
set -eu

printf '%s\n' "$*" >> "$GH_LOG"

if [ "$1" = "release" ] && [ "$2" = "view" ]; then
  if [ -n "${GH_RELEASE_VIEW:-}" ]; then
    printf '%s\n' "$GH_RELEASE_VIEW"
  else
    printf '%s\n' '{"isDraft":false,"assets":[{"name":"install.sh","size":1},{"name":"install.ps1","size":1},{"name":"uloop-darwin-amd64.tar.gz","size":1},{"name":"uloop-darwin-amd64.tar.gz.sha256","size":1},{"name":"uloop-darwin-arm64.tar.gz","size":1},{"name":"uloop-darwin-arm64.tar.gz.sha256","size":1},{"name":"uloop-windows-amd64.zip","size":1},{"name":"uloop-windows-amd64.zip.sha256","size":1}]}'
  fi
  exit 0
fi

if [ "$1" = "api" ] && [ "$2" = "--paginate" ]; then
  if [ -n "$GH_COMMENT_IDS" ]; then
    printf '%s\n' "$GH_COMMENT_IDS"
  fi
  exit 0
fi

if [ "$1" = "api" ] && [ "$2" = "--method" ]; then
  exit 0
fi

echo "unexpected gh command: $*" >&2
exit 1
`
	writeFile(t, path, content)
	err := os.Chmod(path, 0o755)
	if err != nil {
		t.Fatalf("failed to chmod mock gh: %v", err)
	}
}

func writeFile(t *testing.T, path string, content string) {
	t.Helper()
	err := os.WriteFile(path, []byte(content), 0o755)
	if err != nil {
		t.Fatalf("failed to write %s: %v", path, err)
	}
}

func readFile(t *testing.T, path string) string {
	t.Helper()
	content, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read %s: %v", path, err)
	}
	return string(content)
}

func assertProtocolMinimumVersionLogContains(t *testing.T, actual string, expected string) {
	t.Helper()
	if !strings.Contains(actual, expected) {
		t.Fatalf("expected log to contain %q, got:\n%s", expected, actual)
	}
}
