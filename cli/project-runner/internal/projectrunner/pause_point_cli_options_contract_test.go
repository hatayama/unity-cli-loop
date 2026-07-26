package projectrunner

import (
	"testing"

	"github.com/hatayama/unity-cli-loop/common/tooldocs"
)

// pausePointCLIOnlySampleArgs supplies one accepted argv form per CLI-only pause-point flag. A flag
// added to the shared table without an entry here fails the contract tests below rather than
// silently going unchecked.
var pausePointCLIOnlySampleArgs = map[string][]string{
	tooldocs.PausePointEnableAwaitFlagName:           {"--await"},
	tooldocs.PausePointCapturedVariablesFlagName:     {"--captured-variables", "names"},
	tooldocs.PausePointCapturedVariableNamesFlagName: {"--captured-variable-names", "score"},
	tooldocs.PausePointExpectFlagName:                {"--expect", "score=1"},
	tooldocs.PausePointTriggerFlagName:               {"--trigger", "simulate-keyboard --action Press --key Space"},
	tooldocs.PausePointResumePlayFlagName:            {"--resume-play"},
}

// Verifies every CLI-only flag that enable-pause-point's --help advertises is actually consumed by
// the runner's enable-pause-point argument parser. The dispatcher self-updates while the project
// runner stays pinned, so a dispatcher-side table that grows a flag the pinned runner does not
// accept would advertise an option that fails on use; this contract catches that drift.
func TestPausePointEnableHelpOptionsAreAcceptedByTheEnableParser(t *testing.T) {
	for _, option := range tooldocs.PausePointEnableCLIOnlyOptions() {
		args, ok := pausePointCLIOnlySampleArgs[option.FlagName]
		if !ok {
			t.Fatalf("no sample argv for --%s: add one to pausePointCLIOnlySampleArgs", option.FlagName)
		}

		// Every flag but --await itself requires --await, so it is always passed alongside.
		enableArgs := append([]string{"--" + tooldocs.PausePointEnableAwaitFlagName}, args...)
		remaining, _, _, _, _, _, _, _, err := extractPausePointEnableAwaitFlags(enableArgs)
		if err != nil {
			t.Errorf("enable-pause-point parser rejected advertised option --%s: %v", option.FlagName, err)
			continue
		}
		if len(remaining) != 0 {
			t.Errorf("enable-pause-point parser did not consume advertised option --%s: remaining %v",
				option.FlagName, remaining)
		}
	}
}

// Verifies the CLI-only flags shared with await-pause-point are accepted by the wait parser as
// well, since enable-pause-point --help documents them as "same as await-pause-point's --x".
func TestPausePointSharedHelpOptionsAreAcceptedByTheWaitParser(t *testing.T) {
	for _, option := range tooldocs.PausePointEnableCLIOnlyOptions() {
		if option.FlagName == tooldocs.PausePointEnableAwaitFlagName {
			// --await only exists on enable-pause-point: await-pause-point is the wait itself.
			continue
		}

		args, ok := pausePointCLIOnlySampleArgs[option.FlagName]
		if !ok {
			t.Fatalf("no sample argv for --%s: add one to pausePointCLIOnlySampleArgs", option.FlagName)
		}

		waitArgs := append([]string{"--id", "marker"}, args...)
		if _, err := parseWaitForPausePointOptions(waitArgs); err != nil {
			t.Errorf("await-pause-point parser rejected shared option --%s: %v", option.FlagName, err)
		}
	}
}
