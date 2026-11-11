package dispatcher

import (
	"bytes"
	"context"
	"errors"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

// errLaunchTestEditorStart stops a launch test at the point where launch would start an Editor.
var errLaunchTestEditorStart = errors.New("launch test stops before starting an Editor")

// unidentifiedEditorLaunchDeps returns launch dependencies whose ordinary lookup finds no Editor, as it does
// when the only running Editor's command line does not name its project, and whose definitive lookup answers
// definitiveProcess and definitiveErr. editorStarts counts the attempts to start an Editor.
func unidentifiedEditorLaunchDeps(
	definitiveProcess *clicore.UnityProcess,
	definitiveErr error,
) (launchDeps, *int, *unityStopRecorder) {
	deps, recorder := unityStopTestDeps(true)
	editorStarts := 0
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) { return nil, nil }
	deps.findRunningUnityProcessDefinitively = func(context.Context, string) (*clicore.UnityProcess, error) {
		return definitiveProcess, definitiveErr
	}
	deps.resolveUnityExecutablePath = func(string) (string, error) {
		editorStarts++
		return "", errLaunchTestEditorStart
	}
	return deps, &editorStarts, recorder
}

func runUnidentifiedEditorLaunch(t *testing.T, options launchOptions, deps launchDeps) (int, string, string) {
	t.Helper()
	// Why a versioned project: launch reads the Editor version before it resolves the Editor to start.
	projectRoot := createVersionedLaunchTestProject(t)
	options.projectPath = projectRoot
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runLaunchWithDeps(context.Background(), options, projectRoot, &stdout, &stderr, deps)
	return code, stdout.String(), stderr.String()
}

// Verifies quit fails instead of reporting success when an Editor that may be this project's cannot be
// identified, and that the error says why.
func TestRunLaunchQuitFailsWhenAnUnidentifiedEditorIsRunning(t *testing.T) {
	deps, _, recorder := unidentifiedEditorLaunchDeps(nil, unityprocess.ErrUnidentifiedUnityEditor)

	code, stdout, stderr := runUnidentifiedEditorLaunch(t, launchOptions{quit: true}, deps)

	if code != 1 {
		t.Fatalf("exit code = %d, want 1; stdout=%s stderr=%s", code, stdout, stderr)
	}
	if strings.Contains(stdout, `"Success": true`) || strings.Contains(stdout, launchNoProcessMessage) {
		t.Fatalf("stdout reports a successful quit: %s", stdout)
	}
	for _, expected := range []string{"UNITY_NOT_REACHABLE", "does not name its project"} {
		if !strings.Contains(stderr, expected) {
			t.Fatalf("stderr missing %q: %s", expected, stderr)
		}
	}
	if recorder.quitRequests != 0 || len(recorder.killedPids) != 0 {
		t.Fatalf("quit stopped an Editor: quit requests = %d, killed = %v", recorder.quitRequests, recorder.killedPids)
	}
}

// Verifies restart fails without starting a new Editor when an Editor that may be this project's cannot be
// identified, so two Editors never open the same project.
func TestRunLaunchRestartDoesNotStartAnEditorWhenAnUnidentifiedEditorIsRunning(t *testing.T) {
	deps, editorStarts, _ := unidentifiedEditorLaunchDeps(nil, unityprocess.ErrUnidentifiedUnityEditor)

	code, stdout, stderr := runUnidentifiedEditorLaunch(t, launchOptions{restart: true}, deps)

	if code != 1 {
		t.Fatalf("exit code = %d, want 1; stdout=%s stderr=%s", code, stdout, stderr)
	}
	if *editorStarts != 0 {
		t.Fatalf("restart tried to start an Editor %d time(s)", *editorStarts)
	}
	if !strings.Contains(stderr, "does not name its project") {
		t.Fatalf("stderr does not explain the unidentified Editor: %s", stderr)
	}
}

// Verifies quit still reports success when the definitive lookup proves no Editor can be this project's.
func TestRunLaunchQuitSucceedsWhenNoEditorCanBeTheProjects(t *testing.T) {
	deps, _, _ := unidentifiedEditorLaunchDeps(nil, nil)

	code, stdout, stderr := runUnidentifiedEditorLaunch(t, launchOptions{quit: true}, deps)

	if code != 0 {
		t.Fatalf("exit code = %d, want 0; stderr=%s", code, stderr)
	}
	if !strings.Contains(stdout, `"Success": true`) || !strings.Contains(stdout, launchNoProcessMessage) {
		t.Fatalf("stdout mismatch: %s", stdout)
	}
}

// Verifies plain launch keeps the ordinary lookup, so an unrelated Editor whose project is unknown does not
// stop it from starting this project's Editor.
func TestRunLaunchWithoutQuitOrRestartKeepsTheOrdinaryLookup(t *testing.T) {
	deps, editorStarts, _ := unidentifiedEditorLaunchDeps(nil, nil)
	definitiveCalls := 0
	deps.findRunningUnityProcessDefinitively = func(context.Context, string) (*clicore.UnityProcess, error) {
		definitiveCalls++
		return nil, unityprocess.ErrUnidentifiedUnityEditor
	}

	runUnidentifiedEditorLaunch(t, launchOptions{}, deps)

	if definitiveCalls != 0 {
		t.Fatalf("plain launch used the definitive lookup %d time(s)", definitiveCalls)
	}
	if *editorStarts != 1 {
		t.Fatalf("plain launch tried to start an Editor %d time(s), want 1", *editorStarts)
	}
}

// Verifies quit stops the Editor the definitive lookup finds for this project.
func TestRunLaunchQuitStopsTheEditorTheDefinitiveLookupFinds(t *testing.T) {
	deps, _, recorder := unidentifiedEditorLaunchDeps(&clicore.UnityProcess{Pid: 4321}, nil)

	code, stdout, stderr := runUnidentifiedEditorLaunch(t, launchOptions{quit: true}, deps)

	if code != 0 {
		t.Fatalf("exit code = %d, want 0; stdout=%s stderr=%s", code, stdout, stderr)
	}
	if recorder.quitRequests != 1 {
		t.Fatalf("quit requests = %d, want 1", recorder.quitRequests)
	}
	if !strings.Contains(stdout, `"Success": true`) {
		t.Fatalf("stdout mismatch: %s", stdout)
	}
}
