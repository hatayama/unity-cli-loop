package automation

import (
	"bytes"
	"context"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
	"time"
)

type releasePRCheckRunResult struct {
	exitCode int
	stdout   string
	stderr   string
	ghLog    string
	gitLog   string
	sleeps   []time.Duration
}

// Verifies that a plain root package release summary is labeled as the Unity package.
func TestReleasePRCheckUnityPackageSummaryIsClarified(t *testing.T) {
	body := "## Releases\n" +
		"<details><summary>3.0.0-beta.46</summary>\n" +
		"\n" +
		"* Unity package notes\n" +
		"</details>\n" +
		"<details><summary>uloop-project-runner: 3.0.0-beta.44</summary>\n" +
		"\n" +
		"* Project runner notes\n" +
		"</details>\n"

	clarifiedBody, changed := clarifyReleasePRCheckUnityPackageSummary(body)

	if !changed {
		t.Fatal("expected plain Unity package summary to change")
	}
	assertReleasePRCheckLogContains(t, clarifiedBody, "<details><summary>unity-package: 3.0.0-beta.46</summary>")
	assertReleasePRCheckLogContains(t, clarifiedBody, "<details><summary>uloop-project-runner: 3.0.0-beta.44</summary>")
}

// Verifies that already labeled release summaries are left unchanged.
func TestReleasePRCheckUnityPackageSummaryAlreadyClarified(t *testing.T) {
	body := "<details><summary>unity-package: 3.0.0-beta.46</summary>\n</details>\n"

	clarifiedBody, changed := clarifyReleasePRCheckUnityPackageSummary(body)

	if changed {
		t.Fatal("expected labeled Unity package summary to stay unchanged")
	}
	if clarifiedBody != body {
		t.Fatalf("expected body to stay unchanged, got:\n%s", clarifiedBody)
	}
}

// Verifies that missing release PRs skip without dispatching checks.
func TestReleasePRChecksSkipWhenNoReleasePRExists(t *testing.T) {
	result := runReleasePRCheckCase(t, releasePRCheckCase{
		prListJSON:     "[]",
		runListJSON:    "[]",
		runWatchStatus: "0",
	})

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	if result.stdout != "No pending release-please PR found for v3-beta.\n" {
		t.Fatalf("expected no-release skip message, got %q", result.stdout)
	}
	assertReleasePRCheckLogDoesNotContain(t, result.ghLog, "workflow run")
	assertReleasePRCheckLogDoesNotContain(t, result.ghLog, "pr ready")
}

// Verifies that the matching release PR is drafted, dispatched, watched, and marked ready.
func TestReleasePRChecksDispatchAndMarkReady(t *testing.T) {
	result := runReleasePRCheckCase(t, releasePRCheckCase{
		prListJSON:     `[{"number":1043,"headRefName":"release-please--branches--v3-beta","headRefOid":"abc123","title":"chore(v3-beta): release 3.0.0-beta.5","url":"https://example.test/pr/1043"}]`,
		runListJSON:    `[{"databaseId":4242,"headSha":"abc123","createdAt":"2026-05-30T01:00:01Z","status":"queued","conclusion":"","url":"https://example.test/run/4242"}]`,
		runWatchStatus: "0",
	})

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertReleasePRCheckLogContains(t, result.stdout, "Marked release PR #1043 as draft while checks run.")
	assertReleasePRCheckLogContains(t, result.stdout, "Dispatching build-and-test.yml for release PR #1043: https://example.test/pr/1043")
	assertReleasePRCheckLogContains(t, result.stdout, "Watching build-and-test.yml run 4242 for release PR #1043.")
	assertReleasePRCheckLogContains(t, result.stdout, "Marked release PR #1043 as ready after checks passed.")
	assertReleasePRCheckLogContains(t, result.ghLog, "pr ready 1043 --repo owner/repository --undo")
	assertReleasePRCheckLogContains(t, result.ghLog, "workflow run build-and-test.yml --repo owner/repository --ref release-please--branches--v3-beta")
	assertReleasePRCheckLogContains(t, result.ghLog, "run list --repo owner/repository --workflow build-and-test.yml --branch release-please--branches--v3-beta --event workflow_dispatch --json databaseId,status,conclusion,headSha,createdAt,url --limit 20")
	assertReleasePRCheckLogContains(t, result.ghLog, "run watch 4242 --repo owner/repository --exit-status --compact --interval 1")
	assertReleasePRCheckLogContainsLine(t, result.ghLog, "pr ready 1043 --repo owner/repository")
}

