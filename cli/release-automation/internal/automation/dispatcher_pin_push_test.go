package automation

import (
	"bytes"
	"context"
	"errors"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

const dispatcherPinPushStableTag = "dispatcher-v3.0.1"

// dispatcherPinPushCommandRecorder fakes every git invocation so the command
// can be exercised without a repository or network.
type dispatcherPinPushCommandRecorder struct {
	commands     []string
	statusOutput string
	pushError    error
}

func (recorder *dispatcherPinPushCommandRecorder) run(_ context.Context, name string, args ...string) (string, error) {
	command := name + " " + strings.Join(args, " ")
	recorder.commands = append(recorder.commands, command)
	if name == "git" && containsDispatcherPinPushArg(args, "status") {
		return recorder.statusOutput, nil
	}
	if name == "git" && containsDispatcherPinPushArg(args, "push") {
		return "", recorder.pushError
	}
	return "", nil
}

func containsDispatcherPinPushArg(args []string, wanted string) bool {
	for _, arg := range args {
		if arg == wanted {
			return true
		}
	}
	return false
}

func TestRunPushDispatcherPinRejectsPreReleaseTag(t *testing.T) {
	// Verifies a pre-release dispatcher tag is refused before any git command runs.
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}

	exitCode := RunPushDispatcherPin(context.Background(), &stdout, &stderr, []string{
		"--tag", "dispatcher-v3.0.1-beta.2",
		"--base-branch", "main",
	})

	if exitCode != 1 {
		t.Fatalf("expected a pre-release tag to fail, got exit code %d", exitCode)
	}
	if !strings.Contains(stderr.String(), "pre-release") {
		t.Fatalf("expected a pre-release message, got %q", stderr.String())
	}
}

func TestRunPushDispatcherPinRequiresBaseBranch(t *testing.T) {
	// Verifies the command refuses to run without an explicit base branch instead of guessing one.
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}

	exitCode := RunPushDispatcherPin(context.Background(), &stdout, &stderr, []string{
		"--tag", dispatcherPinPushStableTag,
	})

	if exitCode != 1 {
		t.Fatalf("expected a missing base branch to fail, got exit code %d", exitCode)
	}
	if !strings.Contains(stderr.String(), "--base-branch is required") {
		t.Fatalf("expected a base branch message, got %q", stderr.String())
	}
}

func TestRunPushDispatcherPinPushesStampToBaseBranchForStableRelease(t *testing.T) {
	// Verifies a stable release stamps both pins from the base tip and pushes the commit straight to the base branch without force.
	repositoryRoot := setupDispatcherPinPushRepository(t, dispatcherPinPushContent("dispatcher-v3.0.0"))
	recorder := &dispatcherPinPushCommandRecorder{statusOutput: " M Packages/src/project-runner-pin.json\n"}
	deps := dispatcherPinPushTestDeps(repositoryRoot, recorder, dispatcherPinPushContent(dispatcherPinPushStableTag))

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := runPushDispatcherPinWithDeps(context.Background(), &stdout, &stderr, dispatcherPinPushTestConfig(), deps)

	if exitCode != 0 {
		t.Fatalf("push-dispatcher-pin failed: stdout=%s stderr=%s", stdout.String(), stderr.String())
	}
	assertDispatcherPinPushCommand(t, recorder, "git -C "+repositoryRoot+" fetch origin +refs/heads/main:refs/remotes/origin/main")
	assertDispatcherPinPushCommand(t, recorder, "git -C "+repositoryRoot+" checkout --detach refs/remotes/origin/main")
	assertDispatcherPinPushCommand(t, recorder, "commit --message chore: update dispatcher pin to the 3.0.1 stable release")
	assertDispatcherPinPushCommand(t, recorder, "git -C "+repositoryRoot+" push origin HEAD:refs/heads/main")
	assertNoDispatcherPinPushCommand(t, recorder, "--force")
	assertNoDispatcherPinPushCommand(t, recorder, "gh ")
	if !strings.Contains(stdout.String(), "Pushed the stamped dispatcher pin for "+dispatcherPinPushStableTag+" to main.") {
		t.Fatalf("expected a push confirmation, got %q", stdout.String())
	}
	assertDispatcherPinPushMirrored(t, repositoryRoot)
}

func TestRunPushDispatcherPinSkipsWhenPinAlreadyRecordsTheRelease(t *testing.T) {
	// Verifies a re-run on an already stamped pin exits successfully without committing or pushing.
	stampedPin := dispatcherPinPushContent(dispatcherPinPushStableTag)
	repositoryRoot := setupDispatcherPinPushRepository(t, stampedPin)
	recorder := &dispatcherPinPushCommandRecorder{statusOutput: ""}
	deps := dispatcherPinPushTestDeps(repositoryRoot, recorder, stampedPin)

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := runPushDispatcherPinWithDeps(context.Background(), &stdout, &stderr, dispatcherPinPushTestConfig(), deps)

	if exitCode != 0 {
		t.Fatalf("expected an idempotent re-run to succeed: stderr=%s", stderr.String())
	}
	if !strings.Contains(stdout.String(), "already records "+dispatcherPinPushStableTag) {
		t.Fatalf("expected an already-stamped message, got %q", stdout.String())
	}
	assertNoDispatcherPinPushCommand(t, recorder, "commit")
	assertNoDispatcherPinPushCommand(t, recorder, "push")
}

