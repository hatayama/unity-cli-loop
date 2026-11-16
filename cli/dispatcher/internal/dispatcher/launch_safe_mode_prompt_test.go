package dispatcher

import (
	"context"
	"errors"
	"reflect"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityprocess/unitydialog"
)

func safeModePromptDialog() unitydialog.Dialog {
	return unitydialog.Dialog{
		Title:   "Enter Safe Mode?",
		Message: "The project you are opening contains compilation errors.",
		Buttons: []string{"Enter Safe Mode", "Ignore", "Quit"},
	}
}

// answererForTest is a safeModePromptAnswerer whose look returns dialogs and err, and whose press
// returns result and pressErr and counts its calls in presses.
func answererForTest(
	dialogs []unitydialog.Dialog, err error, result unitydialog.PressResult, pressErr error, presses *int,
) *safeModePromptAnswerer {
	return &safeModePromptAnswerer{
		find: func(context.Context) ([]unitydialog.Dialog, error) { return dialogs, err },
		pressIgnore: func(context.Context) (unitydialog.PressResult, error) {
			*presses++
			return result, pressErr
		},
	}
}

// Verifies a look that failed is returned as is, without a press.
func TestSafeModePromptAnswererPassesThroughAFailedLook(t *testing.T) {
	lookErr := errors.New("osascript did not answer")
	presses := 0
	answerer := answererForTest(nil, lookErr, unitydialog.PressResult{}, nil, &presses)

	got, err := answerer.findDialogs(context.Background())

	if !errors.Is(err, lookErr) || len(got) != 0 || presses != 0 || answerer.ignored {
		t.Fatalf("got %#v, %v, presses %d, ignored %v; want the look error and no press", got, err, presses, answerer.ignored)
	}
}

// Verifies a look that found no dialog is returned as is, without a press.
func TestSafeModePromptAnswererPassesThroughNoDialog(t *testing.T) {
	presses := 0
	answerer := answererForTest(nil, nil, unitydialog.PressResult{}, nil, &presses)

	got, err := answerer.findDialogs(context.Background())

	if err != nil || len(got) != 0 || presses != 0 || answerer.ignored {
		t.Fatalf("got %#v, %v, presses %d, ignored %v; want no dialog and no press", got, err, presses, answerer.ignored)
	}
}

// Verifies the Safe Mode prompt alone is pressed once and reported as gone, so the wait goes on.
func TestSafeModePromptAnswererPressesIgnoreOnceAndKeepsWaiting(t *testing.T) {
	presses := 0
	answerer := answererForTest(
		[]unitydialog.Dialog{safeModePromptDialog()}, nil, unitydialog.PressResult{DialogFound: true, Pressed: true}, nil, &presses)

	got, err := answerer.findDialogs(context.Background())

	if err != nil || len(got) != 0 || presses != 1 || !answerer.ignored {
		t.Fatalf("got %#v, %v, presses %d, ignored %v; want no dialog after one press", got, err, presses, answerer.ignored)
	}
}

// Verifies that after the prompt is pressed, the dialogs read behind it are reported, so the wait
// stops at them as before.
func TestSafeModePromptAnswererReportsTheDialogsBehindThePromptAfterPressing(t *testing.T) {
	behind := launchTestDialog()
	presses := 0
	answerer := answererForTest(
		[]unitydialog.Dialog{safeModePromptDialog(), behind}, nil,
		unitydialog.PressResult{DialogFound: true, Pressed: true}, nil, &presses)

	got, err := answerer.findDialogs(context.Background())

	if err != nil || !reflect.DeepEqual(got, []unitydialog.Dialog{behind}) || presses != 1 || !answerer.ignored {
		t.Fatalf("got %#v, %v, presses %d, ignored %v; want only the dialog behind the prompt", got, err, presses, answerer.ignored)
	}
}

