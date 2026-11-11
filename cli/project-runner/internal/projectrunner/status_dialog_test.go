package projectrunner

import (
	"context"
	"errors"
	"reflect"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

// Accessibility output for EditorUtility.DisplayDialog, as osascript printed it in a real Editor.
const accessibilityDisplayDialogOutput = `{"processFound":true,"dialogs":[{"windowTitle":"","texts":["uloop spike","Spike dialog body text"],"buttons":["Abort","Proceed"]}]}`

// Verifies each dialog shape read from Unity's windows becomes a title, a message, and the named
// buttons: an alert's first text is its title, and a window's own title is not repeated as text.
func TestDialogsFromAccessibilityOutputUsesFirstTextAsTitleWhenWindowHasNoTitle(t *testing.T) {
	cases := []struct {
		name   string
		output string
		want   []statusDialog
	}{
		{
			name:   "alert without a window title",
			output: accessibilityDisplayDialogOutput,
			want:   []statusDialog{{Title: "uloop spike", Message: "Spike dialog body text", Buttons: []string{"Abort", "Proceed"}}},
		},
		{
			name:   "three buttons and a message with a line break",
			output: `{"processFound":true,"dialogs":[{"windowTitle":"","texts":["Probe title","Line one\nLine two"],"buttons":["Yes","No","Later"]}]}`,
			want:   []statusDialog{{Title: "Probe title", Message: "Line one\nLine two", Buttons: []string{"Yes", "No", "Later"}}},
		},
		{
			name:   "file picker with only a window title",
			output: `{"processFound":true,"dialogs":[{"windowTitle":"Spike pick","texts":[],"buttons":[]}]}`,
			want:   []statusDialog{{Title: "Spike pick", Message: "", Buttons: []string{}}},
		},
		{
			name:   "modal Editor window repeating its title and unnamed window buttons",
			output: `{"processFound":true,"dialogs":[{"windowTitle":"Spike modal","texts":["Spike modal"],"buttons":[null,null,null]}]}`,
			want:   []statusDialog{{Title: "Spike modal", Message: "", Buttons: []string{}}},
		},
		{
			name:   "window title and a separate body text",
			output: `{"processFound":true,"dialogs":[{"windowTitle":"Spike modal","texts":["Spike modal","Body"],"buttons":[]}]}`,
			want:   []statusDialog{{Title: "Spike modal", Message: "Body", Buttons: []string{}}},
		},
		{
			name:   "null window title and nothing else",
			output: `{"processFound":true,"dialogs":[{"windowTitle":null,"texts":[],"buttons":[]}]}`,
			want:   []statusDialog{{Title: "", Message: "", Buttons: []string{}}},
		},
		{
			name:   "empty and null texts are dropped",
			output: `{"processFound":true,"dialogs":[{"windowTitle":"","texts":[null,"Title","","Body",null],"buttons":["","OK"]}]}`,
			want:   []statusDialog{{Title: "Title", Message: "Body", Buttons: []string{"OK"}}},
		},
		{
			name: "two dialogs keep their order",
			output: `{"processFound":true,"dialogs":[` +
				`{"windowTitle":"","texts":["First"],"buttons":["OK"]},` +
				`{"windowTitle":"Second","texts":[],"buttons":[]}]}`,
			want: []statusDialog{
				{Title: "First", Message: "", Buttons: []string{"OK"}},
				{Title: "Second", Message: "", Buttons: []string{}},
			},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			got, err := dialogsFromAccessibilityOutput([]byte(testCase.output))
			if err != nil {
				t.Fatalf("dialogsFromAccessibilityOutput returned error: %v", err)
			}
			if !reflect.DeepEqual(got, testCase.want) {
				t.Fatalf("dialogs = %#v, want %#v", got, testCase.want)
			}
		})
	}
}

// Verifies a Unity process with no dialog window gives an empty, non-nil list and no error.
func TestDialogsFromAccessibilityOutputReturnsNoDialogs(t *testing.T) {
	got, err := dialogsFromAccessibilityOutput([]byte(`{"processFound":true,"dialogs":[]}`))
	if err != nil {
		t.Fatalf("dialogsFromAccessibilityOutput returned error: %v", err)
	}
	if got == nil || len(got) != 0 {
		t.Fatalf("dialogs = %#v, want an empty non-nil slice", got)
	}
}

