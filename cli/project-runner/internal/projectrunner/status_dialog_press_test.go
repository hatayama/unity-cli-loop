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

// Press output for DisplayDialogComplex when the named button was pressed: the values are the ones
// read before the press.
const accessibilityPressedOutput = `{"processFound":true,"dialogFound":true,"pressed":true,"windowTitle":"","texts":["Probe title","Line one. Line two."],"buttons":["Yes","No","Later"]}`

// Press output for the same dialog given a label it lacks, as osascript printed it in a real Editor.
const accessibilityUnpressedOutput = `{"processFound":true,"dialogFound":true,"pressed":false,"windowTitle":"","texts":["Probe title","Line one. Line two."],"buttons":["Yes","No","Later"]}`

var accessibilityPressProbeDialog = statusDialog{
	Title:   "Probe title",
	Message: "Line one. Line two.",
	Buttons: []string{"Yes", "No", "Later"},
}

func assertPressResult(t *testing.T, got dialogPressResult, err error, want dialogPressResult) {
	t.Helper()
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("result = %#v, want %#v", got, want)
	}
}

// Verifies a pressed alert is read as pressed, with its first text as the title and the rest as
// the message.
func TestPressResultFromAccessibilityOutputReadsThePressedDialog(t *testing.T) {
	got, err := pressResultFromAccessibilityOutput([]byte(accessibilityPressedOutput))

	assertPressResult(t, got, err, dialogPressResult{DialogFound: true, Pressed: true, Dialog: accessibilityPressProbeDialog})
}

// Verifies an alert without the label is reported as found but not pressed, with its content.
func TestPressResultFromAccessibilityOutputKeepsTheDialogWhenNothingWasPressed(t *testing.T) {
	got, err := pressResultFromAccessibilityOutput([]byte(accessibilityUnpressedOutput))

	assertPressResult(t, got, err, dialogPressResult{DialogFound: true, Pressed: false, Dialog: accessibilityPressProbeDialog})
}

// Verifies the unnamed frame buttons of a modal Editor window are dropped, leaving no buttons.
func TestPressResultFromAccessibilityOutputDropsUnnamedButtons(t *testing.T) {
	output := `{"processFound":true,"dialogFound":true,"pressed":false,"windowTitle":"Probe modal","texts":[],"buttons":[null,null,null]}`

	got, err := pressResultFromAccessibilityOutput([]byte(output))

	assertPressResult(t, got, err, dialogPressResult{
		DialogFound: true,
		Pressed:     false,
		Dialog:      statusDialog{Title: "Probe modal", Message: "", Buttons: []string{}},
	})
}

// Verifies output saying no modal dialog is open is a result without a dialog, not an error.
func TestPressResultFromAccessibilityOutputReportsNoDialog(t *testing.T) {
	got, err := pressResultFromAccessibilityOutput([]byte(`{"processFound":true,"dialogFound":false}`))

	assertPressResult(t, got, err, dialogPressResult{DialogFound: false})
}

// Verifies a missing Unity process and output that is not JSON are errors.
func TestPressResultFromAccessibilityOutputRejectsUnusableOutput(t *testing.T) {
	for _, output := range []string{`{"processFound":false}`, `execution error`} {
		t.Run(output, func(t *testing.T) {
			if _, err := pressResultFromAccessibilityOutput([]byte(output)); err == nil {
				t.Fatal("expected an error")
			}
		})
	}
}

// Verifies press runs the press script through osascript with the Unity process ID and the label
// as given.
func TestDialogPressRunsTheScriptWithThePidAndLabel(t *testing.T) {
	var gotArgs []string
	command := func(_ context.Context, name string, args ...string) ([]byte, error) {
		gotArgs = append([]string{name}, args...)
		return []byte(accessibilityPressedOutput), nil
	}
	inspector := dialogInspectorForTest(t, "darwin", unityProcessWithPID(4242), command)

	got, err := inspector.press(context.Background(), "/project", "Later")

	assertPressResult(t, got, err, dialogPressResult{DialogFound: true, Pressed: true, Dialog: accessibilityPressProbeDialog})
	want := []string{"osascript", "-l", "JavaScript", "-e", accessibilityPressScript, "4242", "Later"}
	if !reflect.DeepEqual(gotArgs, want) {
		t.Fatalf("command = %q, want %q", gotArgs, want)
	}
}

