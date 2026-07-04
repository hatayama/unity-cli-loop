package clicore

import (
	"io"

	"github.com/hatayama/unity-cli-loop/common/progress"
	"github.com/hatayama/unity-cli-loop/common/ui"
)

type TerminalSpinner = ui.TerminalSpinner

func NewToolSpinner(stderr io.Writer, command string) *TerminalSpinner {
	return ui.NewToolSpinner(stderr, shouldShowToolFeedback(command))
}

func NewLaunchSpinner(stdout io.Writer, stderr io.Writer) *TerminalSpinner {
	return ui.NewLaunchSpinner(stdout, stderr)
}

func NewSpinnerProgressFunc(spinner *TerminalSpinner, executingMessage string) progress.Func {
	return ui.NewSpinnerProgressFunc(spinner, executingMessage)
}

func shouldShowToolFeedback(command string) bool {
	return command != ExecuteDynamicCodeCommandName
}