// Verifies that matching release PRs are updated when the body has a plain Unity package summary.
func TestReleasePRChecksClarifyUnityPackageSummaryBeforeDispatch(t *testing.T) {
	result := runReleasePRCheckCase(t, releasePRCheckCase{
		prListJSON:     `[{"number":1043,"headRefName":"release-please--branches--v3-beta","headRefOid":"abc123","title":"chore(v3-beta): release 3.0.0-beta.46","url":"https://example.test/pr/1043"}]`,
		prViewJSON:     `{"body":"<details><summary>3.0.0-beta.46</summary>\n</details>\n<details><summary>uloop-project-runner: 3.0.0-beta.44</summary>\n</details>\n"}`,
		runListJSON:    `[{"databaseId":4242,"headSha":"abc123","createdAt":"2026-05-30T01:00:01Z","status":"queued","conclusion":"","url":"https://example.test/run/4242"}]`,
		runWatchStatus: "0",
	})

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertReleasePRCheckLogContains(t, result.stdout, "Updated release PR #1043 body to label the Unity package summary.")
	assertReleasePRCheckLogContains(t, result.ghLog, "pr view 1043 --repo owner/repository --json body")
	assertReleasePRCheckLogContains(t, result.ghLog, "pr edit 1043 --repo owner/repository --body-file")
	assertReleasePRCheckLogContains(t, result.ghLog, "workflow run build-and-test.yml --repo owner/repository --ref release-please--branches--v3-beta")
}

// Verifies that same-second workflow runs are accepted when GitHub rounds createdAt timestamps.
func TestReleasePRChecksAcceptSameSecondRunAfterDispatch(t *testing.T) {
	result := runReleasePRCheckCase(t, releasePRCheckCase{
		prListJSON:     `[{"number":1043,"headRefName":"release-please--branches--v3-beta","headRefOid":"abc123","title":"chore(v3-beta): release 3.0.0-beta.5","url":"https://example.test/pr/1043"}]`,
		runListJSON:    `[{"databaseId":4242,"headSha":"abc123","createdAt":"2026-05-30T01:00:00Z","status":"queued","conclusion":"","url":"https://example.test/run/4242"}]`,
		runWatchStatus: "0",
		now:            time.Date(2026, 5, 30, 1, 0, 0, 500000000, time.UTC),
	})

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertReleasePRCheckLogContains(t, result.stdout, "Watching build-and-test.yml run 4242 for release PR #1043.")
	assertReleasePRCheckLogContainsLine(t, result.ghLog, "pr ready 1043 --repo owner/repository")
}

// Verifies that a stale PR head SHA before dispatch does not hide successful checks for the updated branch head.
func TestReleasePRChecksUseDispatchedRunHeadWhenPRListReturnsStaleHead(t *testing.T) {
	result := runReleasePRCheckCase(t, releasePRCheckCase{
		prListJSON:           `[{"number":1043,"headRefName":"release-please--branches--v3-beta","headRefOid":"release123","title":"chore: release v3-beta","url":"https://example.test/pr/1043"}]`,
		prListJSONAfterWatch: `[{"number":1043,"headRefName":"release-please--branches--v3-beta","headRefOid":"dist456","title":"chore: release v3-beta","url":"https://example.test/pr/1043"}]`,
		runListJSON:          `[{"databaseId":4242,"headSha":"dist456","createdAt":"2026-05-30T01:00:01Z","status":"queued","conclusion":"","url":"https://example.test/run/4242"}]`,
		runWatchStatus:       "0",
	})

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertReleasePRCheckLogContains(t, result.stdout, "Watching build-and-test.yml run 4242 for release PR #1043.")
	assertReleasePRCheckLogContainsLine(t, result.ghLog, "pr ready 1043 --repo owner/repository")
}

