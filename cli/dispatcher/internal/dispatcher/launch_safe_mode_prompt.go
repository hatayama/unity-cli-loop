package dispatcher

import (
	"context"
	"slices"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityprocess/unitydialog"
)

const (
	safeModePromptTitle       = "Enter Safe Mode?"
	safeModePromptIgnoreLabel = "Ignore"
)

// isSafeModePrompt reports whether dialog is the prompt Unity shows at startup when the project
// has compile errors.
// Why the buttons as well as the title: another dialog could share the title, and only the prompt
// offers exactly these three answers, so Ignore there means "open the project anyway".
func isSafeModePrompt(dialog unitydialog.Dialog) bool {
	return dialog.Title == safeModePromptTitle &&
		slices.Equal(dialog.Buttons, []string{"Enter Safe Mode", "Ignore", "Quit"})
}

// safeModePromptAnswerer is the dialog look of one launch wait that answers Unity's Safe Mode
// prompt with Ignore, once, and reports every other dialog as the plain look does.
// The readiness wait calls findDialogs from one goroutine, so its fields need no lock.
type safeModePromptAnswerer struct {
	find        clicore.DialogFinder
	pressIgnore func(context.Context) (unitydialog.PressResult, error)
	// attempted is whether the prompt has been pressed already, whatever the press returned.
	attempted bool
	// ignored is whether the press of Ignore reached the prompt.
	ignored bool
}

func newSafeModePromptAnswerer(projectRoot string, deps launchDeps) *safeModePromptAnswerer {
	return &safeModePromptAnswerer{
		find: v3LaunchDialogFinder(projectRoot, deps),
		pressIgnore: func(ctx context.Context) (unitydialog.PressResult, error) {
			return deps.pressDialogButtonInDialogTitled(
				ctx, projectRoot, deps.findRunningUnityProcessDefinitively, safeModePromptTitle, safeModePromptIgnoreLabel)
		},
	}
}

// findDialogs is a clicore.DialogFinder.
func (answerer *safeModePromptAnswerer) findDialogs(ctx context.Context) ([]unitydialog.Dialog, error) {
	dialogs, err := answerer.find(ctx)
	if err != nil || len(dialogs) == 0 {
		return dialogs, err
	}
	// Why only the first dialog: the press reaches the frontmost dialog only.
	// Why once: a prompt that is still there after a press is one launch cannot answer, and a person
	// has to see it.
	if answerer.attempted || !isSafeModePrompt(dialogs[0]) {
		return dialogs, nil
	}
	answerer.attempted = true
	result, pressErr := answerer.pressIgnore(ctx)
	// Why the dialogs read before the press on a failure: stopping at the prompt and showing it is
	// the safe side when uloop could not answer it.
	if pressErr != nil || !result.Pressed {
		return dialogs, nil
	}
	answerer.ignored = true
	// Why the rest of the earlier reading instead of a new one: right after the press the closing
	// prompt can still be listed, and the wait would stop at it. An empty rest lets the wait go on;
	// any dialog behind the prompt stops it as before.
	return dialogsOtherThanSafeModePrompt(dialogs), nil
}

// dialogsOtherThanSafeModePrompt returns dialogs without any listing of the Safe Mode prompt.
// Why every listing and not only the first: a real Editor on macOS listed the same prompt twice,
// and dropping only the first would stop the wait at a duplicate of the prompt just pressed.
func dialogsOtherThanSafeModePrompt(dialogs []unitydialog.Dialog) []unitydialog.Dialog {
	others := make([]unitydialog.Dialog, 0, len(dialogs))
	for _, dialog := range dialogs {
		if !isSafeModePrompt(dialog) {
			others = append(others, dialog)
		}
	}
	return others
}
