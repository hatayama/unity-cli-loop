package tooldocs

import "strings"

// enable-pause-point accepts six orchestration flags that exist only in the CLI: they are parsed
// out of the argv before the Unity-side EnablePausePointSchema is consulted, so nothing in the
// tool schema describes them. Both listings that document a tool's options — the dispatcher's
// `--help` table and the project runner's `uloop list` output — therefore have to add them by
// hand, and they drifted: `uloop list` documented all six while `--help` documented none.
// Defining them once here makes both listings read the same table.
const (
	PausePointEnableAwaitFlagName           = "await"
	PausePointCapturedVariablesFlagName     = "captured-variables"
	PausePointCapturedVariableNamesFlagName = "captured-variable-names"
	PausePointExpectFlagName                = "expect"
	PausePointTriggerFlagName               = "trigger"
	PausePointResumePlayFlagName            = "resume-play"
)

// Accepted --captured-variables values. Declared here because they appear in the option listings;
// the runner's own mode type is defined from these constants so the two cannot drift.
const (
	PausePointCapturedVariablesModeFull  = "full"
	PausePointCapturedVariablesModeNames = "names"
)

// pausePointEnableCommandName is private to this package: importing the clicore package that owns
// the command-name constants would be an import cycle, the same reason
// executeDynamicCodeCommandName is declared locally.
const pausePointEnableCommandName = "enable-pause-point"

// PausePointCLIOnlyOption describes one CLI-only pause-point flag in the shape both listings need:
// `--help` renders FlagName/Type/Description, and `uloop list` additionally reports Type and Values
// as structured fields.
type PausePointCLIOnlyOption struct {
	FlagName    string
	Type        string
	Description string
	Values      []string
}

// PausePointEnableCLIOnlyOptions returns enable-pause-point's CLI-only flags. A fresh slice is
// built per call so a caller that sorts or appends cannot mutate the shared table.
func PausePointEnableCLIOnlyOptions() []PausePointCLIOnlyOption {
	return []PausePointCLIOnlyOption{
		{
			FlagName: PausePointEnableAwaitFlagName,
			Type:     "boolean",
			Description: "Wait for the marker to be hit (or time out) after enabling, in a single call, " +
				"instead of a separate await-pause-point call",
		},
		{
			FlagName:    PausePointCapturedVariablesFlagName,
			Type:        "string",
			Description: "Requires --await. Same as await-pause-point's --captured-variables",
			Values: []string{
				PausePointCapturedVariablesModeFull,
				PausePointCapturedVariablesModeNames,
			},
		},
		{
			FlagName:    PausePointCapturedVariableNamesFlagName,
			Type:        "string",
			Description: "Requires --await. Same as await-pause-point's --captured-variable-names",
		},
		{
			FlagName:    PausePointExpectFlagName,
			Type:        "string",
			Description: "Requires --await. Same as await-pause-point's --expect (repeatable)",
		},
		{
			FlagName:    PausePointTriggerFlagName,
			Type:        "string",
			Description: "Requires --await. Same as await-pause-point's --trigger",
		},
		{
			FlagName: PausePointResumePlayFlagName,
			Type:     "boolean",
			Description: "Requires --await. After confirming the marker is armed, resume PlayMode if " +
				"paused (before --trigger), so a paused-arm workflow can fire input in one call",
		},
	}
}

func appendPausePointEnableCLIOnlyOptionHelpEntries(
	toolName string,
	entries []OptionHelpEntry,
) []OptionHelpEntry {
	if toolName != pausePointEnableCommandName {
		return entries
	}

	for _, option := range PausePointEnableCLIOnlyOptions() {
		optionName := "--" + option.FlagName
		if hasOptionHelpEntry(entries, optionName) {
			continue
		}
		entries = append(entries, OptionHelpEntry{
			Name:        optionName,
			Usage:       pausePointCLIOnlyOptionUsage(optionName, option),
			Description: pausePointCLIOnlyOptionDescription(option),
		})
	}
	return entries
}

func pausePointCLIOnlyOptionUsage(optionName string, option PausePointCLIOnlyOption) string {
	if option.Type == "boolean" {
		return optionName
	}
	return optionName + " <value>"
}

// pausePointCLIOnlyOptionDescription appends the accepted values the same way the schema-driven
// rows do, so a CLI-only row is indistinguishable in shape from a schema-derived one.
func pausePointCLIOnlyOptionDescription(option PausePointCLIOnlyOption) string {
	if len(option.Values) == 0 {
		return option.Description
	}
	return option.Description + "; values: " + strings.Join(option.Values, optionValuesSeparator)
}

func hasOptionHelpEntry(entries []OptionHelpEntry, name string) bool {
	for _, entry := range entries {
		if entry.Name == name {
			return true
		}
	}
	return false
}
