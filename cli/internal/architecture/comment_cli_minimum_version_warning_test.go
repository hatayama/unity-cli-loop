package architecture

import (
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

const (
	commentScriptPath  = "scripts/comment-cli-minimum-version-warning.sh"
	minimumVersionPath = "Packages/src/Editor/Domain/CliConstants.cs"
	goCliPath          = "cli/internal/cli/run.go"
)

type commentScriptResult struct {
	GitHubLog string
	BodyLog   string
	Output    string
}

type commentScriptOptions struct {
	ExistingCommentID string
	Mutation          string
	BaseRef           string
	HeadRef           string
	IncludePRNumber   bool
	FailOnWarning     bool
	ExpectFailure     bool
}

// Verifies that Go CLI changes without a minimum-version bump create or update the reminder comment.
func TestCommentCliMinimumVersionWarningWarnsForGoCliChange(t *testing.T) {
	result := runCommentScriptCase(t, commentScriptOptions{
		Mutation:        "go-cli",
		BaseRef:         "HEAD^",
		IncludePRNumber: true,
	})

	assertContains(t, result.GitHubLog, "POST repos/hatayama/unity-cli-loop/issues/123/comments")
	assertContains(t, result.BodyLog, "Go CLI files changed")
	assertContains(t, result.Output, "Posted CLI minimum version comment.")

	result = runCommentScriptCase(t, commentScriptOptions{
		ExistingCommentID: "456",
		Mutation:          "go-cli",
		BaseRef:           "HEAD^",
		IncludePRNumber:   true,
	})

	assertContains(t, result.GitHubLog, "PATCH repos/hatayama/unity-cli-loop/issues/comments/456")
	assertContains(t, result.BodyLog, "Go CLI files changed")
	assertNotContains(t, result.GitHubLog, "POST")
	assertContains(t, result.Output, "Updated CLI minimum version comment.")
}

// Verifies that CI check mode fails instead of only writing a reminder comment.
func TestCommentCliMinimumVersionWarningFailsCheckModeForGoCliChange(t *testing.T) {
	result := runCommentScriptCase(t, commentScriptOptions{
		Mutation:        "go-cli",
		BaseRef:         "HEAD^",
		IncludePRNumber: true,
		FailOnWarning:   true,
		ExpectFailure:   true,
	})

	assertContains(t, result.Output, "MINIMUM_REQUIRED_CLI_VERSION")
	assertNotContains(t, result.GitHubLog, "POST")
	assertNotContains(t, result.GitHubLog, "PATCH")
}

// Verifies that touching the constants file without changing the required CLI still fails check mode.
func TestCommentCliMinimumVersionWarningFailsCheckModeForUnchangedMinimumVersion(t *testing.T) {
	result := runCommentScriptCase(t, commentScriptOptions{
		Mutation:        "go-cli-and-unchanged-minimum",
		BaseRef:         "HEAD^",
		IncludePRNumber: true,
		FailOnWarning:   true,
		ExpectFailure:   true,
	})

	assertContains(t, result.Output, "MINIMUM_REQUIRED_CLI_VERSION")
	assertNotContains(t, result.GitHubLog, "POST")
	assertNotContains(t, result.GitHubLog, "PATCH")
}

// Verifies that pull_request_target can diff a fetched PR head without checking it out.
func TestCommentCliMinimumVersionWarningUsesExplicitHeadRef(t *testing.T) {
	workDir := t.TempDir()
	repositoryPath := createRepository(t, workDir)
	baseBranch := strings.TrimSpace(runCommand(t, repositoryPath, "git", "branch", "--show-current"))

	runCommand(t, repositoryPath, "git", "switch", "-q", "-c", "pr-head")
	writeFile(t, filepath.Join(repositoryPath, goCliPath), "package cli // changed")
	runCommand(t, repositoryPath, "git", "add", ".")
	runCommand(t, repositoryPath, "git", "commit", "-q", "-m", "go-cli")
	runCommand(t, repositoryPath, "git", "switch", "-q", baseBranch)

	result := runCommentScript(t, workDir, repositoryPath, commentScriptOptions{
		BaseRef:         "HEAD",
		HeadRef:         "pr-head",
		IncludePRNumber: true,
	})

	assertContains(t, result.GitHubLog, "POST repos/hatayama/unity-cli-loop/issues/123/comments")
	assertContains(t, result.BodyLog, "Go CLI files changed")
}

// Verifies that a matching minimum-version update resolves an existing reminder.
func TestCommentCliMinimumVersionWarningResolvesExistingReminder(t *testing.T) {
	result := runCommentScriptCase(t, commentScriptOptions{
		ExistingCommentID: "456",
		Mutation:          "go-cli-and-minimum",
		BaseRef:           "HEAD^",
		IncludePRNumber:   true,
	})

	assertContains(t, result.GitHubLog, "PATCH repos/hatayama/unity-cli-loop/issues/comments/456")
	assertContains(t, result.BodyLog, "Resolved:")
	assertContains(t, result.Output, "Resolved CLI minimum version comment.")
}

// Verifies that unrelated changes do not create a reminder comment.
func TestCommentCliMinimumVersionWarningIgnoresUnrelatedChanges(t *testing.T) {
	result := runCommentScriptCase(t, commentScriptOptions{
		Mutation:        "docs",
		BaseRef:         "HEAD^",
		IncludePRNumber: true,
	})

	assertNotContains(t, result.GitHubLog, "POST")
	assertNotContains(t, result.GitHubLog, "PATCH")
}

// Verifies that non-PR runs stop before calling GitHub issue APIs.
func TestCommentCliMinimumVersionWarningSkipsNonPullRequestRuns(t *testing.T) {
	result := runCommentScriptCase(t, commentScriptOptions{
		BaseRef: "HEAD^",
	})

	assertContains(t, result.Output, "no PR number")
	assertNotContains(t, result.GitHubLog, "repos/")
}

func runCommentScriptCase(t *testing.T, options commentScriptOptions) commentScriptResult {
	t.Helper()

	workDir := t.TempDir()
	repositoryPath := createRepository(t, workDir)

	switch options.Mutation {
	case "go-cli":
		writeFile(t, filepath.Join(repositoryPath, goCliPath), "package cli // changed")
	case "go-cli-and-minimum":
		writeFile(t, filepath.Join(repositoryPath, goCliPath), "package cli // changed")
		writeFile(t, filepath.Join(repositoryPath, minimumVersionPath), `public const string MINIMUM_REQUIRED_CLI_VERSION = "3.0.0-beta.15";`)
	case "go-cli-and-unchanged-minimum":
		writeFile(t, filepath.Join(repositoryPath, goCliPath), "package cli // changed")
		writeFile(t, filepath.Join(repositoryPath, minimumVersionPath), `public const string MINIMUM_REQUIRED_CLI_VERSION = "3.0.0-beta.14"; // touched`)
	case "docs":
		writeFile(t, filepath.Join(repositoryPath, "README.md"), "documentation")
	case "":
	default:
		t.Fatalf("unknown mutation: %s", options.Mutation)
	}

	if options.Mutation != "" {
		runCommand(t, repositoryPath, "git", "add", ".")
		runCommand(t, repositoryPath, "git", "commit", "-q", "-m", options.Mutation)
	}

	return runCommentScript(t, workDir, repositoryPath, options)
}

func runCommentScript(
	t *testing.T,
	workDir string,
	repositoryPath string,
	options commentScriptOptions,
) commentScriptResult {
	t.Helper()

	mockBinPath := writeMockGitHubCli(t, workDir)
	gitHubLogPath := filepath.Join(workDir, "gh.log")
	bodyLogPath := filepath.Join(workDir, "body.log")
	writeFile(t, gitHubLogPath, "")
	writeFile(t, bodyLogPath, "")

	command := exec.Command(filepath.Join(repositoryRoot(t), commentScriptPath))
	command.Dir = repositoryPath
	command.Env = append(os.Environ(),
		"PATH="+mockBinPath+string(os.PathListSeparator)+os.Getenv("PATH"),
		"GH_LOG="+gitHubLogPath,
		"GH_BODY_LOG="+bodyLogPath,
		"GH_EXISTING_COMMENT_ID="+options.ExistingCommentID,
		"ULOOP_REPOSITORY_ROOT="+repositoryPath,
		"GITHUB_REPOSITORY=hatayama/unity-cli-loop",
	)
	if options.IncludePRNumber {
		command.Env = append(command.Env, "PR_NUMBER=123")
	}
	if options.BaseRef != "" {
		command.Env = append(command.Env, "CLI_MINIMUM_VERSION_BASE_REF="+options.BaseRef)
	}
	if options.HeadRef != "" {
		command.Env = append(command.Env, "CLI_MINIMUM_VERSION_HEAD_REF="+options.HeadRef)
	}
	if options.FailOnWarning {
		command.Env = append(command.Env, "CLI_MINIMUM_VERSION_FAIL_ON_WARNING=true")
	}

	output, err := command.CombinedOutput()
	if options.ExpectFailure {
		if err == nil {
			t.Fatalf("expected comment script to fail, got success\n%s", string(output))
		}
	} else if err != nil {
		t.Fatalf("comment script failed: %v\n%s", err, string(output))
	}

	return commentScriptResult{
		GitHubLog: readFile(t, gitHubLogPath),
		BodyLog:   readFile(t, bodyLogPath),
		Output:    string(output),
	}
}

func createRepository(t *testing.T, workDir string) string {
	t.Helper()

	repositoryPath := filepath.Join(workDir, "repo")
	runCommand(t, workDir, "git", "init", "-q", repositoryPath)
	runCommand(t, repositoryPath, "git", "config", "user.email", "test@example.invalid")
	runCommand(t, repositoryPath, "git", "config", "user.name", "Test User")
	writeFile(t, filepath.Join(repositoryPath, goCliPath), "package cli")
	writeFile(t, filepath.Join(repositoryPath, minimumVersionPath), `public const string MINIMUM_REQUIRED_CLI_VERSION = "3.0.0-beta.14";`)
	runCommand(t, repositoryPath, "git", "add", ".")
	runCommand(t, repositoryPath, "git", "commit", "-q", "-m", "base")
	return repositoryPath
}

func writeMockGitHubCli(t *testing.T, workDir string) string {
	t.Helper()

	mockBinPath := filepath.Join(workDir, "bin")
	if err := os.MkdirAll(mockBinPath, 0o755); err != nil {
		t.Fatalf("failed to create mock bin: %v", err)
	}

	mockGitHubCliPath := filepath.Join(mockBinPath, "gh")
	writeFile(t, mockGitHubCliPath, `#!/bin/sh
set -eu

method=GET
path=
input=

while [ "$#" -gt 0 ]; do
  case "$1" in
    api)
      ;;
    --method)
      shift
      method=$1
      ;;
    --input)
      shift
      input=$1
      ;;
    --jq)
      shift
      ;;
    --*)
      ;;
    *)
      if [ -z "$path" ]; then
        path=$1
      fi
      ;;
  esac
  shift
done

printf '%s %s\n' "$method" "$path" >> "$GH_LOG"

if [ "$method" = "GET" ]; then
  if [ -n "${GH_EXISTING_COMMENT_ID:-}" ]; then
    printf '%s\n' "$GH_EXISTING_COMMENT_ID"
  fi
  exit 0
fi

if [ -n "$input" ]; then
  cat "$input" >> "$GH_BODY_LOG"
fi
`)
	if err := os.Chmod(mockGitHubCliPath, 0o755); err != nil {
		t.Fatalf("failed to chmod mock gh: %v", err)
	}

	return mockBinPath
}

func repositoryRoot(t *testing.T) string {
	t.Helper()

	_, filePath, _, ok := runtime.Caller(0)
	if !ok {
		t.Fatal("failed to resolve test file path")
	}

	directory := filepath.Dir(filePath)
	for {
		if _, err := os.Stat(filepath.Join(directory, commentScriptPath)); err == nil {
			return directory
		}

		parent := filepath.Dir(directory)
		if parent == directory {
			t.Fatalf("failed to find repository root from %s", filePath)
		}
		directory = parent
	}
}

func runCommand(t *testing.T, workDir string, name string, args ...string) string {
	t.Helper()

	command := exec.Command(name, args...)
	command.Dir = workDir
	output, err := command.CombinedOutput()
	if err != nil {
		t.Fatalf("%s %s failed: %v\n%s", name, strings.Join(args, " "), err, string(output))
	}
	return string(output)
}

func writeFile(t *testing.T, path string, content string) {
	t.Helper()

	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatalf("failed to create directory for %s: %v", path, err)
	}
	if err := os.WriteFile(path, []byte(content+"\n"), 0o644); err != nil {
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

func assertContains(t *testing.T, actual string, expected string) {
	t.Helper()

	if !strings.Contains(actual, expected) {
		t.Fatalf("expected text to contain %q\nactual:\n%s", expected, actual)
	}
}

func assertNotContains(t *testing.T, actual string, unexpected string) {
	t.Helper()

	if strings.Contains(actual, unexpected) {
		t.Fatalf("expected text not to contain %q\nactual:\n%s", unexpected, actual)
	}
}