// Verifies that release-looking PRs are ignored without a release-please branch.
func TestReleasePRChecksIgnoreManualReleasePR(t *testing.T) {
	result := runReleasePRCheckCase(t, releasePRCheckCase{
		prListJSON:     `[{"number":1042,"headRefName":"feature/manual-release","headRefOid":"abc123","title":"chore(v3-beta): release 3.0.0-beta.5","url":"https://example.test/pr/1042"}]`,
		runListJSON:    "[]",
		runWatchStatus: "0",
	})

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertReleasePRCheckLogContains(t, result.stdout, "No pending release-please PR found for v3-beta.")
	assertReleasePRCheckLogDoesNotContain(t, result.ghLog, "workflow run")
}

// Verifies that missing dispatched runs fail while leaving the PR drafted.
func TestReleasePRChecksFailWhenDispatchedRunIsMissing(t *testing.T) {
	result := runReleasePRCheckCase(t, releasePRCheckCase{
		prListJSON:     `[{"number":1043,"headRefName":"release-please--branches--v3-beta","headRefOid":"abc123","title":"chore: release v3-beta","url":"https://example.test/pr/1043"}]`,
		runListJSON:    "[]",
		runWatchStatus: "0",
	})

	if result.exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", result.exitCode)
	}
	assertReleasePRCheckLogContains(t, result.stderr, "could not find dispatched build-and-test.yml workflow run for abc123")
	assertReleasePRCheckLogContains(t, result.ghLog, "pr ready 1043 --repo owner/repository --undo")
	assertReleasePRCheckLogDoesNotContainLine(t, result.ghLog, "pr ready 1043 --repo owner/repository")
	if len(result.sleeps) != 0 {
		t.Fatalf("expected no sleep after the final lookup attempt, got %v", result.sleeps)
	}
}

// Verifies that failed watched runs fail while leaving the PR drafted.
func TestReleasePRChecksFailWhenWatchedRunFails(t *testing.T) {
	result := runReleasePRCheckCase(t, releasePRCheckCase{
		prListJSON:     `[{"number":1043,"headRefName":"release-please--branches--v3-beta","headRefOid":"abc123","title":"chore: release v3-beta","url":"https://example.test/pr/1043"}]`,
		runListJSON:    `[{"databaseId":4242,"headSha":"abc123","createdAt":"2026-05-30T01:00:01Z","status":"completed","conclusion":"failure","url":"https://example.test/run/4242"}]`,
		runWatchStatus: "1",
	})

	if result.exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", result.exitCode)
	}
	assertReleasePRCheckLogContains(t, result.stderr, "gh run watch 4242 --repo owner/repository --exit-status --compact --interval 1 failed")
	assertReleasePRCheckLogContains(t, result.ghLog, "pr ready 1043 --repo owner/repository --undo")
	assertReleasePRCheckLogDoesNotContainLine(t, result.ghLog, "pr ready 1043 --repo owner/repository")
}

// Verifies that a changed release PR head is not marked ready after stale checks pass.
func TestReleasePRChecksFailWhenHeadChangesBeforeReady(t *testing.T) {
	result := runReleasePRCheckCase(t, releasePRCheckCase{
		prListJSON:           `[{"number":1043,"headRefName":"release-please--branches--v3-beta","headRefOid":"abc123","title":"chore: release v3-beta","url":"https://example.test/pr/1043"}]`,
		prListJSONAfterWatch: `[{"number":1043,"headRefName":"release-please--branches--v3-beta","headRefOid":"def456","title":"chore: release v3-beta","url":"https://example.test/pr/1043"}]`,
		runListJSON:          `[{"databaseId":4242,"headSha":"abc123","createdAt":"2026-05-30T01:00:01Z","status":"completed","conclusion":"success","url":"https://example.test/run/4242"}]`,
		runWatchStatus:       "0",
	})

	if result.exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", result.exitCode)
	}
	assertReleasePRCheckLogContains(t, result.stderr, "release PR #1043 head changed from abc123 to def456 before marking ready")
	assertReleasePRCheckLogContains(t, result.ghLog, "pr ready 1043 --repo owner/repository --undo")
	assertReleasePRCheckLogDoesNotContainLine(t, result.ghLog, "pr ready 1043 --repo owner/repository")
}

