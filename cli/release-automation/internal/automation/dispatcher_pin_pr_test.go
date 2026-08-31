package automation

import (
	"bytes"
	"context"
	"io"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

const dispatcherPinPRStableTag = "dispatcher-v3.0.1"

// dispatcherPinPRCommandRecorder fakes every git and gh invocation so the
// command can be exercised without a repository or network.
type dispatcherPinPRCommandRecorder struct {
	commands       []string
	statusOutput   string
	lsRemoteOutput string
	prListOutputs  []string
}

func (recorder *dispatcherPinPRCommandRecorder) run(_ context.Context, name string, args ...string) (string, error) {
	command := name + " " + strings.Join(args, " ")
	recorder.commands = append(recorder.commands, command)
	if name == "git" && containsDispatcherPinPRArg(args, "status") {
		return recorder.statusOutput, nil
	}
	if name == "git" && containsDispatcherPinPRArg(args, "ls-remote") {
		return recorder.lsRemoteOutput, nil
	}
	if name == "gh" && containsDispatcherPinPRArg(args, "list") {
		return recorder.nextPRListOutput(), nil
	}
	return "", nil
}

func (recorder *dispatcherPinPRCommandRecorder) nextPRListOutput() string {
	if len(recorder.prListOutputs) == 0 {
		return "[]"
	}
	output := recorder.prListOutputs[0]
	if len(recorder.prListOutputs) > 1 {
		recorder.prListOutputs = recorder.prListOutputs[1:]
	}
	return output
}

func containsDispatcherPinPRArg(args []string, wanted string) bool {
	for _, arg := range args {
		if arg == wanted {
			return true
		}
	}
	return false
}

func TestRunOpenDispatcherPinPRRejectsPreReleaseTag(t *testing.T) {
	// Verifies a pre-release dispatcher tag is refused before any git or gh command runs.
	recorder := &dispatcherPinPRCommandRecorder{}
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}

	exitCode := RunOpenDispatcherPinPR(context.Background(), &stdout, &stderr, []string{
		"--repo", "example/repository",
		"--tag", "dispatcher-v3.0.1-beta.2",
		"--base-branch", "main",
	})

	if exitCode != 1 {
		t.Fatalf("expected a pre-release tag to fail, got exit code %d", exitCode)
	}
	if !strings.Contains(stderr.String(), "pre-release") {
		t.Fatalf("expected a pre-release message, got %q", stderr.String())
	}
	if len(recorder.commands) != 0 {
		t.Fatalf("expected no commands to run, got %v", recorder.commands)
	}
}

func TestRunOpenDispatcherPinPROpensPullRequestForStableRelease(t *testing.T) {
	// Verifies a stable release stamps both pins, pushes a new branch, creates the PR, and dispatches its checks.
	repositoryRoot := setupDispatcherPinPRRepository(t, dispatcherPinPRContent("dispatcher-v3.0.0"))
	recorder := &dispatcherPinPRCommandRecorder{
		statusOutput:  " M Packages/src/project-runner-pin.json\n",
		prListOutputs: []string{"[]", `[{"number":4242,"url":"https://example.test/pr/4242"}]`},
	}
	dispatchedHeadRef := ""
	deps := dispatcherPinPRTestDeps(repositoryRoot, recorder, dispatcherPinPRContent(dispatcherPinPRStableTag))
	deps.dispatchChecks = func(_ context.Context, _ io.Writer, _ string, headRefName string, _ string) error {
		dispatchedHeadRef = headRefName
		return nil
	}

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := runOpenDispatcherPinPRWithDeps(context.Background(), &stdout, &stderr, dispatcherPinPRTestConfig(), deps)

	if exitCode != 0 {
		t.Fatalf("open-dispatcher-pin-pr failed: stdout=%s stderr=%s", stdout.String(), stderr.String())
	}
	branch := "chore/dispatcher-pin-" + dispatcherPinPRStableTag
	assertDispatcherPinPRCommand(t, recorder, "git -C "+repositoryRoot+" checkout -B "+branch+" refs/remotes/origin/main")
	assertDispatcherPinPRCommand(t, recorder, "push origin HEAD:refs/heads/"+branch)
	assertDispatcherPinPRCommand(t, recorder, "gh pr create --repo example/repository --base main --head "+branch)
	if dispatchedHeadRef != branch {
		t.Fatalf("expected checks dispatched for %q, got %q", branch, dispatchedHeadRef)
	}
	assertDispatcherPinPRMirrored(t, repositoryRoot)
}

