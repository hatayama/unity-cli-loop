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

// Verifies pressing is macOS-only: elsewhere press fails without a process lookup or a command.
func TestDialogPressIsMacOSOnly(t *testing.T) {
	for _, goos := range []string{"windows", "linux"} {
		t.Run(goos, func(t *testing.T) {
			inspector := dialogInspectorForTest(t, goos, failingProcessLookup(t), failingCommand(t))

			if inspector.canPress() {
				t.Fatal("canPress must be false")
			}
			if _, err := inspector.press(context.Background(), "/project", "Later"); err == nil {
				t.Fatal("expected an error")
			}
		})
	}
	if !dialogInspectorForTest(t, "darwin", failingProcessLookup(t), failingCommand(t)).canPress() {
		t.Fatal("canPress must be true on macOS")
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

			if _, err := inspector.press(context.Background(), "/project", "Later"); err == nil {
				t.Fatal("expected an error")
			}
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
	})
}