type releasePRCheckCase struct {
	prListJSON           string
	prListJSONAfterWatch string
	prViewJSON           string
	runListJSON          string
	runWatchStatus       string
	now                  time.Time
}

func runReleasePRCheckCase(t *testing.T, testCase releasePRCheckCase) releasePRCheckRunResult {
	t.Helper()

	workDir := t.TempDir()
	mockBin := filepath.Join(workDir, "bin")
	err := os.MkdirAll(mockBin, 0o755)
	if err != nil {
		t.Fatalf("failed to create mock bin: %v", err)
	}

	ghLogPath := filepath.Join(workDir, "gh.log")
	gitLogPath := filepath.Join(workDir, "git.log")
	prListCountPath := filepath.Join(workDir, "pr-list-count")
	writeReleasePRCheckMockGH(t, filepath.Join(mockBin, "gh"))
	writeReleasePRCheckMockGit(t, filepath.Join(mockBin, "git"))

	prViewJSON := testCase.prViewJSON
	if prViewJSON == "" {
		prViewJSON = `{"body":""}`
	}
	prListJSONAfterWatch := testCase.prListJSONAfterWatch
	if prListJSONAfterWatch == "" {
		prListJSONAfterWatch = testCase.prListJSON
	}

	t.Setenv("PATH", mockBin+string(os.PathListSeparator)+os.Getenv("PATH"))
	t.Setenv("GITHUB_REPOSITORY", "owner/repository")
	t.Setenv("TARGET_BRANCH", "v3-beta")
	t.Setenv("ULOOP_REPOSITORY_ROOT", workDir)
	t.Setenv("RELEASE_PR_CHECK_LOOKUP_ATTEMPTS", "1")
	t.Setenv("RELEASE_PR_CHECK_LOOKUP_INTERVAL_SECONDS", "1")
	t.Setenv("RELEASE_PR_CHECK_WATCH_INTERVAL_SECONDS", "1")
	t.Setenv("GH_PR_LIST_JSON", testCase.prListJSON)
	t.Setenv("GH_PR_LIST_JSON_AFTER_WATCH", prListJSONAfterWatch)
	t.Setenv("GH_PR_LIST_COUNT_PATH", prListCountPath)
	t.Setenv("GH_PR_VIEW_JSON", prViewJSON)
	t.Setenv("GH_RUN_LIST_JSON", testCase.runListJSON)
	t.Setenv("GH_RUN_WATCH_STATUS", testCase.runWatchStatus)
	t.Setenv("GH_LOG", ghLogPath)
	t.Setenv("GIT_LOG", gitLogPath)

	originalNow := releasePRCheckNow
	originalSleep := releasePRCheckSleep
	sleeps := []time.Duration{}
	now := testCase.now
	if now.IsZero() {
		now = time.Date(2026, 5, 30, 1, 0, 0, 0, time.UTC)
	}
	releasePRCheckNow = func() time.Time {
		return now
	}
	releasePRCheckSleep = func(duration time.Duration) {
		sleeps = append(sleeps, duration)
	}
	t.Cleanup(func() {
		releasePRCheckNow = originalNow
		releasePRCheckSleep = originalSleep
	})

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := RunReleasePleasePRChecks(context.Background(), &stdout, &stderr)
	ghLogBytes, err := os.ReadFile(ghLogPath)
	if err != nil {
		t.Fatalf("failed to read gh log: %v", err)
	}
	gitLog := readOptionalFile(t, gitLogPath)

	return releasePRCheckRunResult{
		exitCode: exitCode,
		stdout:   stdout.String(),
		stderr:   stderr.String(),
		ghLog:    string(ghLogBytes),
		gitLog:   gitLog,
		sleeps:   sleeps,
	}
}

