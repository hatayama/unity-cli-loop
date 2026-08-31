package clicore

import (
	"io"

	"github.com/hatayama/unity-cli-loop/common/ui"
)

func NewToolSpinner(stderr io.Writer, command string) *ui.TerminalSpinner {
	return ui.NewToolSpinner(stderr, shouldShowToolFeedback(command))
}

func shouldShowToolFeedback(command string) bool {
	return command != ExecuteDynamicCodeCommandName
}
