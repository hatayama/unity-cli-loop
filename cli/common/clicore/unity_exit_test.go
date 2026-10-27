package clicore

import (
	"context"
	"errors"
	"io"
	"os"
	"path/filepath"
	"syscall"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

// unityExitWatchFixture drives a UnityExitWatch with a hand-moved clock and a scripted process lookup.
type unityExitWatchFixture struct {
	now     time.Time
	running bool
	lookErr error
	lookups int
	watch   *UnityExitWatch
}

func newUnityExitWatchFixture(t *testing.T) *unityExitWatchFixture {
	t.Helper()
	fixture := &unityExitWatchFixture{now: time.Unix(1_700_000_000, 0), running: true}
	fixture.watch = NewUnityExitWatch(
		t.TempDir(),
		"/tmp/uloop/sample.sock",
		func(context.Context, string) (*unityprocess.UnityProcess, error) {
			fixture.lookups++
			if fixture.lookErr != nil {
				return nil, fixture.lookErr
			}
			if fixture.running {
				return &unityprocess.UnityProcess{Pid: 4321}, nil
			}
			return nil, nil
		},
		func() time.Time { return fixture.now },
	)
	return fixture
}

func (fixture *unityExitWatchFixture) observe(pollErr error) error {
	return fixture.watch.Observe(context.Background(), pollErr)
}

func (fixture *unityExitWatchFixture) advance(duration time.Duration) {
	fixture.now = fixture.now.Add(duration)
}

func serverGonePollError() error {
	return &unityipc.ConnectionAttemptError{Cause: syscall.ECONNREFUSED}
}

// Verifies the watch reports Unity as gone only after two lookups, spaced at least the confirm
// interval apart, both find no process, and that the report says Unity went away during the command.
func TestUnityExitWatchReportsAfterTwoSpacedLookupsFindNoProcess(t *testing.T) {
	fixture := newUnityExitWatchFixture(t)
	fixture.running = false

	if err := fixture.observe(serverGonePollError()); err != nil {
		t.Fatalf("one lookup must not be enough, got %v", err)
	}
	fixture.advance(unityExitConfirmInterval - time.Millisecond)
	if err := fixture.observe(serverGonePollError()); err != nil {
		t.Fatalf("a poll inside the confirm interval must not confirm, got %v", err)
	}
	fixture.advance(time.Millisecond)
	err := fixture.observe(serverGonePollError())

	var notRunning clierrors.UnityNotRunningError
	if !errors.As(err, &notRunning) {
		t.Fatalf("expected UnityNotRunningError after the confirming lookup, got %v", err)
	}
	if !notRunning.ExitedDuringCommand {
		t.Fatal("a wait loop's report must say Unity went away during the command")
	}
	if fixture.lookups != 2 {
		t.Fatalf("lookups = %d, want 2 (the poll inside the interval must not look up)", fixture.lookups)
	}
}

// Verifies a running Editor is looked up at most once per confirm interval, so a wait that polls every
// few milliseconds through a long domain reload does not list processes on every poll.
func TestUnityExitWatchThrottlesLookupsWhileUnityRuns(t *testing.T) {
	fixture := newUnityExitWatchFixture(t)

	for poll := 0; poll < 10; poll++ {
		if err := fixture.observe(serverGonePollError()); err != nil {
			t.Fatalf("a running Editor must never be reported as gone, got %v", err)
		}
		fixture.advance(unityExitConfirmInterval / 4)
	}

	if fixture.lookups != 3 {
		t.Fatalf("lookups = %d, want 3 over 2.5 confirm intervals", fixture.lookups)
	}
}

// Verifies each kind of evidence that the Editor may still be there restarts the confirmation: a process
// found, a lookup that failed, a poll that succeeded, and a poll error that a live server also produces.
func TestUnityExitWatchRestartsTheConfirmationOnEvidenceOfALiveEditor(t *testing.T) {
	cases := []struct {
		name       string
		interrupt  func(fixture *unityExitWatchFixture) error
		wantLookup bool
	}{
		{
			name: "process found",
			interrupt: func(fixture *unityExitWatchFixture) error {
				fixture.running = true
				defer func() { fixture.running = false }()
				return fixture.observe(serverGonePollError())
			},
		},
		{
			name: "lookup failed",
			interrupt: func(fixture *unityExitWatchFixture) error {
				fixture.lookErr = errors.New("sysctl kern.proc.all: operation not permitted")
				defer func() { fixture.lookErr = nil }()
				return fixture.observe(serverGonePollError())
			},
		},
		{
			name: "poll succeeded",
			interrupt: func(fixture *unityExitWatchFixture) error {
				return fixture.observe(nil)
			},
		},
		{
			name: "dial timed out",
			interrupt: func(fixture *unityExitWatchFixture) error {
				return fixture.observe(&unityipc.ConnectionAttemptError{Cause: os.ErrDeadlineExceeded})
			},
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			fixture := newUnityExitWatchFixture(t)
			fixture.running = false
			if err := fixture.observe(serverGonePollError()); err != nil {
				t.Fatalf("one lookup must not be enough, got %v", err)
			}
			fixture.advance(unityExitConfirmInterval)
			if err := testCase.interrupt(fixture); err != nil {
				t.Fatalf("the interruption must not report Unity as gone, got %v", err)
			}
			fixture.advance(unityExitConfirmInterval)
			if err := fixture.observe(serverGonePollError()); err != nil {
				t.Fatalf("the lookup after the interruption must start a new confirmation, got %v", err)
			}
			fixture.advance(unityExitConfirmInterval)
			if err := fixture.observe(serverGonePollError()); err == nil {
				t.Fatal("two spaced lookups after the interruption must report Unity as gone")
			}
		})
	}
}

