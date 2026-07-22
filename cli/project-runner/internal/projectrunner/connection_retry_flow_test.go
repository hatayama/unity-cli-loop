package projectrunner

import (
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

// Verifies only execute-dynamic-code gets main-thread stall tolerance: other commands'
// stalls must keep failing as a genuine freeze signal.
func TestCommandNeedsSelfInducedStallToleranceOnlyForExecuteDynamicCode(t *testing.T) {
	if !commandNeedsSelfInducedStallTolerance(clicore.ExecuteDynamicCodeCommandName) {
		t.Fatal("expected execute-dynamic-code to need self-induced stall tolerance")
	}
	if commandNeedsSelfInducedStallTolerance("compile") {
		t.Fatal("expected compile to not need self-induced stall tolerance")
	}
	if commandNeedsSelfInducedStallTolerance("run-tests") {
		t.Fatal("expected run-tests to not need self-induced stall tolerance")
	}
}
