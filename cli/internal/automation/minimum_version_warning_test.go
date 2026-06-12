package automation

import (
	"bytes"
	"context"
	"os"
	"path/filepath"
	"testing"
)

// Verifies that GitHub Actions environment values map to the diff configuration.
func TestMinimumVersionWarningConfigUsesGitHubEnvironmentFallbacks(t *testing.T) {
	t.Setenv("ULOOP_REPOSITORY_ROOT", "/tmp/repository")
	t.Setenv("PR_NUMBER", "123")
	t.Setenv("GITHUB_REPOSITORY", "owner/repository")
	t.Setenv("GITHUB_BASE_REF", "main")
	t.Setenv("CLI_MINIMUM_VERSION_BASE_REF", "")
	t.Setenv("CLI_MINIMUM_VERSION_HEAD_REF", "")

	config, err := minimumVersionWarningConfigFromEnvironment()
	if err != nil {
		t.Fatalf("expected environment config, got error: %v", err)
	}

	if config.repositoryRoot != "/tmp/repository" {
		t.Fatalf("expected repository root from env, got %q", config.repositoryRoot)
	}
	if config.pullRequest != "123" {
		t.Fatalf("expected pull request from env, got %q", config.pullRequest)
	}
	if config.repository != "owner/repository" {
		t.Fatalf("expected repository from env, got %q", config.repository)
	}
	if config.baseRef != "origin/main" {
		t.Fatalf("expected base ref fallback, got %q", config.baseRef)
	}
	if config.headRef != "HEAD" {
		t.Fatalf("expected default head ref, got %q", config.headRef)
	}
}

// Verifies that missing base refs skip before repository resolution needs gh.
func TestMinimumVersionWarningSkipsMissingBaseRefBeforeRepositoryResolution(t *testing.T) {
	t.Setenv("ULOOP_REPOSITORY_ROOT", t.TempDir())
	t.Setenv("PR_NUMBER", "123")
	t.Setenv("GITHUB_REPOSITORY", "")
	t.Setenv("GITHUB_BASE_REF", "")
	// CI sets the real PR head branch for every step; clear it so the
	// release-please skip cannot fire before the base-ref skip under test.
	t.Setenv("GITHUB_HEAD_REF", "")
	t.Setenv("CLI_MINIMUM_VERSION_BASE_REF", "")
	t.Setenv("CLI_MINIMUM_VERSION_HEAD_REF", "")
	t.Setenv("PATH", t.TempDir())

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := RunMinimumVersionWarning(context.Background(), &stdout, &stderr)

	if exitCode != 0 {
		t.Fatalf("expected missing base ref to skip with exit code 0, got %d", exitCode)
	}
	if stdout.String() != "Skipping CLI minimum version comment because no base ref was provided.\n" {
		t.Fatalf("expected base-ref skip message, got %q", stdout.String())
	}
	if stderr.String() != "" {
		t.Fatalf("expected no stderr on base-ref skip, got %q", stderr.String())
	}
}

// Verifies that release-please release PRs skip the check before any git diff runs.
func TestMinimumVersionWarningSkipsReleasePleaseHeadBranch(t *testing.T) {
	t.Setenv("ULOOP_REPOSITORY_ROOT", t.TempDir())
	t.Setenv("PR_NUMBER", "123")
	t.Setenv("GITHUB_REPOSITORY", "owner/repository")
	t.Setenv("GITHUB_BASE_REF", "v3-beta")
	t.Setenv("GITHUB_HEAD_REF", "release-please--branches--v3-beta")
	t.Setenv("CLI_MINIMUM_VERSION_BASE_REF", "")
	t.Setenv("CLI_MINIMUM_VERSION_HEAD_REF", "")
	t.Setenv("CLI_MINIMUM_VERSION_FAIL_ON_WARNING", "true")
	t.Setenv("PATH", t.TempDir())

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := RunMinimumVersionWarning(context.Background(), &stdout, &stderr)

	if exitCode != 0 {
		t.Fatalf("expected release-please head branch to skip with exit code 0, got %d\n%s", exitCode, stderr.String())
	}
	if stdout.String() != "Skipping CLI minimum version check because this is a release-please release PR.\n" {
		t.Fatalf("expected release-please skip message, got %q", stdout.String())
	}
	if stderr.String() != "" {
		t.Fatalf("expected no stderr on release-please skip, got %q", stderr.String())
	}
}

