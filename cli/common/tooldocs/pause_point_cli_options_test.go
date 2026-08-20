package tooldocs

import (
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/tools"
)

// Verifies enable-pause-point's --help lists every CLI-only pause-point flag. The schema-driven
// loop cannot produce them because none of them exist in the Unity-side EnablePausePointSchema,
// so --help documented none of the six while `uloop list` documented all six.
func TestVisibleOptionHelpEntriesIncludePausePointEnableCLIOnlyOptions(t *testing.T) {
	tool, ok := tools.Find(tools.LoadDefault(), pausePointEnableCommandName)
	if !ok {
		t.Fatalf("embedded catalog has no %q tool", pausePointEnableCommandName)
	}

	entries := VisibleOptionHelpEntriesForTool(tool)
	for _, option := range PausePointEnableCLIOnlyOptions() {
		optionName := "--" + option.FlagName
		entry, found := findOptionHelpEntry(entries, optionName)
		if !found {
			t.Fatalf("enable-pause-point --help is missing CLI-only option %s", optionName)
		}
		if entry.Description == "" {
			t.Errorf("option %s has no description in --help", optionName)
		}
	}
}

// Verifies boolean CLI-only flags render without a value placeholder and valued ones render with
// one, so the help usage column matches how the flags are actually passed.
func TestPausePointEnableCLIOnlyOptionHelpUsage(t *testing.T) {
	tool, ok := tools.Find(tools.LoadDefault(), pausePointEnableCommandName)
	if !ok {
		t.Fatalf("embedded catalog has no %q tool", pausePointEnableCommandName)
	}

	entries := VisibleOptionHelpEntriesForTool(tool)

	awaitEntry, found := findOptionHelpEntry(entries, "--"+PausePointEnableAwaitFlagName)
	if !found {
		t.Fatalf("enable-pause-point --help is missing --%s", PausePointEnableAwaitFlagName)
	}
	if awaitEntry.Usage != "--"+PausePointEnableAwaitFlagName {
		t.Errorf("boolean flag usage = %q, want %q", awaitEntry.Usage, "--"+PausePointEnableAwaitFlagName)
	}

	triggerEntry, found := findOptionHelpEntry(entries, "--"+PausePointTriggerFlagName)
	if !found {
		t.Fatalf("enable-pause-point --help is missing --%s", PausePointTriggerFlagName)
	}
	if !strings.HasSuffix(triggerEntry.Usage, " <value>") {
		t.Errorf("valued flag usage = %q, want a value placeholder", triggerEntry.Usage)
	}
}

// Verifies await and status CLI-only tables produce --help rows with usage and description, so
// native-command help can render the same shape as schema-driven tool help.
func TestPausePointAwaitAndStatusCLIOnlyHelpEntries(t *testing.T) {
	awaitEntries := PausePointCLIOnlyHelpEntries(PausePointAwaitCLIOnlyOptions())
	if len(awaitEntries) != len(PausePointAwaitCLIOnlyOptions()) {
		t.Fatalf("await help entries = %d, want %d", len(awaitEntries), len(PausePointAwaitCLIOnlyOptions()))
	}
	for _, entry := range awaitEntries {
		if entry.Usage == "" {
			t.Errorf("await help entry %s has empty usage", entry.Name)
		}
		if entry.Description == "" {
			t.Errorf("await help entry %s has empty description", entry.Name)
		}
	}

	statusEntries := PausePointCLIOnlyHelpEntries(PausePointStatusCLIOnlyOptions())
	if len(statusEntries) != len(PausePointStatusCLIOnlyOptions()) {
		t.Fatalf("status help entries = %d, want %d", len(statusEntries), len(PausePointStatusCLIOnlyOptions()))
	}
	for _, entry := range statusEntries {
		if entry.Usage == "" {
			t.Errorf("status help entry %s has empty usage", entry.Name)
		}
		if entry.Description == "" {
			t.Errorf("status help entry %s has empty description", entry.Name)
		}
	}
}

// Verifies the CLI-only option table is not applied to unrelated tools, which would advertise
// pause-point orchestration flags on commands that reject them.
func TestVisibleOptionHelpEntriesOmitPausePointCLIOnlyOptionsForOtherTools(t *testing.T) {
	tool, ok := tools.Find(tools.LoadDefault(), compileCommandName)
	if !ok {
		t.Fatalf("embedded catalog has no %q tool", compileCommandName)
	}

	entries := VisibleOptionHelpEntriesForTool(tool)
	if _, found := findOptionHelpEntry(entries, "--"+PausePointEnableAwaitFlagName); found {
		t.Errorf("%s --help advertises --%s", compileCommandName, PausePointEnableAwaitFlagName)
	}
}

func findOptionHelpEntry(entries []OptionHelpEntry, name string) (OptionHelpEntry, bool) {
	for _, entry := range entries {
		if entry.Name == name {
			return entry, true
		}
	}
	return OptionHelpEntry{}, false
}