// Verifies pressing is supported on macOS and Windows only: elsewhere press fails without a
// process lookup, a command, a window listing, or a press, and names the two systems.
func TestDialogPressIsSupportedOnMacOSAndWindowsOnly(t *testing.T) {
	for _, goos := range []string{"darwin", "windows"} {
		if !dialogInspectorForTest(t, goos, failingProcessLookup(t), failingCommand(t)).canPress() {
			t.Fatalf("canPress must be true on %s", goos)
		}
	}

	inspector := dialogInspectorForTest(t, "linux", failingProcessLookup(t), failingCommand(t))
	if inspector.canPress() {
		t.Fatal("canPress must be false on linux")
	}
	_, err := inspector.press(context.Background(), "/project", "Later")
	assertPressNotStarted(t, err, true)
	if !strings.Contains(err.Error(), "only on macOS and Windows") {
		t.Fatalf("error = %v, want it to name macOS and Windows", err)
	}
}

// Verifies press fails without starting osascript when the Unity process cannot be found.
func TestDialogPressFailsWithoutAUnityProcess(t *testing.T) {
	lookups := map[string]func(context.Context, string) (*unityprocess.UnityProcess, error){
		"lookup fails": func(context.Context, string) (*unityprocess.UnityProcess, error) {
			return nil, errors.New("ps refused")
		},
		"not running": func(context.Context, string) (*unityprocess.UnityProcess, error) {
			return nil, nil
		},
	}
	for name, lookup := range lookups {
		t.Run(name, func(t *testing.T) {
			inspector := dialogInspectorForTest(t, "darwin", lookup, failingCommand(t))

			_, err := inspector.press(context.Background(), "/project", "Later")
			assertPressNotStarted(t, err, true)
		})
	}
}

// Verifies a failing, timed-out, or cancelled osascript each produce an error that says which.
func TestDialogPressReportsWhyTheScriptStopped(t *testing.T) {
	t.Run("fails", func(t *testing.T) {
		command := func(context.Context, string, ...string) ([]byte, error) {
			return nil, errors.New("exit status 1: osascript is not allowed assistive access. (-25211)")
		}
		inspector := dialogInspectorForTest(t, "darwin", unityProcessWithPID(4242), command)

		_, err := inspector.press(context.Background(), "/project", "Later")

		if err == nil || !strings.Contains(err.Error(), "not allowed assistive access") {
			t.Fatalf("error = %v, want the osascript error text", err)
		}
		assertPressNotStarted(t, err, false)
	})
	t.Run("times out", func(t *testing.T) {
		command := func(ctx context.Context, _ string, _ ...string) ([]byte, error) {
			<-ctx.Done()
			return nil, errors.New("signal: killed")
		}
		inspector := dialogInspectorForTest(t, "darwin", unityProcessWithPID(4242), command)
		inspector.timeout = 50 * time.Millisecond

		_, err := inspector.press(context.Background(), "/project", "Later")

		if err == nil || !strings.Contains(err.Error(), "did not answer within 50ms") {
			t.Fatalf("error = %v, want a timeout error", err)
		}
		assertPressNotStarted(t, err, false)
	})
	t.Run("cancelled", func(t *testing.T) {
		ctx, cancel := context.WithCancel(context.Background())
		command := func(context.Context, string, ...string) ([]byte, error) {
			cancel()
			return nil, errors.New("signal: killed")
		}
		inspector := dialogInspectorForTest(t, "darwin", unityProcessWithPID(4242), command)

		_, err := inspector.press(ctx, "/project", "Later")

		if !errors.Is(err, context.Canceled) || strings.Contains(err.Error(), "did not answer") {
			t.Fatalf("error = %v, want a cancellation", err)
		}
		assertPressNotStarted(t, err, false)
	})
}

