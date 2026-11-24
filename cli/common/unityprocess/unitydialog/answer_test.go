package unitydialog

import (
	"context"
	"errors"
	"reflect"
	"strings"
	"testing"
	"time"
)

// safeModePromptAnswerForTest is the answer uloop launch gives Unity's Safe Mode prompt.
func safeModePromptAnswerForTest() DialogAnswer {
	return DialogAnswer{
		Title:   "Enter Safe Mode?",
		Buttons: []string{"Enter Safe Mode", "Ignore", "Quit"},
		Label:   "Ignore",
	}
}

// The Safe Mode prompt as a real Editor on macOS listed it: an alert whose first text is its title.
const accessibilitySafeModePromptWindow = `{"windowTitle":"","texts":["Enter Safe Mode?",` +
	`"The project you are opening contains compilation errors."],"buttons":["Enter Safe Mode","Ignore","Quit"]}`

// The progress window a real Editor on macOS listed before the Safe Mode prompt while it compiled:
// no window title and one button without a name.
const accessibilityCompileProgressWindow = `{"windowTitle":"","texts":["Compiling Scripts","Probe project"],"buttons":[null]}`

// The Safe Mode prompt as the Windows listing reads it.
var win32SafeModePromptDialog = Dialog{
	Title:   "Enter Safe Mode?",
	Message: "The project has compilation errors.\n\nEnter Safe Mode?",
	Buttons: []string{"Enter Safe Mode", "Ignore", "Quit"},
}

var accessibilitySafeModePromptDialog = Dialog{
	Title:   "Enter Safe Mode?",
	Message: "The project you are opening contains compilation errors.",
	Buttons: []string{"Enter Safe Mode", "Ignore", "Quit"},
}

func assertAnswerResult(t *testing.T, got AnswerResult, err error, want AnswerResult) {
	t.Helper()
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("result = %#v, want %#v", got, want)
	}
}

// Verifies an answer names only the dialog with its title and exactly its buttons, in order.
func TestDialogAnswerMatchesTheTitleAndExactlyTheButtons(t *testing.T) {
	answer := safeModePromptAnswerForTest()
	cases := map[string]struct {
		dialog Dialog
		want   bool
	}{
		"the prompt":                   {dialog: accessibilitySafeModePromptDialog, want: true},
		"another title":                {dialog: Dialog{Title: "Probe retry", Buttons: answer.Buttons}, want: false},
		"the title with other buttons": {dialog: Dialog{Title: answer.Title, Buttons: []string{"OK"}}, want: false},
		"the buttons in another order": {
			dialog: Dialog{Title: answer.Title, Buttons: []string{"Ignore", "Enter Safe Mode", "Quit"}},
			want:   false,
		},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			if got := answer.Matches(testCase.dialog); got != testCase.want {
				t.Fatalf("Matches = %v, want %v", got, testCase.want)
			}
		})
	}
}

// Verifies the answer script is run once, with the Unity process ID and the answer as JSON after it.
func TestInspectAndAnswerRunsTheAnswerScriptWithThePidAndTheAnswer(t *testing.T) {
	calls := [][]string{}
	command := func(_ context.Context, name string, args ...string) ([]byte, error) {
		calls = append(calls, append([]string{name}, args...))
		return []byte(`{"processFound":true,"dialogs":[],"matched":false,"pressed":false}`), nil
	}
	inspector := dialogInspectorForTest(t, "darwin", unityProcessWithPID(4242), command)

	if _, err := inspector.InspectAndAnswer(context.Background(), "/project", safeModePromptAnswerForTest()); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	want := [][]string{{
		"osascript", "-l", "JavaScript", "-e", accessibilityAnswerScript, "4242",
		`{"title":"Enter Safe Mode?","buttons":["Enter Safe Mode","Ignore","Quit"],"label":"Ignore"}`,
	}}
	if !reflect.DeepEqual(calls, want) {
		t.Fatalf("commands = %q, want %q", calls, want)
	}
}

