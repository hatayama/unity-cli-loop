package clicore

import (
	"io"
	"os"
	"path/filepath"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

// Verifies the not-running error records a lockfile left behind, which only a shutdown that did not
// finish leaves, along with the Editor log location.
func TestNewUnityNotRunningErrorRecordsALockfileLeftBehind(t *testing.T) {
	projectRoot := t.TempDir()
	lockfilePath := unityprocess.LockfilePath(projectRoot)
	if err := os.MkdirAll(filepath.Dir(lockfilePath), 0o755); err != nil {
		t.Fatalf("creating Temp failed: %v", err)
	}
	if err := os.WriteFile(lockfilePath, []byte{}, 0o644); err != nil {
		t.Fatalf("writing the lockfile failed: %v", err)
	}

	err := NewUnityNotRunningError(projectRoot, "/tmp/uloop/sample.sock", io.EOF, true)

	if !err.LockfileLeftBehind {
		t.Fatal("expected the lockfile left behind to be recorded")
	}
	if err.EditorLogPath != unityprocess.DefaultEditorLogPath() {
		t.Fatalf("EditorLogPath = %q, want the default Editor log path", err.EditorLogPath)
	}
	if err.ProjectRoot != projectRoot || err.Endpoint != "/tmp/uloop/sample.sock" || err.Cause != io.EOF ||
		!err.ExitedDuringCommand {
		t.Fatalf("the arguments were not carried over: %#v", err)
	}
}

// Verifies a project without a lockfile is reported as a normal shutdown.
func TestNewUnityNotRunningErrorWithoutALockfile(t *testing.T) {
	err := NewUnityNotRunningError(t.TempDir(), "", nil, false)

	if err.LockfileLeftBehind {
		t.Fatal("a project without a lockfile must not be reported as an abnormal exit")
	}
}
