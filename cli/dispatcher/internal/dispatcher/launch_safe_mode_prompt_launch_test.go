package dispatcher

import (
	"bytes"
	"context"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
	"github.com/hatayama/unity-cli-loop/common/unityprocess/unitydialog"
)

// safeModePromptPresses records the presses a launch asked for.
type safeModePromptPresses struct {
	titles []string
	labels []string
}

// safeModePromptLaunchDeps is freshV3LaunchDeps whose readiness wait looks once, through the
// finder launch hands it, at the dialogs looked returns, and then ends with err. Every press is
// recorded and presses.
func safeModePromptLaunchDeps(t *testing.T, looked []unitydialog.Dialog, err error) (launchDeps, *safeModePromptPresses) {
	t.Helper()
	deps := freshV3LaunchDeps(t, nil)
	presses := &safeModePromptPresses{}
	deps.lookForDialogs = func(context.Context, string, unitydialog.ProcessLookup) ([]unitydialog.Dialog, error) {
		return looked, nil
	}
	deps.pressDialogButtonInDialogTitled = func(
		_ context.Context, _ string, _ unitydialog.ProcessLookup, title string, label string,
	) (unitydialog.PressResult, error) {
		presses.titles = append(presses.titles, title)
		presses.labels = append(presses.labels, label)
		return unitydialog.PressResult{DialogFound: true, Pressed: true}, nil
	}
	deps.waitForToolReadiness = func(ctx context.Context, _ string, _ time.Duration, find clicore.DialogFinder) error {
		dialogs, findErr := find(ctx)
		if findErr == nil && len(dialogs) > 0 {
			return clicore.ToolReadinessBlockedByDialogError{Dialogs: dialogs}
		}
		return err
	}
	return deps, presses
}

// assertOneIgnorePressOnThePrompt checks launch pressed exactly once, Ignore in the Safe Mode prompt.
func assertOneIgnorePressOnThePrompt(t *testing.T, presses *safeModePromptPresses) {
	t.Helper()
	if len(presses.titles) != 1 || presses.titles[0] != "Enter Safe Mode?" || presses.labels[0] != "Ignore" {
		t.Fatalf("presses = %+v, want one press of Ignore in \"Enter Safe Mode?\"", presses)
	}
}

// launchRun is what one launch printed.
type launchRun struct {
	code   int
	stdout string
	stderr string
}

func runLaunchForTest(t *testing.T, options launchOptions, deps launchDeps) launchRun {
	t.Helper()
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runLaunchWithDeps(context.Background(), options, options.projectPath, &stdout, &stderr, deps)
	return launchRun{code: code, stdout: stdout.String(), stderr: stderr.String()}
}

// assertReadyAfterIgnoringThePrompt checks a launch ended ready and reported the pressed prompt in
// the response and its message.
func assertReadyAfterIgnoringThePrompt(t *testing.T, run launchRun) {
	t.Helper()
	if run.code != 0 {
		t.Fatalf("exit code = %d, want 0\nstderr=%s", run.code, run.stderr)
	}
	for _, expected := range []string{
		`"Ready": true`,
		`"SafeModePromptIgnored": true`,
		`uloop pressed Ignore on Unity's \"Enter Safe Mode?\" prompt`,
		"run uloop compile to list them.",
	} {
		if !strings.Contains(run.stdout, expected) {
			t.Fatalf("launch output missing %q:\n%s", expected, run.stdout)
		}
	}
}

// Verifies a fresh launch presses Ignore once on the Safe Mode prompt, keeps waiting, and reports
// the pressed prompt when the Editor gets ready.
func TestLaunchStartsEditorAndIgnoresTheSafeModePrompt(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	deps, presses := safeModePromptLaunchDeps(t, []unitydialog.Dialog{safeModePromptDialog()}, nil)

	run := runLaunchForTest(t, launchOptions{projectPath: projectRoot, editorVersion: "6000.0.0f1"}, deps)

	assertReadyAfterIgnoringThePrompt(t, run)
	assertOneIgnorePressOnThePrompt(t, presses)
	if !strings.Contains(run.stdout, `"Launched": true`) || !strings.Contains(run.stdout, "Unity Loop Harness is ready. uloop pressed") {
		t.Fatalf("launch output is not a fresh launch's:\n%s", run.stdout)
	}
}

// Verifies a launch that finds an open Editor still starting presses Ignore once on the Safe Mode
// prompt and reports it with the already-running response.
func TestLaunchWaitsForRunningEditorAndIgnoresTheSafeModePrompt(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	deps, presses := safeModePromptLaunchDeps(t, []unitydialog.Dialog{safeModePromptDialog()}, nil)
	deps.findRunningUnityProcess = func(context.Context, string) (*unityprocess.UnityProcess, error) {
		return &unityprocess.UnityProcess{Pid: 111}, nil
	}

	run := runLaunchForTest(t, launchOptions{projectPath: projectRoot}, deps)

	assertReadyAfterIgnoringThePrompt(t, run)
	assertOneIgnorePressOnThePrompt(t, presses)
	if !strings.Contains(run.stdout, `"AlreadyRunning": true`) {
		t.Fatalf("launch output is not an already-running launch's:\n%s", run.stdout)
	}
}