// assertPressNotStarted checks whether err says the press failed before osascript started.
func assertPressNotStarted(t *testing.T, err error, want bool) {
	t.Helper()
	if err == nil {
		t.Fatal("expected an error")
	}
	var notStarted dialogPressNotStartedError
	if got := errors.As(err, &notStarted); got != want {
		t.Fatalf("not started = %v, want %v: %v", got, want, err)
	}
}

// Verifies the hint added to a failed press names the macOS Accessibility setting without
// describing a failed look, and is empty where no permission is involved.
func TestDialogInspectorAnswerFailedHintNamesTheAccessibilitySetting(t *testing.T) {
	macOS := dialogInspector{goos: "darwin"}.answerFailedHint()
	if !strings.Contains(macOS, "Accessibility access") || strings.Contains(macOS, "could not look") {
		t.Fatalf("macOS hint = %q, want the Accessibility setting without the look failure", macOS)
	}
	if windows := (dialogInspector{goos: "windows"}).answerFailedHint(); windows != "" {
		t.Fatalf("Windows hint = %q, want none", windows)
	}
}

// win32PressInspectorForTest is the inspector as it presses on Windows: it lists windows, hands
// the chosen button to press, and must not start a command.
func win32PressInspectorForTest(
	t *testing.T,
	listWindows func(context.Context, int) ([]win32Window, error),
	press func(context.Context, win32PressTargetResult) error,
) dialogInspector {
	t.Helper()
	inspector := win32DialogInspectorForTest(t, unityProcessWithPID(4242), listWindows)
	inspector.pressWindowButton = press
	return inspector
}

func listingWin32Windows(windows []win32Window) func(context.Context, int) ([]win32Window, error) {
	return func(context.Context, int) ([]win32Window, error) {
		return windows, nil
	}
}

// Verifies that on Windows press lists the windows of the Unity process ID, hands the button
// chosen for the label to the press exactly once, and returns the dialog read before the press.
func TestDialogPressOnWindowsPressesTheLabeledButton(t *testing.T) {
	windows := win32ProbeDialogWindows()
	var listedPID int
	listWindows := func(_ context.Context, pid int) ([]win32Window, error) {
		listedPID = pid
		return windows, nil
	}
	var pressed []win32PressTargetResult
	press := func(_ context.Context, target win32PressTargetResult) error {
		pressed = append(pressed, target)
		return nil
	}
	inspector := win32PressInspectorForTest(t, listWindows, press)

	got, err := inspector.press(context.Background(), "/project", "Later")

	assertPressResult(t, got, err, dialogPressResult{DialogFound: true, Pressed: true, Dialog: accessibilityPressProbeDialog})
	if listedPID != 4242 {
		t.Fatalf("listed process ID = %d, want 4242", listedPID)
	}
	want := []win32PressTargetResult{win32PressTarget(windows, "Later")}
	if !reflect.DeepEqual(pressed, want) || pressed[0].ButtonHandle != win32ProbeLaterButtonHandle {
		t.Fatalf("pressed = %#v, want one press of the Later button %#v", pressed, want)
	}
}

// Verifies that on Windows nothing is pressed without a button to press: a label the dialog
// lacks, no dialog at all, and a dialog that has no button each return what was read.
func TestDialogPressOnWindowsPressesNothingWithoutAMatch(t *testing.T) {
	modal := win32ModalWindow("UnityContainerWndClass", "Probe modal",
		win32Control{ClassName: "UnityGUIViewWndClass", Text: "UnityEditor.EditorWindow", Visible: true})
	cases := []struct {
		name    string
		windows []win32Window
		label   string
		want    dialogPressResult
	}{
		{
			name:    "label the dialog lacks",
			windows: win32ProbeDialogWindows(),
			label:   "later",
			want:    dialogPressResult{DialogFound: true, Pressed: false, Dialog: accessibilityPressProbeDialog},
		},
		{
			name:    "no dialog",
			windows: []win32Window{win32EnabledMainWindow, win32InputMethodWindow},
			label:   "Later",
			want:    dialogPressResult{DialogFound: false},
		},
		{
			name:    "dialog without buttons",
			windows: []win32Window{modal, win32DisabledMainWindow},
			label:   "OK",
			want: dialogPressResult{
				DialogFound: true,
				Pressed:     false,
				Dialog:      statusDialog{Title: "Probe modal", Message: "", Buttons: []string{}},
			},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			inspector := win32PressInspectorForTest(t, listingWin32Windows(testCase.windows), failingWindowButtonPress(t))

			got, err := inspector.press(context.Background(), "/project", testCase.label)

			assertPressResult(t, got, err, testCase.want)
		})
	}
}

