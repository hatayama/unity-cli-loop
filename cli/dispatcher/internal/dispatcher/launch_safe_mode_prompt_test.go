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

// answererCalls counts the looks of an answerer: answers that may press, and plain looks.
type answererCalls struct {
	answers int
	looks   int
}

// answererForTest is a safeModePromptAnswerer whose answering look returns result and err, and whose
// plain look returns plain. Both count their calls in calls.
func answererForTest(
	result unitydialog.AnswerResult, err error, plain []unitydialog.Dialog, calls *answererCalls,
) *safeModePromptAnswerer {
	return &safeModePromptAnswerer{
		find: func(context.Context) ([]unitydialog.Dialog, error) {
			calls.looks++
			return plain, nil
		},
		answer: func(context.Context) (unitydialog.AnswerResult, error) {
			calls.answers++
			return result, err
		},
	}
}

// pressedPrompt is an answer that pressed Ignore in the prompt, frontmost in dialogs.
func pressedPrompt(dialogs ...unitydialog.Dialog) unitydialog.AnswerResult {
	return unitydialog.AnswerResult{Dialogs: dialogs, Matched: true, Pressed: true}
}

// Verifies the answer given is the Safe Mode prompt's: Ignore, only in a dialog titled
// "Enter Safe Mode?" with exactly its three buttons in order.
func TestSafeModePromptAnswerNamesThePromptAndIgnore(t *testing.T) {
	answer := safeModePromptAnswer()

	if !answer.Matches(safeModePromptDialog()) || answer.Label != "Ignore" {
		t.Fatalf("answer = %#v, want Ignore in the Safe Mode prompt", answer)
	}
	if !reflect.DeepEqual(answer.Buttons, []string{"Enter Safe Mode", "Ignore", "Quit"}) {
		t.Fatalf("Buttons = %q, want exactly the prompt's three buttons", answer.Buttons)
	}
}

// Verifies a look that failed is returned as is and does not count as the one press: the next look
// answers again, because the failed one read nothing and cannot tell whether the prompt was up.
func TestSafeModePromptAnswererAnswersAgainAfterAFailedLook(t *testing.T) {
	lookErr := errors.New("osascript did not answer")
	calls := answererCalls{}
	answerer := answererForTest(unitydialog.AnswerResult{}, lookErr, nil, &calls)

	got, err := answerer.findDialogs(context.Background())
	_, _ = answerer.findDialogs(context.Background())

	if !errors.Is(err, lookErr) || len(got) != 0 || answerer.ignored {
		t.Fatalf("got %#v, %v, ignored %v; want the look error", got, err, answerer.ignored)
	}
	if calls.answers != 2 || calls.looks != 0 {
		t.Fatalf("calls = %+v, want two answering looks and no plain look", calls)
	}
}

// Verifies a look that found no dialog is returned as is, and the next look answers again.
func TestSafeModePromptAnswererPassesThroughNoDialog(t *testing.T) {
	calls := answererCalls{}
	answerer := answererForTest(unitydialog.AnswerResult{Dialogs: []unitydialog.Dialog{}}, nil, nil, &calls)

	got, err := answerer.findDialogs(context.Background())
	_, _ = answerer.findDialogs(context.Background())

	if err != nil || len(got) != 0 || answerer.ignored || calls.answers != 2 {
		t.Fatalf("got %#v, %v, ignored %v, calls %+v; want no dialog and two answering looks",
			got, err, answerer.ignored, calls)
	}
}

// Verifies the Safe Mode prompt pressed in the look is reported as gone, so the wait goes on.
func TestSafeModePromptAnswererPressesIgnoreOnceAndKeepsWaiting(t *testing.T) {
	calls := answererCalls{}
	answerer := answererForTest(pressedPrompt(safeModePromptDialog()), nil, nil, &calls)

	got, err := answerer.findDialogs(context.Background())

	if err != nil || len(got) != 0 || calls.answers != 1 || !answerer.ignored {
		t.Fatalf("got %#v, %v, calls %+v, ignored %v; want no dialog after one press", got, err, calls, answerer.ignored)
	}
}