// Verifies a launch that saw no Safe Mode prompt prints the response it always printed: no
// SafeModePromptIgnored key and the plain ready message.
func TestLaunchWithoutTheSafeModePromptLeavesTheResponseUnchanged(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	deps, presses := safeModePromptLaunchDeps(t, nil, nil)

	run := runLaunchForTest(t, launchOptions{projectPath: projectRoot, editorVersion: "6000.0.0f1"}, deps)

	if run.code != 0 || len(presses.titles) != 0 {
		t.Fatalf("exit code = %d, presses = %+v, want 0 and no press\nstderr=%s", run.code, presses, run.stderr)
	}
	if strings.Contains(run.stdout, "SafeModePromptIgnored") || !strings.Contains(run.stdout, `"Message": "Unity Loop Harness is ready."`) {
		t.Fatalf("launch output changed:\n%s", run.stdout)
	}
}

// Verifies a launch that pressed Ignore and then stopped at another dialog behind the prompt
// reports that dialog and says it pressed the prompt.
func TestLaunchReportsTheIgnoredSafeModePromptWhenAnotherDialogBlocks(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	deps, presses := safeModePromptLaunchDeps(t, []unitydialog.Dialog{safeModePromptDialog(), launchTestDialog()}, nil)

	run := runFreshLaunch(t, projectRoot, deps)

	cliError := assertLaunchBlockedByDialog(t, run, 1)
	assertOneIgnorePressOnThePrompt(t, presses)
	if cliError.Details["SafeModePromptIgnored"] != true {
		t.Fatalf("Details = %#v, want SafeModePromptIgnored true", cliError.Details)
	}
}

// Verifies a launch that pressed Ignore and then ran out of time reports the startup timeout and
// says it pressed the prompt.
func TestLaunchReportsTheIgnoredSafeModePromptOnStartupTimeout(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	timeout := clierrors.UnityServerNotRespondingError{ProjectRoot: projectRoot, Endpoint: "probe.sock"}
	deps, presses := safeModePromptLaunchDeps(t, []unitydialog.Dialog{safeModePromptDialog()}, timeout)

	run := runFreshLaunch(t, projectRoot, deps)

	assertOneIgnorePressOnThePrompt(t, presses)
	cliError := run.envelope.Error
	if cliError.ErrorCode != clierrors.ErrorCodeUnityStartupTimeout || cliError.Details["SafeModePromptIgnored"] != true {
		t.Fatalf("ErrorCode = %q, Details = %#v, want %s with SafeModePromptIgnored true",
			cliError.ErrorCode, cliError.Details, clierrors.ErrorCodeUnityStartupTimeout)
	}
}

// Verifies a launch that pressed Ignore and then saw the Editor exit returns clicore's not-running
// error as it is, without the pressed prompt.
func TestLaunchAfterIgnoringTheSafeModePromptReturnsAnExitedEditorErrorAsIs(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	exited := clierrors.UnityNotRunningError{ProjectRoot: projectRoot, ExitedDuringCommand: true}
	deps, presses := safeModePromptLaunchDeps(t, []unitydialog.Dialog{safeModePromptDialog()}, exited)

	run := runFreshLaunch(t, projectRoot, deps)

	assertOneIgnorePressOnThePrompt(t, presses)
	if run.envelope.Error.ErrorCode != clierrors.ErrorCodeUnityNotRunning || strings.Contains(run.stderr, "SafeModePromptIgnored") {
		t.Fatalf("ErrorCode = %q, want %s without SafeModePromptIgnored\n%s",
			run.envelope.Error.ErrorCode, clierrors.ErrorCodeUnityNotRunning, run.stderr)
	}
}

// Verifies a launch stopped at a dialog without pressing the Safe Mode prompt has no
// SafeModePromptIgnored key in its Details.
func TestLaunchDialogErrorWithoutTheSafeModePromptHasNoIgnoredDetail(t *testing.T) {
	err := asLaunchDialogError(blockedReadiness(launchTestDialog()), "/project", true, false, false)

	cliError := clierrors.ClassifyError(err, clierrors.ErrorContext{Command: clicore.LaunchCommandName})

	if _, found := cliError.Details["SafeModePromptIgnored"]; found {
		t.Fatalf("Details = %#v, want no SafeModePromptIgnored key", cliError.Details)
	}
}

// Verifies a launch that ran out of time without pressing the Safe Mode prompt has no
// SafeModePromptIgnored key in its Details.
func TestLaunchStartupTimeoutWithoutTheSafeModePromptHasNoIgnoredDetail(t *testing.T) {
	err := launchStartupTimeoutError{projectRoot: "/project"}

	cliError := err.ToCLIError(clierrors.ErrorContext{Command: clicore.LaunchCommandName})

	if _, found := cliError.Details["SafeModePromptIgnored"]; found {
		t.Fatalf("Details = %#v, want no SafeModePromptIgnored key", cliError.Details)
	}
}