// Verifies output saying System Events does not list the process is an error, not "no dialog".
func TestDialogsFromAccessibilityOutputFailsWhenProcessIsMissing(t *testing.T) {
	_, err := dialogsFromAccessibilityOutput([]byte(`{"processFound":false,"dialogs":[]}`))
	if err == nil || !strings.Contains(err.Error(), "not listed by System Events") {
		t.Fatalf("error = %v, want one saying System Events does not list the process", err)
	}
}

// Verifies output that is not the expected JSON is an error.
func TestDialogsFromAccessibilityOutputFailsOnUnreadableOutput(t *testing.T) {
	_, err := dialogsFromAccessibilityOutput([]byte("execution error: something went wrong"))
	if err == nil || !strings.Contains(err.Error(), "unreadable osascript output") {
		t.Fatalf("error = %v, want an unreadable output error", err)
	}
}

// Verifies the inspector reports that it cannot look on an operating system other than macOS and
// Windows, before it looks up the Unity process, starts a command, or lists windows.
func TestDialogInspectorIsUnsupportedOutsideMacOSAndWindows(t *testing.T) {
	for _, goos := range []string{"linux", "freebsd"} {
		t.Run(goos, func(t *testing.T) {
			inspector := dialogInspectorForTest(t, goos, failingProcessLookup(t), failingCommand(t))

			_, err := inspector.inspect(context.Background(), "/project")

			if !errors.Is(err, errDialogInspectionUnsupported) {
				t.Fatalf("error = %v, want errDialogInspectionUnsupported", err)
			}
		})
	}
}

// Verifies a failed Unity process lookup is reported with its cause and no command is started.
func TestDialogInspectorFailsWhenProcessLookupFails(t *testing.T) {
	lookup := func(context.Context, string) (*unityprocess.UnityProcess, error) {
		return nil, errors.New("ps refused")
	}
	inspector := dialogInspectorForTest(t, "darwin", lookup, failingCommand(t))

	_, err := inspector.inspect(context.Background(), "/project")

	if err == nil || !strings.Contains(err.Error(), "ps refused") {
		t.Fatalf("error = %v, want the lookup failure", err)
	}
}

// Verifies the inspector reports that no Unity process runs, and starts no command.
func TestDialogInspectorFailsWhenUnityIsNotRunning(t *testing.T) {
	lookup := func(context.Context, string) (*unityprocess.UnityProcess, error) {
		return nil, nil
	}
	inspector := dialogInspectorForTest(t, "darwin", lookup, failingCommand(t))

	_, err := inspector.inspect(context.Background(), "/project")

	if err == nil || !strings.Contains(err.Error(), "no Unity process runs for this project") {
		t.Fatalf("error = %v, want one saying Unity is not running", err)
	}
}

// Verifies a failing osascript is reported with its own error text, which explains a denied
// Accessibility permission.
func TestDialogInspectorFailsWhenOsascriptFails(t *testing.T) {
	command := func(context.Context, string, ...string) ([]byte, error) {
		return nil, errors.New("exit status 1: osascript is not allowed assistive access. (-25211)")
	}
	inspector := dialogInspectorForTest(t, "darwin", unityProcessWithPID(4242), command)

	_, err := inspector.inspect(context.Background(), "/project")

	if err == nil || !strings.Contains(err.Error(), "not allowed assistive access") {
		t.Fatalf("error = %v, want the osascript error text", err)
	}
}

// Verifies an osascript that does not answer in time is stopped and reported as a timeout,
// not as the kill signal that stopped it.
func TestDialogInspectorFailsWhenOsascriptTimesOut(t *testing.T) {
	command := func(ctx context.Context, _ string, _ ...string) ([]byte, error) {
		<-ctx.Done()
		return nil, errors.New("signal: killed")
	}
	inspector := dialogInspectorForTest(t, "darwin", unityProcessWithPID(4242), command)
	inspector.timeout = 50 * time.Millisecond

	_, err := inspector.inspect(context.Background(), "/project")

	if err == nil || !strings.Contains(err.Error(), "osascript did not answer within 50ms") {
		t.Fatalf("error = %v, want a timeout error", err)
	}
}

// Verifies a look stopped because the caller cancelled is reported as cancelled, not as a timeout.
func TestDialogInspectorReportsCancellation(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	command := func(context.Context, string, ...string) ([]byte, error) {
		cancel()
		return nil, errors.New("signal: killed")
	}
	inspector := dialogInspectorForTest(t, "darwin", unityProcessWithPID(4242), command)

	_, err := inspector.inspect(ctx, "/project")

	if !errors.Is(err, context.Canceled) || strings.Contains(err.Error(), "did not answer") {
		t.Fatalf("error = %v, want a cancellation", err)
	}
}