// Verifies a dropped connection counts as the server going away, the same as a dial nobody answered.
func TestUnityExitWatchTreatsADroppedConnectionAsTheServerGoingAway(t *testing.T) {
	fixture := newUnityExitWatchFixture(t)
	fixture.running = false

	_ = fixture.observe(io.EOF)
	fixture.advance(unityExitConfirmInterval)
	if err := fixture.observe(io.EOF); err == nil {
		t.Fatal("two spaced lookups after dropped connections must report Unity as gone")
	}
}

// writeProjectEditorLogForTest points Unity's default log location at a temporary home and writes a log
// there that records projectRoot as its project, returning the log's path.
func writeProjectEditorLogForTest(t *testing.T, projectRoot string) string {
	t.Helper()
	home := t.TempDir()
	t.Setenv("HOME", home)
	t.Setenv("USERPROFILE", home)
	t.Setenv("LOCALAPPDATA", home)
	editorLogPath := unityprocess.DefaultEditorLogPath()
	if err := os.MkdirAll(filepath.Dir(editorLogPath), 0o755); err != nil {
		t.Fatalf("creating the log directory failed: %v", err)
	}
	content := "COMMAND LINE ARGUMENTS:\nSuccessfully changed project path to: " + projectRoot + "\n"
	if err := os.WriteFile(editorLogPath, []byte(content), 0o644); err != nil {
		t.Fatalf("writing the Editor log failed: %v", err)
	}
	return editorLogPath
}

// Verifies the not-running error records a lockfile left behind, which only a shutdown that did not
// finish leaves, along with the log that Editor wrote.
func TestNewUnityNotRunningErrorRecordsALockfileLeftBehind(t *testing.T) {
	projectRoot := t.TempDir()
	lockfilePath := unityprocess.LockfilePath(projectRoot)
	if err := os.MkdirAll(filepath.Dir(lockfilePath), 0o755); err != nil {
		t.Fatalf("creating Temp failed: %v", err)
	}
	if err := os.WriteFile(lockfilePath, []byte{}, 0o644); err != nil {
		t.Fatalf("writing the lockfile failed: %v", err)
	}

	editorLogPath := writeProjectEditorLogForTest(t, projectRoot)

	err := NewUnityNotRunningError(projectRoot, "/tmp/uloop/sample.sock", io.EOF, true)

	if !err.LockfileLeftBehind {
		t.Fatal("expected the lockfile left behind to be recorded")
	}
	if err.EditorLogPath != editorLogPath {
		t.Fatalf("EditorLogPath = %q, want the project's Editor log %q", err.EditorLogPath, editorLogPath)
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