// Verifies that Go CLI source changes require a minimum-version warning.
func TestMinimumVersionWarningRequiresCommentForGoCliSourceChanges(t *testing.T) {
	testCases := []struct {
		name        string
		changedFile string
		shouldWarn  bool
	}{
		{
			name:        "command source",
			changedFile: goCliPackageRoot + "cmd/uloop/main.go",
			shouldWarn:  true,
		},
		{
			name:        "internal source",
			changedFile: goCliPackageRoot + "internal/cli/run.go",
			shouldWarn:  true,
		},
		{
			name:        "internal test source",
			changedFile: goCliPackageRoot + "internal/architecture/comment_cli_minimum_version_warning_test.go",
			shouldWarn:  false,
		},
		{
			name:        "warning command source",
			changedFile: goCliPackageRoot + "cmd/comment-cli-minimum-version-warning/main.go",
			shouldWarn:  false,
		},
		{
			name:        "release check command source",
			changedFile: goCliPackageRoot + "cmd/dispatch-release-please-pr-checks/main.go",
			shouldWarn:  false,
		},
		{
			name:        "warning automation source",
			changedFile: goCliPackageRoot + "internal/automation/minimum_version_warning.go",
			shouldWarn:  false,
		},
		{
			name:        "root source",
			changedFile: goCliPackageRoot + "main.go",
			shouldWarn:  true,
		},
		{
			name:        "module file",
			changedFile: goCliPackageRoot + "go.mod",
			shouldWarn:  true,
		},
		{
			name:        "contract file",
			changedFile: goCliPackageRoot + "contract.json",
			shouldWarn:  true,
		},
		{
			name:        "generated binary",
			changedFile: goCliPackageRoot + "dist/darwin-arm64/uloop",
			shouldWarn:  false,
		},
		{
			name:        "release notes",
			changedFile: goCliPackageRoot + "CHANGELOG.md",
			shouldWarn:  false,
		},
		{
			name:        "unrelated documentation",
			changedFile: "docs/usage.md",
			shouldWarn:  false,
		},
	}

	for _, testCase := range testCases {
		t.Run(testCase.name, func(t *testing.T) {
			requiresComment := minimumVersionWarningRequiresComment([]string{testCase.changedFile})
			if requiresComment != testCase.shouldWarn {
				t.Fatalf("expected warning=%t for %s, got %t", testCase.shouldWarn, testCase.changedFile, requiresComment)
			}
		})
	}
}

// Verifies that a minimum-version bump suppresses the warning even when Go CLI files changed.
func TestMinimumVersionWarningSkipsCommentWhenMinimumVersionChanges(t *testing.T) {
	requiresComment := minimumVersionWarningRequiresComment([]string{
		goCliPackageRoot + "internal/cli/run.go",
		minimumVersionWarningFile,
	})

	if requiresComment {
		t.Fatal("expected minimum-version changes to suppress the warning")
	}
}

// Verifies that successful command stderr does not pollute parsed stdout.
func TestMinimumVersionWarningOutputKeepsStderrOutOfStdout(t *testing.T) {
	workDir := t.TempDir()
	commandPath := filepath.Join(workDir, "emit-output")
	err := os.WriteFile(commandPath, []byte("#!/bin/sh\nprintf 'parsed-output\\n'\nprintf 'stderr notice\\n' >&2\n"), 0o755)
	if err != nil {
		t.Fatalf("failed to write test command: %v", err)
	}

	output, err := runMinimumVersionWarningOutput(context.Background(), workDir, commandPath)
	if err != nil {
		t.Fatalf("expected command success, got error: %v", err)
	}

	if output != "parsed-output\n" {
		t.Fatalf("expected stdout only, got %q", output)
	}
}