func TestRunOpenDispatcherPinPRSkipsWhenPinAlreadyRecordsTheRelease(t *testing.T) {
	// Verifies a re-run on an already stamped pin exits successfully without committing, pushing, or opening a PR.
	stampedPin := dispatcherPinPRContent(dispatcherPinPRStableTag)
	repositoryRoot := setupDispatcherPinPRRepository(t, stampedPin)
	recorder := &dispatcherPinPRCommandRecorder{statusOutput: ""}
	deps := dispatcherPinPRTestDeps(repositoryRoot, recorder, stampedPin)

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := runOpenDispatcherPinPRWithDeps(context.Background(), &stdout, &stderr, dispatcherPinPRTestConfig(), deps)

	if exitCode != 0 {
		t.Fatalf("expected an idempotent re-run to succeed: stderr=%s", stderr.String())
	}
	if !strings.Contains(stdout.String(), "already records "+dispatcherPinPRStableTag) {
		t.Fatalf("expected an already-stamped message, got %q", stdout.String())
	}
	assertNoDispatcherPinPRCommand(t, recorder, "commit")
	assertNoDispatcherPinPRCommand(t, recorder, "push")
	assertNoDispatcherPinPRCommand(t, recorder, "gh pr")
}

func TestRunOpenDispatcherPinPRUpdatesAnExistingBranchAndPullRequest(t *testing.T) {
	// Verifies a re-run over an existing remote branch force-pushes with a lease and edits the open PR instead of creating one.
	repositoryRoot := setupDispatcherPinPRRepository(t, dispatcherPinPRContent("dispatcher-v3.0.0"))
	remoteSHA := "0123456789012345678901234567890123456789"
	branch := "chore/dispatcher-pin-" + dispatcherPinPRStableTag
	recorder := &dispatcherPinPRCommandRecorder{
		statusOutput:   " M Packages/src/project-runner-pin.json\n",
		lsRemoteOutput: remoteSHA + "\trefs/heads/" + branch + "\n",
		prListOutputs:  []string{`[{"number":77,"url":"https://example.test/pr/77"}]`},
	}
	deps := dispatcherPinPRTestDeps(repositoryRoot, recorder, dispatcherPinPRContent(dispatcherPinPRStableTag))

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := runOpenDispatcherPinPRWithDeps(context.Background(), &stdout, &stderr, dispatcherPinPRTestConfig(), deps)

	if exitCode != 0 {
		t.Fatalf("open-dispatcher-pin-pr failed: stdout=%s stderr=%s", stdout.String(), stderr.String())
	}
	assertDispatcherPinPRCommand(t, recorder, "push --force-with-lease=refs/heads/"+branch+":"+remoteSHA)
	assertDispatcherPinPRCommand(t, recorder, "gh pr edit 77 --repo example/repository")
	assertNoDispatcherPinPRCommand(t, recorder, "gh pr create")
}

func TestRunOpenDispatcherPinPRStopsBeforePushWhenTheStampedPinIsInvalid(t *testing.T) {
	// Verifies a stamp that fails offline pin validation never reaches commit or push.
	repositoryRoot := setupDispatcherPinPRRepository(t, dispatcherPinPRContent("dispatcher-v3.0.0"))
	recorder := &dispatcherPinPRCommandRecorder{statusOutput: " M Packages/src/project-runner-pin.json\n"}
	deps := dispatcherPinPRTestDeps(repositoryRoot, recorder, `{"projectRunnerVersion":"3.0.1","minimumDispatcherVersion":"3.0.0","dispatcherReleaseTag":"dispatcher-v3.0.1","dispatcherArchiveManifest":""}`+"\n")

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := runOpenDispatcherPinPRWithDeps(context.Background(), &stdout, &stderr, dispatcherPinPRTestConfig(), deps)

	if exitCode != 1 {
		t.Fatalf("expected an invalid stamped pin to fail, got exit code %d", exitCode)
	}
	if !strings.Contains(stderr.String(), "dispatcherArchiveManifest is empty") {
		t.Fatalf("expected the offline pin guard message, got %q", stderr.String())
	}
	assertNoDispatcherPinPRCommand(t, recorder, "commit")
	assertNoDispatcherPinPRCommand(t, recorder, "push")
}

