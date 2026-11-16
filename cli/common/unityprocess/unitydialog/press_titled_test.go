package unitydialog

import (
	"context"
	"reflect"
	"testing"
)

// The Safe Mode prompt as the Windows listing reads it.
var win32SafeModePromptDialog = Dialog{
	Title:   "Enter Safe Mode?",
	Message: "The project has compilation errors.\n\nEnter Safe Mode?",
	Buttons: []string{"Enter Safe Mode", "Ignore", "Quit"},
}

// Verifies a press limited to a dialog title runs the press script with the title after the
// Unity process ID and the label, so the script can compare it before it clicks.
func TestPressInDialogTitledPassesTheTitleToTheScript(t *testing.T) {
	var gotArgs []string
	command := func(_ context.Context, name string, args ...string) ([]byte, error) {
		gotArgs = append([]string{name}, args...)
		return []byte(accessibilityPressedOutput), nil
	}
	inspector := dialogInspectorForTest(t, "darwin", unityProcessWithPID(4242), command)

	_, err := inspector.PressInDialogTitled(context.Background(), "/project", "Enter Safe Mode?", "Ignore")
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	want := []string{"osascript", "-l", "JavaScript", "-e", accessibilityPressScript, "4242", "Ignore", "Enter Safe Mode?"}
	if !reflect.DeepEqual(gotArgs, want) {
		t.Fatalf("command = %q, want %q", gotArgs, want)
	}
}

// Verifies a press limited to an empty title is a caller error that fails before anything runs:
// no process lookup, no osascript, and the failure says the press never started.
func TestPressInDialogTitledRefusesAnEmptyTitleWithoutStartingOsascript(t *testing.T) {
	inspector := dialogInspectorForTest(t, "darwin", failingProcessLookup(t), failingCommand(t))

	_, err := inspector.PressInDialogTitled(context.Background(), "/project", "", "Ignore")

	assertPressNotStarted(t, err, true)
}

// Verifies that on Windows a press limited to the Safe Mode prompt's title presses Ignore in that
// prompt exactly once.
func TestPressInDialogTitledOnWindowsPressesIgnoreInTheSafeModePrompt(t *testing.T) {
	windows := []win32Window{win32StartupMessageBox(), win32InputMethodWindow}
	var pressed []win32PressTargetResult
	press := func(_ context.Context, target win32PressTargetResult) error {
		pressed = append(pressed, target)
		return nil
	}
	inspector := win32PressInspectorForTest(t, listingWin32Windows(windows), press)

	got, err := inspector.PressInDialogTitled(context.Background(), "/project", "Enter Safe Mode?", "Ignore")

	assertPressResult(t, got, err, PressResult{DialogFound: true, Pressed: true, Dialog: win32SafeModePromptDialog})
	// "&Ignore" has control ID 3 in the prompt.
	if len(pressed) != 1 || pressed[0].ButtonID != 3 || pressed[0].Dialog.Title != "Enter Safe Mode?" || pressed[0].ButtonDisabled {
		t.Fatalf("pressed = %#v, want one press of the prompt's Ignore button", pressed)
	}
}

// Verifies that on Windows a press limited to the Safe Mode prompt's title presses nothing in a
// message box with another title, even though it has an Ignore button, and returns that dialog.
func TestPressInDialogTitledOnWindowsDoesNotPressADialogWithAnotherTitle(t *testing.T) {
	other := win32StartupMessageBox()
	other.Title = "Probe retry"
	windows := []win32Window{other, win32InputMethodWindow}
	inspector := win32PressInspectorForTest(t, listingWin32Windows(windows), failingWindowButtonPress(t))

	got, err := inspector.PressInDialogTitled(context.Background(), "/project", "Enter Safe Mode?", "Ignore")

	want := win32SafeModePromptDialog
	want.Title = "Probe retry"
	assertPressResult(t, got, err, PressResult{DialogFound: true, Pressed: false, Dialog: want})
}
