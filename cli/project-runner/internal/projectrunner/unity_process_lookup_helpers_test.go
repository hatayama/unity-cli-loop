package projectrunner

import (
	"context"
	"os"
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
