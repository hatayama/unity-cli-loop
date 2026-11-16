package dispatcher

import (
	"fmt"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

func launchEditorVersionRequiresRestartError(editorVersion string) error {
	return &clierrors.ArgumentError{
		Message:  "--editor-version requires --restart when Unity is already running for this project.",
		Option:   "--editor-version",
		Received: editorVersion,
		Command:  clicore.LaunchCommandName,
		NextActions: []string{
			fmt.Sprintf("Run `uloop launch --restart --editor-version %s` to relaunch Unity with the requested Editor version.", editorVersion),
		},
	}
}
