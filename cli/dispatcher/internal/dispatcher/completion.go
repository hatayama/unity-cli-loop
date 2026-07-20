package dispatcher

import (
	"github.com/hatayama/unity-cli-loop/common/clicore"
)

// tryHandleCompletionRequest keeps `completion` as a harmless no-op: it used
// to generate and install shell completion scripts, but that feature has been
// removed. Shells that ran `uloop completion --install` before this change
// still run `eval "$(uloop completion --shell zsh)"` on every shell startup,
// so this must stay silent and exit 0 instead of erroring on unknown args.
func tryHandleCompletionRequest(args []string) (bool, int) {
	if len(args) == 0 || args[0] != clicore.CompletionCommand {
		return false, 0
	}
	return true, 0
}