func dispatcherPinPRTestConfig() dispatcherPinPRConfig {
	return dispatcherPinPRConfig{
		repository: "example/repository",
		tag:        dispatcherPinPRStableTag,
		baseBranch: "main",
	}
}

func dispatcherPinPRTestDeps(
	repositoryRoot string,
	recorder *dispatcherPinPRCommandRecorder,
	stampedPin string,
) dispatcherPinPRDeps {
	return dispatcherPinPRDeps{
		runOutput:      recorder.run,
		repositoryRoot: func(context.Context) (string, error) { return repositoryRoot, nil },
		stampPin: func(_ context.Context, pinPath string, _ string) error {
			return os.WriteFile(pinPath, []byte(stampedPin), 0o644)
		},
		verifySubjects: func(context.Context, []byte) error { return nil },
		dispatchChecks: func(context.Context, io.Writer, string, string, string) error { return nil },
	}
}

// dispatcherPinPRContent renders a pin that passes the offline guard for the
// given dispatcher release tag.
func dispatcherPinPRContent(releaseTag string) string {
	manifest := strings.Join([]string{
		"1111111111111111111111111111111111111111111111111111111111111111  install.ps1",
		"2222222222222222222222222222222222222222222222222222222222222222  install.sh",
		"3333333333333333333333333333333333333333333333333333333333333333  uloop-dispatcher-darwin-amd64.tar.gz",
		"4444444444444444444444444444444444444444444444444444444444444444  uloop-dispatcher-darwin-arm64.tar.gz",
		"5555555555555555555555555555555555555555555555555555555555555555  uloop-dispatcher-windows-amd64.zip",
	}, "\\n")
	return `{
  "projectRunnerVersion": "3.0.1",
  "minimumDispatcherVersion": "3.0.0",
  "dispatcherReleaseTag": "` + releaseTag + `",
  "dispatcherArchiveManifest": "` + manifest + `"
}
`
}

func setupDispatcherPinPRRepository(t *testing.T, pinContent string) string {
	t.Helper()
	repositoryRoot := t.TempDir()
	packagePinPath := filepath.Join(repositoryRoot, filepath.FromSlash(unityPackageCliPinFile))
	projectPinPath := filepath.Join(repositoryRoot, filepath.FromSlash(unityProjectCliPinFile))
	for _, pinPath := range []string{packagePinPath, projectPinPath} {
		if err := os.MkdirAll(filepath.Dir(pinPath), 0o755); err != nil {
			t.Fatalf("failed to create pin directory: %v", err)
		}
		if err := os.WriteFile(pinPath, []byte(pinContent), 0o644); err != nil {
			t.Fatalf("failed to write pin: %v", err)
		}
	}
	return repositoryRoot
}

func assertDispatcherPinPRMirrored(t *testing.T, repositoryRoot string) {
	t.Helper()
	packagePin, err := os.ReadFile(filepath.Join(repositoryRoot, filepath.FromSlash(unityPackageCliPinFile)))
	if err != nil {
		t.Fatalf("failed to read package pin: %v", err)
	}
	projectPin, err := os.ReadFile(filepath.Join(repositoryRoot, filepath.FromSlash(unityProjectCliPinFile)))
	if err != nil {
		t.Fatalf("failed to read project pin: %v", err)
	}
	if !bytes.Equal(packagePin, projectPin) {
		t.Fatalf("mirrored pin differs from the package pin")
	}
}

func assertDispatcherPinPRCommand(t *testing.T, recorder *dispatcherPinPRCommandRecorder, wanted string) {
	t.Helper()
	for _, command := range recorder.commands {
		if strings.Contains(command, wanted) {
			return
		}
	}
	t.Fatalf("expected a command containing %q, got %v", wanted, recorder.commands)
}

func assertNoDispatcherPinPRCommand(t *testing.T, recorder *dispatcherPinPRCommandRecorder, unwanted string) {
	t.Helper()
	for _, command := range recorder.commands {
		if strings.Contains(command, unwanted) {
			t.Fatalf("expected no command containing %q, got %q", unwanted, command)
		}
	}
}