func writeReleasePRCheckMockGH(t *testing.T, path string) {
	t.Helper()

	if runtime.GOOS == "windows" {
		writeReleasePRCheckMockGHBatch(t, path+".cmd")
		return
	}

	content := `#!/bin/sh
set -eu

printf '%s\n' "$*" >> "$GH_LOG"

if [ "$1" = "pr" ] && [ "$2" = "list" ]; then
  count=0
  if [ -f "$GH_PR_LIST_COUNT_PATH" ]; then
    count=$(cat "$GH_PR_LIST_COUNT_PATH")
  fi
  count=$((count + 1))
  printf '%s\n' "$count" > "$GH_PR_LIST_COUNT_PATH"

  if [ "$count" -gt 1 ] && [ -n "$GH_PR_LIST_JSON_AFTER_WATCH" ]; then
    printf '%s\n' "$GH_PR_LIST_JSON_AFTER_WATCH"
    exit 0
  fi

  printf '%s\n' "$GH_PR_LIST_JSON"
  exit 0
fi

if [ "$1" = "pr" ] && [ "$2" = "ready" ]; then
  exit 0
fi

if [ "$1" = "pr" ] && [ "$2" = "view" ]; then
  printf '%s\n' "$GH_PR_VIEW_JSON"
  exit 0
fi

if [ "$1" = "pr" ] && [ "$2" = "edit" ]; then
  exit 0
fi

if [ "$1" = "workflow" ] && [ "$2" = "run" ]; then
  exit 0
fi

if [ "$1" = "run" ] && [ "$2" = "list" ]; then
  printf '%s\n' "$GH_RUN_LIST_JSON"
  exit 0
fi

if [ "$1" = "run" ] && [ "$2" = "watch" ]; then
  exit "$GH_RUN_WATCH_STATUS"
fi

echo "unexpected gh command: $*" >&2
exit 1
`
	err := os.WriteFile(path, []byte(content), 0o755)
	if err != nil {
		t.Fatalf("failed to write mock gh command: %v", err)
	}
}

func writeReleasePRCheckMockGit(t *testing.T, path string) {
	t.Helper()

	if runtime.GOOS == "windows" {
		writeReleasePRCheckMockGitBatch(t, path+".cmd")
		return
	}

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

if [ "$1" = "fetch" ] && [ "$2" = "origin" ]; then
  exit 0
fi

if [ "$1" = "switch" ] && [ "$2" = "--detach" ] && [ "$3" = "FETCH_HEAD" ]; then
  exit 0
fi

if [ "$1" = "show" ]; then
  case "$2" in
    uloop-project-runner-v*:cli/contract.json)
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

if [ "$1" = "config" ]; then
  exit 0
fi

if [ "$1" = "add" ]; then
  exit 0
fi

if [ "$1" = "commit" ]; then
  exit 0
fi

if [ "$1" = "push" ]; then
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