// Verifies the answer script's output is read as Inspect reads its own, untitled windows without a
// named button left out, together with whether the frontmost dialog matched and was pressed.
func TestAnswerResultFromAccessibilityOutputReadsTheDialogsAndTheVerdict(t *testing.T) {
	behind := `{"windowTitle":"Probe behind","texts":["Probe behind"],"buttons":["OK"]}`
	behindDialog := Dialog{Title: "Probe behind", Message: "", Buttons: []string{"OK"}}
	cases := map[string]struct {
		output string
		want   AnswerResult
	}{
		"prompt pressed behind the progress window": {
			output: `{"processFound":true,"dialogs":[` + accessibilityCompileProgressWindow + `,` +
				accessibilitySafeModePromptWindow + `,` + behind + `],"matched":true,"pressed":true}`,
			want: AnswerResult{
				Dialogs: []Dialog{accessibilitySafeModePromptDialog, behindDialog},
				Matched: true,
				Pressed: true,
			},
		},
		"prompt matched but not pressed": {
			output: `{"processFound":true,"dialogs":[` + accessibilitySafeModePromptWindow + `],"matched":true,"pressed":false}`,
			want:   AnswerResult{Dialogs: []Dialog{accessibilitySafeModePromptDialog}, Matched: true},
		},
		"another dialog in front": {
			output: `{"processFound":true,"dialogs":[` + behind + `,` +
				accessibilitySafeModePromptWindow + `],"matched":false,"pressed":false}`,
			want: AnswerResult{Dialogs: []Dialog{behindDialog, accessibilitySafeModePromptDialog}},
		},
		"no dialog": {
			output: `{"processFound":true,"dialogs":[],"matched":false,"pressed":false}`,
			want:   AnswerResult{Dialogs: []Dialog{}},
		},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			got, err := answerResultFromAccessibilityOutput([]byte(testCase.output), safeModePromptAnswerForTest())

			assertAnswerResult(t, got, err, testCase.want)
		})
	}
}

// Verifies output whose verdict contradicts the dialogs it lists is refused rather than returned:
// a match the frontmost dialog does not bear out, a frontmost match the script missed, and a press
// without a match.
func TestAnswerResultFromAccessibilityOutputRefusesAContradictoryVerdict(t *testing.T) {
	other := `{"windowTitle":"Probe behind","texts":[],"buttons":["OK"]}`
	cases := map[string]string{
		"match of another dialog": `{"processFound":true,"dialogs":[` + other + `],"matched":true,"pressed":true}`,
		"missed match":            `{"processFound":true,"dialogs":[` + accessibilitySafeModePromptWindow + `],"matched":false,"pressed":false}`,
		"press without a match":   `{"processFound":true,"dialogs":[` + other + `],"matched":false,"pressed":true}`,
	}
	for name, output := range cases {
		t.Run(name, func(t *testing.T) {
			if _, err := answerResultFromAccessibilityOutput([]byte(output), safeModePromptAnswerForTest()); err == nil {
				t.Fatal("expected an error for a contradictory verdict")
			}
		})
	}
}

// Verifies output from a script that did not find the Unity process, or that cannot be read, is an
// error.
func TestAnswerResultFromAccessibilityOutputRejectsAMissingProcessAndUnusableOutput(t *testing.T) {
	for name, output := range map[string]string{
		"process missing": `{"processFound":false,"dialogs":[]}`,
		"unreadable":      `not json`,
	} {
		t.Run(name, func(t *testing.T) {
			if _, err := answerResultFromAccessibilityOutput([]byte(output), safeModePromptAnswerForTest()); err == nil {
				t.Fatal("expected an error")
			}
		})
	}
}

// Verifies an answer without a title, or whose label is not one of its buttons, is a caller error
// that fails before the process lookup and before any command or window listing.
func TestInspectAndAnswerRefusesAnInvalidAnswerBeforeLooking(t *testing.T) {
	noTitle := safeModePromptAnswerForTest()
	noTitle.Title = ""
	strayLabel := safeModePromptAnswerForTest()
	strayLabel.Label = "Retry"
	for name, answer := range map[string]DialogAnswer{"no title": noTitle, "label not a button": strayLabel} {
		for _, goos := range []string{"darwin", "windows"} {
			t.Run(name+" on "+goos, func(t *testing.T) {
				inspector := dialogInspectorForTest(t, goos, failingProcessLookup(t), failingCommand(t))

				if _, err := inspector.InspectAndAnswer(context.Background(), "/project", answer); err == nil {
					t.Fatal("expected an error for an invalid answer")
				}
			})
		}
	}
}

// Verifies answering is unsupported where dialogs cannot be read, without a process lookup.
func TestInspectAndAnswerIsUnsupportedOutsideMacOSAndWindows(t *testing.T) {
	inspector := dialogInspectorForTest(t, "linux", failingProcessLookup(t), failingCommand(t))

	_, err := inspector.InspectAndAnswer(context.Background(), "/project", safeModePromptAnswerForTest())

	if !errors.Is(err, ErrInspectionUnsupported) {
		t.Fatalf("error = %v, want ErrInspectionUnsupported", err)
	}
}

// Verifies an answer script that runs past the inspector's timeout ends with an error that says so.
func TestInspectAndAnswerReportsATimedOutScript(t *testing.T) {
	command := func(ctx context.Context, _ string, _ ...string) ([]byte, error) {
		<-ctx.Done()
		return nil, errors.New("signal: killed")
	}
	inspector := dialogInspectorForTest(t, "darwin", unityProcessWithPID(4242), command)
	inspector.timeout = 50 * time.Millisecond

	_, err := inspector.InspectAndAnswer(context.Background(), "/project", safeModePromptAnswerForTest())

	if err == nil || !strings.Contains(err.Error(), "did not answer within 50ms") {
		t.Fatalf("error = %v, want a timeout error", err)
	}
}

