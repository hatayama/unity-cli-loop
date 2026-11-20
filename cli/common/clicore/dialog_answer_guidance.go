package clicore

import "github.com/hatayama/unity-cli-loop/common/unityprocess/unitydialog"

// DialogAnswerNextActions tells the reader how to get the dialogs, frontmost first, closed: press a
// button of the frontmost one when uloop can, or ask the user otherwise. closedNextAction is the
// last step, which names the command to run once the dialog is closed.
func DialogAnswerNextActions(dialogs []unitydialog.Dialog, canAnswerDialogs bool, closedNextAction string) []string {
	if !canAnswerDialogs || len(dialogs) == 0 || len(dialogs[0].Buttons) == 0 {
		return unanswerableDialogNextActions(closedNextAction)
	}
	return []string{
		"Read the dialog's Title, Message, and Buttons, then press the button that fits what the user asked for: " +
			"uloop press-dialog-button --label \"<button>\".",
		"Ask the user first when no button clearly fits or the choice would discard work (such as \"Don't Save\").",
		closedNextAction,
	}
}

func unanswerableDialogNextActions(closedNextAction string) []string {
	return []string{
		"uloop cannot press a button in this dialog. Show the user its Title and Message and ask them to answer it in Unity.",
		closedNextAction,
	}
}