func writeReleasePRCheckMockGHBatch(t *testing.T, path string) {
	t.Helper()
	content := `@echo off
setlocal EnableExtensions EnableDelayedExpansion
>>"%GH_LOG%" echo %*

if "%~1"=="pr" if "%~2"=="list" (
  set "count=0"
  if exist "%GH_PR_LIST_COUNT_PATH%" set /p count=<"%GH_PR_LIST_COUNT_PATH%"
  set /a count+=1
  >"%GH_PR_LIST_COUNT_PATH%" echo !count!
  if !count! GTR 1 (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "[Console]::Out.WriteLine($env:GH_PR_LIST_JSON_AFTER_WATCH)"
    exit /b 0
  )
  powershell -NoProfile -ExecutionPolicy Bypass -Command "[Console]::Out.WriteLine($env:GH_PR_LIST_JSON)"
  exit /b 0
)

if "%~1"=="pr" if "%~2"=="ready" exit /b 0

if "%~1"=="pr" if "%~2"=="view" (
  powershell -NoProfile -ExecutionPolicy Bypass -Command "[Console]::Out.WriteLine($env:GH_PR_VIEW_JSON)"
  exit /b 0
)

if "%~1"=="pr" if "%~2"=="edit" exit /b 0

if "%~1"=="workflow" if "%~2"=="run" exit /b 0

if "%~1"=="run" if "%~2"=="list" (
  powershell -NoProfile -ExecutionPolicy Bypass -Command "[Console]::Out.WriteLine($env:GH_RUN_LIST_JSON)"
  exit /b 0
)

if "%~1"=="run" if "%~2"=="watch" exit /b %GH_RUN_WATCH_STATUS%

echo unexpected gh command: %* 1>&2
exit /b 1
`
	writeFile(t, path, content)
}

func writeReleasePRCheckMockGitBatch(t *testing.T, path string) {
	t.Helper()
	content := `@echo off
setlocal EnableExtensions EnableDelayedExpansion
>>"%GIT_LOG%" echo %*

if "%~1"=="rev-parse" if "%~2"=="--show-toplevel" (
  powershell -NoProfile -ExecutionPolicy Bypass -Command "[Console]::Out.WriteLine($env:ULOOP_REPOSITORY_ROOT)"
  exit /b 0
)

if "%~1"=="-C" (
  shift
  shift
)

if "%~1"=="fetch" if "%~2"=="origin" exit /b 0
if "%~1"=="switch" if "%~2"=="--detach" if "%~3"=="FETCH_HEAD" exit /b 0

if "%~1"=="show" (
  echo %~2| findstr /r "^uloop-project-runner-v.*:cli/contract.json$" >nul
  if not errorlevel 1 (
    if not "%GIT_RELEASE_CONTRACT%"=="" (
      type "%GIT_RELEASE_CONTRACT%"
      exit /b 0
    )
    echo release not found 1>&2
    exit /b 1
  )
  echo unexpected git show ref: %~2 1>&2
  exit /b 1
)

if "%~1"=="config" exit /b 0
if "%~1"=="add" exit /b 0
if "%~1"=="commit" exit /b 0
if "%~1"=="push" exit /b 0

echo unexpected git command: %* 1>&2
exit /b 1
`
	writeFile(t, path, content)
}

func readOptionalFile(t *testing.T, path string) string {
	t.Helper()
	content, err := os.ReadFile(path)
	if os.IsNotExist(err) {
		return ""
	}
	if err != nil {
		t.Fatalf("failed to read %s: %v", path, err)
	}
	return string(content)
}

func assertReleasePRCheckLogContains(t *testing.T, actual string, expected string) {
	t.Helper()
	if !strings.Contains(actual, expected) {
		t.Fatalf("expected log to contain %q, got:\n%s", expected, actual)
	}
}

func assertReleasePRCheckLogDoesNotContain(t *testing.T, actual string, unexpected string) {
	t.Helper()
	if strings.Contains(actual, unexpected) {
		t.Fatalf("expected log not to contain %q, got:\n%s", unexpected, actual)
	}
}

func assertReleasePRCheckLogContainsLine(t *testing.T, actual string, expected string) {
	t.Helper()
	for _, line := range strings.Split(actual, "\n") {
		if strings.TrimSuffix(line, "\r") == expected {
			return
		}
	}
	t.Fatalf("expected log to contain line %q, got:\n%s", expected, actual)
}

func assertReleasePRCheckLogDoesNotContainLine(t *testing.T, actual string, unexpected string) {
	t.Helper()
	for _, line := range strings.Split(actual, "\n") {
		if strings.TrimSuffix(line, "\r") == unexpected {
			t.Fatalf("expected log not to contain line %q, got:\n%s", unexpected, actual)
		}
	}
}