// Verifies a failing command's stderr text is part of the error, and a passing command's stdout
// is returned.
func TestRunCommandCapturingStderrReportsStderr(t *testing.T) {
	output, err := runCommandCapturingStderr(context.Background(), "go", "env", "GOOS")
	if err != nil || strings.TrimSpace(string(output)) == "" {
		t.Fatalf("go env GOOS = %q, %v; want output and no error", output, err)
	}

	_, err = runCommandCapturingStderr(context.Background(), "go", "no-such-subcommand")
	if err == nil || !strings.Contains(err.Error(), "unknown command") {
		t.Fatalf("error = %v, want go's stderr text", err)
	}
}

// Verifies the inspector runs the JXA script against the Unity process ID and returns the
// dialogs it read.
func TestDialogInspectorReadsDialogsFromOsascript(t *testing.T) {
	var gotName string
	var gotArgs []string
	command := func(_ context.Context, name string, args ...string) ([]byte, error) {
		gotName, gotArgs = name, args
		return []byte(accessibilityDisplayDialogOutput), nil
	}
	inspector := dialogInspectorForTest(t, "darwin", unityProcessWithPID(4242), command)

	dialogs, err := inspector.inspect(context.Background(), "/project")
	if err != nil {
		t.Fatalf("inspect returned error: %v", err)
	}
	wantArgs := []string{"-l", "JavaScript", "-e", accessibilityDialogScript, "4242"}
	if gotName != "osascript" || !reflect.DeepEqual(gotArgs, wantArgs) {
		t.Fatalf("command = %q %q, want osascript %q", gotName, gotArgs, wantArgs)
	}
	want := []statusDialog{{Title: "uloop spike", Message: "Spike dialog body text", Buttons: []string{"Abort", "Proceed"}}}
	if !reflect.DeepEqual(dialogs, want) {
		t.Fatalf("dialogs = %#v, want %#v", dialogs, want)
	}
}

func dialogInspectorForTest(
	t *testing.T,
	goos string,
	lookup func(context.Context, string) (*unityprocess.UnityProcess, error),
	command func(context.Context, string, ...string) ([]byte, error),
) dialogInspector {
	t.Helper()
	return dialogInspector{
		goos:          goos,
		timeout:       2 * time.Second,
		lookupProcess: lookup,
		runCommand:    command,
		listWindows:   failingWindowList(t),
		// Only a test of the press on Windows replaces this.
		pressWindowButton: failingWindowButtonPress(t),
	}
}

// win32DialogInspectorForTest is the inspector as it runs on Windows: it lists windows and must
// not start a command.
func win32DialogInspectorForTest(
	t *testing.T,
	lookup func(context.Context, string) (*unityprocess.UnityProcess, error),
	listWindows func(context.Context, int) ([]win32Window, error),
) dialogInspector {
	t.Helper()
	inspector := dialogInspectorForTest(t, "windows", lookup, failingCommand(t))
	inspector.listWindows = listWindows
	return inspector
}

func failingWindowList(t *testing.T) func(context.Context, int) ([]win32Window, error) {
	return func(context.Context, int) ([]win32Window, error) {
		t.Error("the inspector must not list windows here")
		return nil, errors.New("unexpected window listing")
	}
}

func failingWindowButtonPress(t *testing.T) func(context.Context, win32PressTargetResult) error {
	return func(context.Context, win32PressTargetResult) error {
		t.Error("the inspector must not press a window's button here")
		return errors.New("unexpected window button press")
	}
}

func failingProcessLookup(t *testing.T) func(context.Context, string) (*unityprocess.UnityProcess, error) {
	return func(context.Context, string) (*unityprocess.UnityProcess, error) {
		t.Error("the inspector must not look up the Unity process here")
		return nil, errors.New("unexpected process lookup")
	}
}

func failingCommand(t *testing.T) func(context.Context, string, ...string) ([]byte, error) {
	return func(context.Context, string, ...string) ([]byte, error) {
		t.Error("the inspector must not start a command here")
		return nil, errors.New("unexpected command")
	}
}

func unityProcessWithPID(pid int) func(context.Context, string) (*unityprocess.UnityProcess, error) {
	return func(context.Context, string) (*unityprocess.UnityProcess, error) {
		return &unityprocess.UnityProcess{Pid: pid}, nil
	}
}
