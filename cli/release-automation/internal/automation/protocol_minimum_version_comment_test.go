package automation

import (
	"bytes"
	"context"
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// protocolMinimumVersionWarningCase changes the protocol without a matching minimum runner, so the guard warns.
func protocolMinimumVersionWarningCase() protocolMinimumVersionCommentCase {
	return protocolMinimumVersionCommentCase{
		baseProtocol:      1,
		baseProjectRunner: "3.0.0-beta.32",
		headProtocol:      2,
		headProjectRunner: "3.0.0-beta.32",
	}
}

// protocolMinimumVersionResolvedCase changes the protocol together with a matching minimum runner release.
func protocolMinimumVersionResolvedCase() protocolMinimumVersionCommentCase {
	return protocolMinimumVersionCommentCase{
		baseProtocol:      1,
		baseProjectRunner: "3.0.0-beta.32",
		headProtocol:      2,
		headProjectRunner: "3.0.0-beta.33",
		releaseContent:    `{"schemaVersion":1,"protocolVersion":2,"projectRunnerVersion":"3.0.0-beta.33"}`,
	}
}

func TestRunProtocolMinimumVersionComment_WhenNoCommentExists_PostsNewComment(t *testing.T) {
	// Verifies a warning with no earlier bot comment posts a new comment on the pull request.
	result := runProtocolMinimumVersionCommentCase(t, protocolMinimumVersionWarningCase())

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertProtocolMinimumVersionLogContains(t, result.stdout, "Posted protocol minimum version comment.")
	assertProtocolMinimumVersionLogContains(t, result.ghLog, "api --method POST repos/owner/repository/issues/456/comments --input")
	assertProtocolMinimumVersionPostedBody(t, result.ghLog, "Protocol version changed, but")
}

// assertProtocolMinimumVersionPostedBody checks the comment body the mock gh copied from --input carries the marker that
// later runs search for and the expected warning text.
func assertProtocolMinimumVersionPostedBody(t *testing.T, ghLog string, wantText string) {
	t.Helper()
	_, input, found := strings.Cut(ghLog, "input: ")
	if !found {
		t.Fatalf("expected the mock gh to log the --input body, got:\n%s", ghLog)
	}
	input, _, _ = strings.Cut(input, "\n")
	payload := struct {
		Body string `json:"body"`
	}{}
	if err := json.Unmarshal([]byte(input), &payload); err != nil {
		t.Fatalf("failed to parse the posted body %q: %v", input, err)
	}
	if !strings.HasPrefix(payload.Body, protocolMinimumVersionMarker+"\n") || !strings.Contains(payload.Body, wantText) {
		t.Fatalf("posted body lacks the marker or %q:\n%s", wantText, payload.Body)
	}
}

func TestRunProtocolMinimumVersionComment_WhenResolvedWithoutComment_DoesNothing(t *testing.T) {
	// Verifies a resolved warning with no earlier bot comment neither deletes nor posts anything.
	result := runProtocolMinimumVersionCommentCase(t, protocolMinimumVersionResolvedCase())

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	if result.stdout != "" {
		t.Fatalf("expected no output, got %q", result.stdout)
	}
	if strings.Contains(result.ghLog, "--method") {
		t.Fatalf("expected no comment write, got gh log:\n%s", result.ghLog)
	}
}

func TestRunProtocolMinimumVersionComment_WhenRepositoryIsUnset_ResolvesItThroughGh(t *testing.T) {
	// Verifies the repository comes from gh repo view when GITHUB_REPOSITORY is empty.
	testCase := protocolMinimumVersionWarningCase()
	testCase.repositoryFromGh = true
	testCase.repoView = "resolved/repository"

	result := runProtocolMinimumVersionCommentCase(t, testCase)

	if result.exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", result.exitCode, result.stderr)
	}
	assertProtocolMinimumVersionLogContains(t, result.ghLog, "repo view --json nameWithOwner")
	assertProtocolMinimumVersionLogContains(t, result.ghLog, "repos/resolved/repository/issues/456/comments")
}

func TestRunProtocolMinimumVersionComment_WhenCommentLookupFails_Fails(t *testing.T) {
	// Verifies a failing comment lookup fails the command both when posting and when deleting.
	cases := map[string]protocolMinimumVersionCommentCase{
		"warning":  protocolMinimumVersionWarningCase(),
		"resolved": protocolMinimumVersionResolvedCase(),
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			testCase.failCommentLookup = true

			result := runProtocolMinimumVersionCommentCase(t, testCase)

			if result.exitCode != 1 {
				t.Fatalf("expected exit code 1, got %d\nstdout: %s", result.exitCode, result.stdout)
			}
			assertProtocolMinimumVersionLogContains(t, result.stderr, "comment lookup failed")
		})
	}
}