// Verifies that on Windows a failure before the press starts says so and presses nothing: the
// Unity process is not found, or its windows cannot be listed, in time or at all.
func TestDialogPressOnWindowsFailsBeforeThePressStarts(t *testing.T) {
	t.Run("process lookup fails", func(t *testing.T) {
		lookup := func(context.Context, string) (*unityprocess.UnityProcess, error) {
			return nil, errors.New("process list refused")
		}
		inspector := win32DialogInspectorForTest(t, lookup, failingWindowList(t))

		_, err := inspector.press(context.Background(), "/project", "Later")

		assertPressNotStarted(t, err, true)
	})
	t.Run("window listing fails", func(t *testing.T) {
		listWindows := func(context.Context, int) ([]win32Window, error) {
			return nil, errors.New("EnumWindows failed: access is denied")
		}
		inspector := win32PressInspectorForTest(t, listWindows, failingWindowButtonPress(t))

		_, err := inspector.press(context.Background(), "/project", "Later")

		assertPressNotStarted(t, err, true)
		if !strings.Contains(err.Error(), "EnumWindows failed: access is denied") {
			t.Fatalf("error = %v, want the listing failure", err)
		}
	})
	t.Run("window listing times out", func(t *testing.T) {
		listWindows := func(ctx context.Context, _ int) ([]win32Window, error) {
			<-ctx.Done()
			return nil, ctx.Err()
		}
		inspector := win32PressInspectorForTest(t, listWindows, failingWindowButtonPress(t))
		inspector.timeout = 50 * time.Millisecond

		_, err := inspector.press(context.Background(), "/project", "Later")

		assertPressNotStarted(t, err, true)
		if !strings.Contains(err.Error(), "reading Unity's windows did not finish within 50ms") {
			t.Fatalf("error = %v, want a timeout error", err)
		}
	})
}

// Verifies that on Windows a press that fails, runs out of time, or is cancelled is an error that
// does not claim the press never started, because the click may have reached the dialog.
func TestDialogPressOnWindowsReportsAFailedPress(t *testing.T) {
	t.Run("fails", func(t *testing.T) {
		press := func(context.Context, win32PressTargetResult) error {
			return errors.New("the dialog closed before the button was pressed")
		}
		inspector := win32PressInspectorForTest(t, listingWin32Windows(win32ProbeDialogWindows()), press)

		_, err := inspector.press(context.Background(), "/project", "Later")

		assertPressNotStarted(t, err, false)
		if !strings.Contains(err.Error(), "the dialog closed before the button was pressed") {
			t.Fatalf("error = %v, want the press failure", err)
		}
	})
	t.Run("times out", func(t *testing.T) {
		press := func(ctx context.Context, _ win32PressTargetResult) error {
			<-ctx.Done()
			return errors.New("the dialog did not answer")
		}
		inspector := win32PressInspectorForTest(t, listingWin32Windows(win32ProbeDialogWindows()), press)
		inspector.timeout = 50 * time.Millisecond

		_, err := inspector.press(context.Background(), "/project", "Later")

		assertPressNotStarted(t, err, false)
		if !strings.Contains(err.Error(), "the dialog did not answer the press within 50ms") {
			t.Fatalf("error = %v, want a timeout error", err)
		}
	})
	t.Run("cancelled", func(t *testing.T) {
		ctx, cancel := context.WithCancel(context.Background())
		press := func(context.Context, win32PressTargetResult) error {
			cancel()
			return errors.New("the dialog did not answer")
		}
		inspector := win32PressInspectorForTest(t, listingWin32Windows(win32ProbeDialogWindows()), press)

		_, err := inspector.press(ctx, "/project", "Later")

		assertPressNotStarted(t, err, false)
		if !errors.Is(err, context.Canceled) {
			t.Fatalf("error = %v, want a cancellation", err)
		}
	})
}