// Verifies that after the prompt is pressed, the dialogs read behind it are reported, so the wait
// stops at them as before.
func TestSafeModePromptAnswererReportsTheDialogsBehindThePromptAfterPressing(t *testing.T) {
	behind := launchTestDialog()
	calls := answererCalls{}
	answerer := answererForTest(pressedPrompt(safeModePromptDialog(), behind), nil, nil, &calls)

	got, err := answerer.findDialogs(context.Background())

	if err != nil || !reflect.DeepEqual(got, []unitydialog.Dialog{behind}) || !answerer.ignored {
		t.Fatalf("got %#v, %v, ignored %v; want only the dialog behind the prompt", got, err, answerer.ignored)
	}
}

// Verifies that after the prompt is pressed, a second listing of the same prompt, as a real
// Editor on macOS showed it, is dropped with the first, so the wait goes on after one press.
func TestSafeModePromptAnswererDropsRepeatedListingsOfThePromptAfterPressing(t *testing.T) {
	calls := answererCalls{}
	answerer := answererForTest(pressedPrompt(safeModePromptDialog(), safeModePromptDialog()), nil, nil, &calls)

	got, err := answerer.findDialogs(context.Background())

	if err != nil || len(got) != 0 || !answerer.ignored {
		t.Fatalf("got %#v, %v, ignored %v; want no dialog after one press", got, err, answerer.ignored)
	}
}

// Verifies a prompt the look matched but did not press is reported, so the wait stops at it.
func TestSafeModePromptAnswererReportsThePromptWhenTheLookDidNotPress(t *testing.T) {
	dialogs := []unitydialog.Dialog{safeModePromptDialog()}
	calls := answererCalls{}
	answerer := answererForTest(unitydialog.AnswerResult{Dialogs: dialogs, Matched: true}, nil, nil, &calls)

	got, err := answerer.findDialogs(context.Background())

	if err != nil || !reflect.DeepEqual(got, dialogs) || answerer.ignored {
		t.Fatalf("got %#v, %v, ignored %v; want the prompt", got, err, answerer.ignored)
	}
}

// Verifies that once a look has matched the prompt, pressed or not, later looks only read the
// dialogs and report a prompt still there without pressing it again.
func TestSafeModePromptAnswererDoesNotAnswerASecondTime(t *testing.T) {
	prompt := []unitydialog.Dialog{safeModePromptDialog()}
	cases := map[string]unitydialog.AnswerResult{
		"pressed":     pressedPrompt(safeModePromptDialog()),
		"not pressed": {Dialogs: prompt, Matched: true},
	}
	for name, first := range cases {
		t.Run(name, func(t *testing.T) {
			calls := answererCalls{}
			answerer := answererForTest(first, nil, prompt, &calls)
			if _, err := answerer.findDialogs(context.Background()); err != nil {
				t.Fatalf("first look: %v", err)
			}

			got, err := answerer.findDialogs(context.Background())

			if err != nil || !reflect.DeepEqual(got, prompt) || calls.answers != 1 || calls.looks != 1 {
				t.Fatalf("got %#v, %v, calls %+v; want the prompt from one plain look after one answer", got, err, calls)
			}
		})
	}
}

// Verifies dialogs the look did not match are reported as they are, and the next look answers
// again, so a prompt that comes to the front later is still pressed.
func TestSafeModePromptAnswererLeavesUnmatchedDialogsAlone(t *testing.T) {
	dialogs := []unitydialog.Dialog{launchTestDialog(), safeModePromptDialog()}
	calls := answererCalls{}
	answerer := answererForTest(unitydialog.AnswerResult{Dialogs: dialogs}, nil, nil, &calls)

	got, err := answerer.findDialogs(context.Background())
	_, _ = answerer.findDialogs(context.Background())

	if err != nil || !reflect.DeepEqual(got, dialogs) || answerer.ignored || calls.answers != 2 {
		t.Fatalf("got %#v, %v, ignored %v, calls %+v; want the dialogs and two answering looks",
			got, err, answerer.ignored, calls)
	}
}