func TestRunProtocolMinimumVersionComment_WhenCommentBodyFileCannotBeCreated_Fails(t *testing.T) {
	// Verifies an unusable temporary directory fails the command instead of posting an empty comment.
	missingDirectory := filepath.Join(t.TempDir(), "missing")
	t.Setenv("TMPDIR", missingDirectory)
	t.Setenv("TMP", missingDirectory)
	t.Setenv("TEMP", missingDirectory)

	result := runProtocolMinimumVersionCommentCase(t, protocolMinimumVersionWarningCase())

	if result.exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", result.exitCode, result.stdout)
	}
	assertProtocolMinimumVersionLogContains(t, result.stderr, "failed to create comment body file")
}

func TestRunProtocolMinimumVersionComment_WhenRepositoryCannotBeResolved_Fails(t *testing.T) {
	// Verifies a failing gh repo view fails the command before any comment is touched.
	testCase := protocolMinimumVersionWarningCase()
	testCase.repositoryFromGh = true

	result := runProtocolMinimumVersionCommentCase(t, testCase)

	if result.exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", result.exitCode, result.stdout)
	}
	assertProtocolMinimumVersionLogContains(t, result.stderr, "repo view failed")
}

func TestRunProtocolMinimumVersionComment_WithoutPullRequestOrBaseRef_Skips(t *testing.T) {
	// Verifies a run without a pull request number or base ref skips without failing.
	cases := []struct {
		name        string
		environment map[string]string
		want        string
	}{
		{"no pull request", map[string]string{"PR_NUMBER": "", "GITHUB_BASE_REF": "main"}, "no PR number was provided"},
		{"no base ref", map[string]string{"PR_NUMBER": "1", "GITHUB_BASE_REF": "", "PROTOCOL_MINIMUM_VERSION_BASE_REF": ""}, "no base ref was provided"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			// Mock git and gh come first on PATH so a regression past the skip reaches the mocks, not GitHub.
			workDir := setupProtocolMinimumVersionMocks(t, protocolMinimumVersionRefCase{})
			for key, value := range testCase.environment {
				t.Setenv(key, value)
			}
			stdout := bytes.Buffer{}
			stderr := bytes.Buffer{}

			exitCode := RunProtocolMinimumVersionComment(context.Background(), &stdout, &stderr)

			if exitCode != 0 {
				t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr.String())
			}
			assertProtocolMinimumVersionLogContains(t, stdout.String(), testCase.want)
			if _, err := os.Stat(filepath.Join(workDir, "gh.log")); !os.IsNotExist(err) {
				t.Fatalf("expected no gh call, got stat error %v", err)
			}
		})
	}
}

func TestRunProtocolMinimumVersionComment_WhenGitIsUnavailable_Fails(t *testing.T) {
	// Verifies a guard analysis failure fails the command instead of deleting the existing comment.
	t.Setenv("PATH", t.TempDir())
	t.Setenv("ULOOP_REPOSITORY_ROOT", t.TempDir())
	t.Setenv("PR_NUMBER", "1")
	t.Setenv("PROTOCOL_MINIMUM_VERSION_BASE_REF", "origin/main")
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}

	exitCode := RunProtocolMinimumVersionComment(context.Background(), &stdout, &stderr)

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", exitCode, stdout.String())
	}
	assertProtocolMinimumVersionLogContains(t, stderr.String(), "failed to resolve git repository root")
}

func TestProtocolMinimumVersionCommentConfigFromEnvironment_AppliesDefaults(t *testing.T) {
	// Verifies the working directory, origin-prefixed base ref, HEAD, and bot author are used when their variables are unset.
	t.Setenv("ULOOP_REPOSITORY_ROOT", "")
	t.Setenv("PROTOCOL_MINIMUM_VERSION_BASE_REF", "")
	t.Setenv("GITHUB_BASE_REF", "main")
	t.Setenv("PROTOCOL_MINIMUM_VERSION_HEAD_REF", "")
	t.Setenv("PROTOCOL_MINIMUM_VERSION_COMMENT_AUTHOR", "")
	workingDirectory, err := os.Getwd()
	if err != nil {
		t.Fatalf("getwd: %v", err)
	}

	config, err := protocolMinimumVersionCommentConfigFromEnvironment()
	if err != nil {
		t.Fatalf("expected config to resolve, got %v", err)
	}
	if config.repositoryRoot != workingDirectory || config.baseRef != "origin/main" || config.headRef != "HEAD" || config.commentAuthor != protocolMinimumVersionCommentAuthor {
		t.Fatalf("config = %+v", config)
	}
}
