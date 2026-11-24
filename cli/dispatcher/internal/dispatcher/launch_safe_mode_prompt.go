package dispatcher

import (
	"context"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityprocess/unitydialog"
)

// safeModePromptAnswer presses Ignore in the prompt Unity shows at startup when the project has
// compile errors.
// Why the buttons as well as the title: another dialog could share the title, and only the prompt
// offers exactly these three answers, so Ignore there means "open the project anyway".
func safeModePromptAnswer() unitydialog.DialogAnswer {
	return unitydialog.DialogAnswer{
		Title:   "Enter Safe Mode?",
		Buttons: []string{"Enter Safe Mode", "Ignore", "Quit"},
		Label:   "Ignore",
	}
}

// safeModePromptAnswerer is the dialog look of one launch wait that answers Unity's Safe Mode
// prompt with Ignore, once, and reports every other dialog as the plain look does.
// The readiness wait calls findDialogs from one goroutine, so its fields need no lock.
type safeModePromptAnswerer struct {
	find clicore.DialogFinder
	// answer reads the dialogs and presses Ignore in the prompt within the same look.
	// Why one look and not a look and then a press: each starts osascript on macOS, which takes
	// about a second, and the prompt stays up until the press.
	answer func(context.Context) (unitydialog.AnswerResult, error)
	// attempted is whether a look has matched the prompt already, whatever the press did.
	attempted bool
	// ignored is whether the press of Ignore reached the prompt.
	ignored bool
}

func newSafeModePromptAnswerer(projectRoot string, deps launchDeps) *safeModePromptAnswerer {
	return &safeModePromptAnswerer{
		find: v3LaunchDialogFinder(projectRoot, deps),
		answer: func(ctx context.Context) (unitydialog.AnswerResult, error) {
			return deps.answerDialog(ctx, projectRoot, deps.findRunningUnityProcessDefinitively, safeModePromptAnswer())
		},
	}
}

// findDialogs is a clicore.DialogFinder.
func (answerer *safeModePromptAnswerer) findDialogs(ctx context.Context) ([]unitydialog.Dialog, error) {
	// Why once: a prompt that is still there after a look matched it is one launch cannot answer, and
	// a person has to see it.
	if answerer.attempted {
		return answerer.find(ctx)
	}
	result, err := answerer.answer(ctx)
	// Why a failed look does not count as the one press: it read nothing, so it cannot tell whether
	// the prompt was up, and counting it would let one slow look while Unity starts turn the press
	// off. The next look reads the dialogs again before it presses. A look stopped after its click
	// leaves ignored false: the next look no longer finds the prompt, and the wait goes on without
	// reporting the press.
	if err != nil {
		return nil, err
	}
	if !result.Matched {
		return result.Dialogs, nil
	}
	answerer.attempted = true
	// Why the dialogs read before the press when nothing was pressed: stopping at the prompt and
	// showing it is the safe side when uloop could not answer it.
	if !result.Pressed {
		return result.Dialogs, nil
	}
	answerer.ignored = true
	// Why the rest of the reading instead of a new one: right after the press the closing prompt can
	// still be listed, and the wait would stop at it. An empty rest lets the wait go on; any dialog
	// behind the prompt stops it as before.
	return dialogsOtherThanSafeModePrompt(result.Dialogs), nil
}

// dialogsOtherThanSafeModePrompt returns dialogs without any listing of the Safe Mode prompt.
// Why every listing and not only the first: a real Editor on macOS listed the same prompt twice,
// and dropping only the first would stop the wait at a duplicate of the prompt just pressed.
func dialogsOtherThanSafeModePrompt(dialogs []unitydialog.Dialog) []unitydialog.Dialog {
	answer := safeModePromptAnswer()
	others := make([]unitydialog.Dialog, 0, len(dialogs))
	for _, dialog := range dialogs {
		if !answer.Matches(dialog) {
			others = append(others, dialog)
		}
	}
	return others
}
