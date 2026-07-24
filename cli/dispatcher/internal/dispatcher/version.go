package dispatcher

import (
	"io"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

func tryHandleVersionRequest(args []string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 || args[0] != clicore.VersionCommandName {
		return false, 0
	}
	if clicore.ContainsHelpRequest(args[1:]) {
		printNativeSingleCommandHelp(clicore.VersionCommandName, stdout)
		return true, 0
	}
	if len(args) == 1 {
		writeDispatcherVersionOutput(stdout, false)
		return true, 0
	}
	if len(args) == 2 && args[1] == "--json" {
		writeDispatcherVersionOutput(stdout, true)
		return true, 0
	}

	unknownOption := args[1]
	if unknownOption == "--json" && len(args) > 2 {
		unknownOption = args[2]
	}
	clierrors.WriteClassifiedError(stderr, &clierrors.ArgumentError{
		Message:     "Unknown version option: " + unknownOption,
		Option:      unknownOption,
		Command:     clicore.VersionCommandName,
		NextActions: []string{"Run `uloop version --help` to inspect supported options."},
	}, clierrors.ErrorContext{Command: clicore.VersionCommandName})
	return true, 1
}
