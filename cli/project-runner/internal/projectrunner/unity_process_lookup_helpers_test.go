package projectrunner

import (
	"context"
	"errors"
	"os"
	"reflect"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

// useUnityProcessLookup replaces the default Editor process lookup for one test.
func useUnityProcessLookup(
	t *testing.T,
	lookup func(context.Context, string) (*unityprocess.UnityProcess, error),
) {
	t.Helper()
	original := unityProcessLookup
	unityProcessLookup = lookup
	t.Cleanup(func() {
		unityProcessLookup = original
	})
}

// useRunningUnityProcess makes the default lookup find an Editor, as a scripted server stands in for one.
// Why this test process's own pid: a focus attempt on it can never raise another application's window.
// The default focus is still the real one, so a test whose command ends in a dial that never reaches
// the server should also stub focusUnityProcess, or it waits out a real focus attempt.
func useRunningUnityProcess(t *testing.T) {
	t.Helper()
	useUnityProcessLookup(t, runningUnityProcessLookup)
}

func runningUnityProcessLookup(context.Context, string) (*unityprocess.UnityProcess, error) {
	return &unityprocess.UnityProcess{Pid: os.Getpid()}, nil
}

func noUnityProcessLookup(context.Context, string) (*unityprocess.UnityProcess, error) {
	return nil, nil
}

// Verifies the lookup behind every default dependency set, status included, is the one that reports an
// Editor of unknown project instead of "none", since those callers conclude from "none" that Unity is
// not running.
func TestDefaultUnityProcessLookupDoesNotTakeAnUnidentifiedEditorAsNone(t *testing.T) {
	if reflect.ValueOf(unityProcessLookup).Pointer() != reflect.ValueOf(unityprocess.FindRunningUnityProcessDefinitively).Pointer() {
		t.Fatal("the default lookup must be unityprocess.FindRunningUnityProcessDefinitively")
	}
	useUnityProcessLookup(t, func(context.Context, string) (*unityprocess.UnityProcess, error) {
		return nil, unityprocess.ErrUnidentifiedUnityEditor
	})

	_, err := defaultStatusCommandDeps().isUnityProcessRunning(context.Background(), t.TempDir())

	if !errors.Is(err, unityprocess.ErrUnidentifiedUnityEditor) {
		t.Fatalf("status lookup err = %v, want the default lookup's answer", err)
	}
}
