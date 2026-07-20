package dispatcher

import (
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

// Verifies the completion stub silently handles any arguments so shell rc
// files with a stale `eval "$(uloop completion --shell zsh)"` block keep
// starting up without an unknown-command error.
func TestTryHandleCompletionRequestIsSilentNoOpForAnyArgs(t *testing.T) {
	for _, args := range [][]string{
		{clicore.CompletionCommand},
		{clicore.CompletionCommand, "--shell", "zsh"},
		{clicore.CompletionCommand, "--install"},
		{clicore.CompletionCommand, "--help"},
		{clicore.CompletionCommand, "--anything", "unexpected"},
	} {
		handled, code := tryHandleCompletionRequest(args)

		if !handled {
			t.Fatalf("%v: completion command must be handled", args)
		}
		if code != 0 {
			t.Fatalf("%v: expected exit code 0, got %d", args, code)
		}
	}
}

// Verifies non-completion commands are left unhandled so normal dispatch continues.
func TestTryHandleCompletionRequestIgnoresOtherCommands(t *testing.T) {
	handled, _ := tryHandleCompletionRequest([]string{clicore.CompileCommandName})
	if handled {
		t.Fatal("non-completion command must not be handled by the completion stub")
	}
}
