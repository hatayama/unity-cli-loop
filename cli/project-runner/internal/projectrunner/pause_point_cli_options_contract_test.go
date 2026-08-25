package projectrunner

import (
	"sort"
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

// Verifies the documented flag set and the parsed flag set are exactly the same. The one-directional
// subset checks below cannot see the original defect from the other side: a flag the parser accepts
// but no listing documents is undiscoverable, which is how all six of these flags came to be missing
// from --help in the first place.
func TestPausePointEnableDocumentedAndParsedFlagsMatch(t *testing.T) {
	documented := map[string]bool{}
	for _, option := range tooldocs.PausePointEnableCLIOnlyOptions() {
		documented[option.FlagName] = true
	}

	for flagName := range pausePointEnableFlagHandlers {
		if !documented[flagName] {
			t.Errorf("the enable-pause-point parser accepts --%s but no option listing documents it", flagName)
		}
	}
	for flagName := range documented {
		if _, ok := pausePointEnableFlagHandlers[flagName]; !ok {
			t.Errorf("--%s is documented but the enable-pause-point parser does not accept it", flagName)
		}
	}
}

// Verifies --await has no =value form: it is passed through to Unity schema parsing, which is the
// behavior the parser had before the handler table replaced its open-coded branches.
func TestPausePointEnableAwaitRejectsValueForm(t *testing.T) {
	remaining, await, _, _, _, _, _, _, err := extractPausePointEnableAwaitFlags([]string{"--await=true"})
	if err != nil {
		t.Fatalf("--await=true should be passed through, not rejected here: %v", err)
	}
	if await {
		t.Error("--await=true must not enable the wait")
	}
	if len(remaining) != 1 || remaining[0] != "--await=true" {
		t.Errorf("--await=true was not passed through: %v", remaining)
	}
}

// Verifies an unrelated argument is passed through untouched for the schema pipeline.
func TestPausePointEnableLeavesSchemaArgumentsAlone(t *testing.T) {
	remaining, _, _, _, _, _, _, _, err := extractPausePointEnableAwaitFlags(
		[]string{"--id", "marker", "--await", "--line", "42"})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	expected := []string{"--id", "marker", "--line", "42"}
	if len(remaining) != len(expected) {
		t.Fatalf("remaining = %v, want %v", remaining, expected)
	}
	for index, argument := range expected {
		if remaining[index] != argument {
			t.Fatalf("remaining = %v, want %v", remaining, expected)
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

// runnerNativeCommandSampleArgs supplies one accepted argv form per flag advertised by a
// runner-owned native command's --help. A flag added to a tooldocs table without an entry
// here fails the contract test below rather than silently going unchecked.
var runnerNativeCommandSampleArgs = map[string][]string{
	"--" + PausePointIDFlagName:           {"--id", "marker"},
	"--file":                              {"--file", "Assets/Scripts/Marker.cs", "--line", "42"},
	"--line":                              {"--file", "Assets/Scripts/Marker.cs", "--line", "42"},
	"--" + PausePointTimeoutFlagName:      {"--timeout-seconds", "5"},
	"--" + PausePointLogsMaxCountFlagName: {"--matching-logs-max-count", "3"},
	"--" + tooldocs.PausePointCapturedVariablesFlagName:     {"--captured-variables", "names"},
	"--" + tooldocs.PausePointCapturedVariableNamesFlagName: {"--captured-variable-names", "score"},
	"--" + tooldocs.PausePointExpectFlagName:                {"--expect", "score=1"},
	"--" + tooldocs.PausePointTriggerFlagName:               {"--trigger", "focus-window"},
	"--" + tooldocs.PausePointResumePlayFlagName:            {"--resume-play"},
}

// Verifies every flag pause-point-status advertises in its --help output is actually accepted by its
// own parser. The status parser is a separate switch from the await one, so a flag added to the help
// table alone would be advertised and then rejected as unknown on use.
func TestPausePointStatusHelpOptionsAreAcceptedByTheStatusParser(t *testing.T) {
	for _, option := range tooldocs.PausePointStatusCLIOnlyOptions() {
		optionName := "--" + option.FlagName
		args, ok := runnerNativeCommandSampleArgs[optionName]
		if !ok {
			t.Fatalf("no sample argv for %s: add one to runnerNativeCommandSampleArgs", optionName)
		}

		statusArgs := pausePointQuerySampleArgs(optionName, args)
		if _, err := parsePausePointStatusOptions(statusArgs); err != nil {
			t.Errorf("pause-point-status parser rejected advertised option %s: %v", optionName, err)
		}
	}
}

// Verifies the same for await-pause-point, whose help table is the larger of the two.
func TestPausePointAwaitHelpOptionsAreAcceptedByTheWaitParser(t *testing.T) {
	for _, option := range tooldocs.PausePointAwaitCLIOnlyOptions() {
		optionName := "--" + option.FlagName
		args, ok := runnerNativeCommandSampleArgs[optionName]
		if !ok {
			t.Fatalf("no sample argv for %s: add one to runnerNativeCommandSampleArgs", optionName)
		}

		waitArgs := pausePointQuerySampleArgs(optionName, args)
		if _, err := parseWaitForPausePointOptions(waitArgs); err != nil {
			t.Errorf("await-pause-point parser rejected advertised option %s: %v", optionName, err)
		}
	}
}

// pausePointQuerySampleArgs supplies a complete marker target for each documented query option.
// File and line must travel together and cannot be combined with --id, unlike every other option.
func pausePointQuerySampleArgs(optionName string, args []string) []string {
	if optionName == "--file" || optionName == "--line" {
		return args
	}
	return append([]string{"--" + PausePointIDFlagName, "marker"}, args...)
}

// pausePointAwaitAdvertisedFlagNames is the await-pause-point advertised flag set. These are
// test-local literals, not tooldocs constants, so deleting a table row fails even when the
// switch-based parser still accepts the flag.
var pausePointAwaitAdvertisedFlagNames = []string{
	"id",
	"file",
	"line",
	"timeout-seconds",
	"matching-logs-max-count",
	"captured-variables",
	"captured-variable-names",
	"expect",
	"trigger",
	"resume-play",
}

// pausePointStatusAdvertisedFlagNames is the pause-point-status advertised flag set. Same
// test-local-literal rule as pausePointAwaitAdvertisedFlagNames.
var pausePointStatusAdvertisedFlagNames = []string{
	"id",
	"file",
	"line",
	"captured-variables",
	"captured-variable-names",
	"expect",
}

// Verifies the await-pause-point option table's flag names equal the advertised set. Table-to-parser
// acceptance cannot see a deleted row: the parsers are switch-based, so a missing help row still
// leaves the flag accepted.
func TestPausePointAwaitCLIOnlyOptionsMatchFixedFlagSet(t *testing.T) {
	assertPausePointCLIOnlyFlagNames(t, tooldocs.PausePointAwaitCLIOnlyOptions(), pausePointAwaitAdvertisedFlagNames)
}

// Verifies the same reverse-direction contract for pause-point-status.
func TestPausePointStatusCLIOnlyOptionsMatchFixedFlagSet(t *testing.T) {
	assertPausePointCLIOnlyFlagNames(t, tooldocs.PausePointStatusCLIOnlyOptions(), pausePointStatusAdvertisedFlagNames)
}

func assertPausePointCLIOnlyFlagNames(
	t *testing.T,
	options []tooldocs.PausePointCLIOnlyOption,
	expected []string,
) {
	t.Helper()
	actual := make([]string, 0, len(options))
	for _, option := range options {
		actual = append(actual, option.FlagName)
	}
	sort.Strings(actual)
	want := append([]string{}, expected...)
	sort.Strings(want)
	if len(actual) != len(want) {
		t.Fatalf("flag names = %v, want %v", actual, want)
	}
	for index, name := range want {
		if actual[index] != name {
			t.Fatalf("flag names = %v, want %v", actual, want)
		}
	}
}

// Verifies every await-pause-point and pause-point-status help table row has a description. A
// names-only listing is how --help used to render these commands, so an empty Description would
// recreate that gap even after the renderer learned to print a second column.
func TestPausePointAwaitAndStatusCLIOnlyOptionsHaveDescriptions(t *testing.T) {
	for _, option := range tooldocs.PausePointAwaitCLIOnlyOptions() {
		if option.Description == "" {
			t.Errorf("await-pause-point --%s has an empty description", option.FlagName)
		}
	}
	for _, option := range tooldocs.PausePointStatusCLIOnlyOptions() {
		if option.Description == "" {
			t.Errorf("pause-point-status --%s has an empty description", option.FlagName)
		}
	}
}