func TestRunPushDispatcherPinFailsWhenTheBaseBranchMovedUnderneath(t *testing.T) {
	// Verifies a rejected non-force push surfaces as a failure so a re-run stamps the new base tip instead of overwriting it.
	repositoryRoot := setupDispatcherPinPushRepository(t, dispatcherPinPushContent("dispatcher-v3.0.0"))
	recorder := &dispatcherPinPushCommandRecorder{
		statusOutput: " M Packages/src/project-runner-pin.json\n",
		pushError:    errors.New("! [rejected] HEAD -> main (fetch first)"),
	}
	deps := dispatcherPinPushTestDeps(repositoryRoot, recorder, dispatcherPinPushContent(dispatcherPinPushStableTag))

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := runPushDispatcherPinWithDeps(context.Background(), &stdout, &stderr, dispatcherPinPushTestConfig(), deps)

	if exitCode != 1 {
		t.Fatalf("expected a rejected push to fail, got exit code %d", exitCode)
	}
	if !strings.Contains(stderr.String(), "rejected") {
		t.Fatalf("expected the push rejection in stderr, got %q", stderr.String())
	}
	assertNoDispatcherPinPushCommand(t, recorder, "--force")
}

func TestRunPushDispatcherPinStopsBeforePushWhenTheStampedPinIsInvalid(t *testing.T) {
	// Verifies a stamp that fails offline pin validation never reaches commit or push.
	repositoryRoot := setupDispatcherPinPushRepository(t, dispatcherPinPushContent("dispatcher-v3.0.0"))
	recorder := &dispatcherPinPushCommandRecorder{statusOutput: " M Packages/src/project-runner-pin.json\n"}
	deps := dispatcherPinPushTestDeps(repositoryRoot, recorder, `{"projectRunnerVersion":"3.0.1","minimumDispatcherVersion":"3.0.0","dispatcherReleaseTag":"dispatcher-v3.0.1","dispatcherArchiveManifest":""}`+"\n")

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := runPushDispatcherPinWithDeps(context.Background(), &stdout, &stderr, dispatcherPinPushTestConfig(), deps)

	if exitCode != 1 {
		t.Fatalf("expected an invalid stamped pin to fail, got exit code %d", exitCode)
	}
	if !strings.Contains(stderr.String(), "dispatcherArchiveManifest is empty") {
		t.Fatalf("expected the offline pin guard message, got %q", stderr.String())
	}
	assertNoDispatcherPinPushCommand(t, recorder, "commit")
	assertNoDispatcherPinPushCommand(t, recorder, "push")
}

func dispatcherPinPushTestConfig() dispatcherPinPushConfig {
	return dispatcherPinPushConfig{
		tag:        dispatcherPinPushStableTag,
		baseBranch: "main",
	}
}

func dispatcherPinPushTestDeps(
	repositoryRoot string,
	recorder *dispatcherPinPushCommandRecorder,
	stampedPin string,
) dispatcherPinPushDeps {
	return dispatcherPinPushDeps{
		runOutput:      recorder.run,
		repositoryRoot: func(context.Context) (string, error) { return repositoryRoot, nil },
		stampPin: func(_ context.Context, pinPath string, _ string) error {
			return os.WriteFile(pinPath, []byte(stampedPin), 0o644)
		},
		verifySubjects: func(context.Context, []byte) error { return nil },
	}
}

// dispatcherPinPushContent renders a pin that passes the offline guard for the
// given dispatcher release tag.
func dispatcherPinPushContent(releaseTag string) string {
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

func setupDispatcherPinPushRepository(t *testing.T, pinContent string) string {
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

func assertDispatcherPinPushMirrored(t *testing.T, repositoryRoot string) {
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

func assertDispatcherPinPushCommand(t *testing.T, recorder *dispatcherPinPushCommandRecorder, wanted string) {
	t.Helper()
	for _, command := range recorder.commands {
		if strings.Contains(command, wanted) {
			return
		}
	}
	t.Fatalf("expected a command containing %q, got %v", wanted, recorder.commands)
}

func assertNoDispatcherPinPushCommand(t *testing.T, recorder *dispatcherPinPushCommandRecorder, unwanted string) {
	t.Helper()
	for _, command := range recorder.commands {
		if strings.Contains(command, unwanted) {
			t.Fatalf("expected no command containing %q, got %q", unwanted, command)
		}
	}
}