// recordingWin32Press returns a press that records every target it is handed and fails with err.
func recordingWin32Press(pressed *[]win32PressTargetResult, err error) func(context.Context, win32PressTargetResult) error {
	return func(_ context.Context, target win32PressTargetResult) error {
		*pressed = append(*pressed, target)
		return err
	}
}

// Verifies that on Windows the answer presses Ignore in the Safe Mode prompt once, from the same
// listing it returns the dialogs from.
func TestInspectAndAnswerOnWindowsPressesIgnoreInTheSafeModePrompt(t *testing.T) {
	listings := 0
	list := func(context.Context, int) ([]win32Window, error) {
		listings++
		return []win32Window{win32StartupMessageBox(), win32InputMethodWindow}, nil
	}
	var pressed []win32PressTargetResult
	inspector := win32PressInspectorForTest(t, list, recordingWin32Press(&pressed, nil))

	got, err := inspector.InspectAndAnswer(context.Background(), "/project", safeModePromptAnswerForTest())

	assertAnswerResult(t, got, err, AnswerResult{Dialogs: []Dialog{win32SafeModePromptDialog}, Matched: true, Pressed: true})
	// "&Ignore" has control ID 3 in the prompt.
	if listings != 1 || len(pressed) != 1 || pressed[0].ButtonID != 3 {
		t.Fatalf("listings = %d, pressed = %#v, want one listing and one press of the prompt's Ignore button", listings, pressed)
	}
}

// Verifies that on Windows a dialog the answer does not name is returned without a press: another
// title with an Ignore button, and the prompt's title with other buttons.
func TestInspectAndAnswerOnWindowsLeavesOtherDialogsAlone(t *testing.T) {
	otherTitle := win32StartupMessageBox()
	otherTitle.Title = "Probe retry"
	otherButtons := win32StartupMessageBox()
	otherButtons.Controls = otherButtons.Controls[:4]
	for name, window := range map[string]win32Window{"another title": otherTitle, "other buttons": otherButtons} {
		t.Run(name, func(t *testing.T) {
			windows := []win32Window{window, win32InputMethodWindow}
			inspector := win32PressInspectorForTest(t, listingWin32Windows(windows), failingWindowButtonPress(t))

			got, err := inspector.InspectAndAnswer(context.Background(), "/project", safeModePromptAnswerForTest())

			assertAnswerResult(t, got, err, AnswerResult{Dialogs: dialogsFromWin32Windows(windows)})
		})
	}
}

// Verifies that on Windows a matching prompt whose Ignore is disabled, or whose press fails, is
// reported as matched and not pressed, with the dialogs read before.
func TestInspectAndAnswerOnWindowsReportsAMatchItCouldNotPress(t *testing.T) {
	t.Run("disabled button", func(t *testing.T) {
		prompt := win32StartupMessageBox()
		prompt.Controls[3].Style |= win32StyleDisabled
		windows := []win32Window{prompt, win32InputMethodWindow}
		inspector := win32PressInspectorForTest(t, listingWin32Windows(windows), failingWindowButtonPress(t))

		got, err := inspector.InspectAndAnswer(context.Background(), "/project", safeModePromptAnswerForTest())

		assertAnswerResult(t, got, err, AnswerResult{Dialogs: dialogsFromWin32Windows(windows), Matched: true})
	})
	t.Run("press fails", func(t *testing.T) {
		windows := []win32Window{win32StartupMessageBox(), win32InputMethodWindow}
		var pressed []win32PressTargetResult
		inspector := win32PressInspectorForTest(
			t, listingWin32Windows(windows), recordingWin32Press(&pressed, errors.New("SendMessage failed")))

		got, err := inspector.InspectAndAnswer(context.Background(), "/project", safeModePromptAnswerForTest())

		assertAnswerResult(t, got, err, AnswerResult{Dialogs: []Dialog{win32SafeModePromptDialog}, Matched: true})
		if len(pressed) != 1 {
			t.Fatalf("pressed = %#v, want one press attempt", pressed)
		}
	})
}

// Verifies that on Windows a listing that fails is an error, and nothing is pressed.
func TestInspectAndAnswerOnWindowsFailsWhenTheListingFails(t *testing.T) {
	list := func(context.Context, int) ([]win32Window, error) { return nil, errors.New("EnumWindows failed") }
	inspector := win32PressInspectorForTest(t, list, failingWindowButtonPress(t))

	_, err := inspector.InspectAndAnswer(context.Background(), "/project", safeModePromptAnswerForTest())

	if err == nil || !strings.Contains(err.Error(), "EnumWindows failed") {
		t.Fatalf("error = %v, want the listing failure", err)
	}
}