// Verifies that after the prompt is pressed, a second listing of the same prompt, as a real
// Editor on macOS showed it, is dropped with the first, so the wait goes on after one press.
func TestSafeModePromptAnswererDropsRepeatedListingsOfThePromptAfterPressing(t *testing.T) {
	presses := 0
	answerer := answererForTest(
		[]unitydialog.Dialog{safeModePromptDialog(), safeModePromptDialog()}, nil,
		unitydialog.PressResult{DialogFound: true, Pressed: true}, nil, &presses)

	got, err := answerer.findDialogs(context.Background())

	if err != nil || len(got) != 0 || presses != 1 || !answerer.ignored {
		t.Fatalf("got %#v, %v, presses %d, ignored %v; want no dialog after one press", got, err, presses, answerer.ignored)
	}
}

// Verifies a press that pressed nothing leaves the prompt reported, so the wait stops at it.
func TestSafeModePromptAnswererReportsThePromptWhenThePressDidNotPress(t *testing.T) {
	dialogs := []unitydialog.Dialog{safeModePromptDialog()}
	presses := 0
	answerer := answererForTest(dialogs, nil, unitydialog.PressResult{DialogFound: true, Pressed: false}, nil, &presses)

	got, err := answerer.findDialogs(context.Background())

	if err != nil || !reflect.DeepEqual(got, dialogs) || presses != 1 || answerer.ignored {
		t.Fatalf("got %#v, %v, presses %d, ignored %v; want the prompt after one press", got, err, presses, answerer.ignored)
	}
}

// Verifies a press that failed, before it started or after, leaves the prompt reported without an
// error, so the wait stops at the prompt.
func TestSafeModePromptAnswererReportsThePromptWhenThePressFailed(t *testing.T) {
	failures := map[string]error{
		"not started": unitydialog.PressNotStartedError{Err: errors.New("no process")},
		"other":       errors.New("osascript could not press the button"),
	}
	for name, pressErr := range failures {
		t.Run(name, func(t *testing.T) {
			dialogs := []unitydialog.Dialog{safeModePromptDialog()}
			presses := 0
			answerer := answererForTest(dialogs, nil, unitydialog.PressResult{}, pressErr, &presses)

			got, err := answerer.findDialogs(context.Background())

			if err != nil || !reflect.DeepEqual(got, dialogs) || presses != 1 || answerer.ignored {
				t.Fatalf("got %#v, %v, presses %d, ignored %v; want the prompt after one press", got, err, presses, answerer.ignored)
			}
		})
	}
}

// Verifies the prompt seen again after a press is reported and not pressed a second time.
func TestSafeModePromptAnswererDoesNotPressASecondTime(t *testing.T) {
	dialogs := []unitydialog.Dialog{safeModePromptDialog()}
	presses := 0
	answerer := answererForTest(dialogs, nil, unitydialog.PressResult{DialogFound: true, Pressed: true}, nil, &presses)
	if _, err := answerer.findDialogs(context.Background()); err != nil {
		t.Fatalf("first look: %v", err)
	}

	got, err := answerer.findDialogs(context.Background())

	if err != nil || !reflect.DeepEqual(got, dialogs) || presses != 1 {
		t.Fatalf("got %#v, %v, presses %d; want the prompt and still one press", got, err, presses)
	}
}

// Verifies dialogs that are not the Safe Mode prompt in front are reported as they are and never
// pressed: the prompt's title with other buttons, another title with an Ignore button, and the
// prompt behind another dialog.
func TestSafeModePromptAnswererLeavesOtherDialogsAlone(t *testing.T) {
	sameTitle := safeModePromptDialog()
	sameTitle.Buttons = []string{"OK"}
	otherTitle := unitydialog.Dialog{Title: "Probe retry", Buttons: []string{"Retry", "Ignore"}}
	cases := map[string][]unitydialog.Dialog{
		"prompt title with other buttons":   {sameTitle},
		"other title with an Ignore button": {otherTitle},
		"prompt behind another dialog":      {launchTestDialog(), safeModePromptDialog()},
	}
	for name, dialogs := range cases {
		t.Run(name, func(t *testing.T) {
			presses := 0
			answerer := answererForTest(dialogs, nil, unitydialog.PressResult{DialogFound: true, Pressed: true}, nil, &presses)

			got, err := answerer.findDialogs(context.Background())

			if err != nil || !reflect.DeepEqual(got, dialogs) || presses != 0 || answerer.ignored {
				t.Fatalf("got %#v, %v, presses %d, ignored %v; want the dialogs and no press", got, err, presses, answerer.ignored)
			}
		})
	}
}
