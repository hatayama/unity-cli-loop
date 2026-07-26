package tooldocs

import (
	"testing"

	"github.com/hatayama/unity-cli-loop/common/tools"
)

// Verifies a negated boolean whose description came from a skill parameter table prints that text
// verbatim. Those rows are written from the flag's point of view, and the summary this replaced
// discarded them in favor of a synthesized "Disable <Name>".
func TestOptionSummaryKeepsASkillSourcedNegatedBooleanDescription(t *testing.T) {
	summary := OptionSummary("get-hierarchy", "IncludeComponents", tools.ToolProperty{
		Type:                    "boolean",
		Default:                 true,
		Description:             "Exclude component information",
		SkillSourcedDescription: true,
	})

	if summary != "Exclude component information" {
		t.Errorf("a skill-sourced description must be printed as written: %q", summary)
	}
}

// Verifies a negated boolean with no skill behind it still gets a synthesized summary. A custom
// command's author writes the property in the positive sense, so printing "Show my overlay" against
// --no-show-my-overlay would state the opposite of what the flag does.
func TestOptionSummarySynthesizesForANegatedBooleanWithNoSkill(t *testing.T) {
	summary := OptionSummary("my-custom-command", "ShowMyOverlay", tools.ToolProperty{
		Type:        "boolean",
		Default:     true,
		Description: "Show my overlay",
	})

	if summary != "Disable show my overlay" {
		t.Errorf("a description with no skill behind it must be synthesized: %q", summary)
	}
}

// Verifies the branch is on where the text came from, not on how it is worded: a description that
// happens to start with "Disable" is no longer what decides the outcome.
func TestOptionSummaryIgnoresTheWordingOfTheDescription(t *testing.T) {
	summary := OptionSummary("my-custom-command", "WaitForThing", tools.ToolProperty{
		Type:        "boolean",
		Default:     true,
		Description: "Disable the wait that this custom command performs",
	})

	if summary != "Disable wait for thing" {
		t.Errorf("wording must not decide the branch: %q", summary)
	}
}

// Verifies a plain (non-negated) option is unaffected by provenance, since its description already
// reads correctly against its own flag name.
func TestOptionSummaryKeepsPlainOptionDescriptions(t *testing.T) {
	summary := OptionSummary("my-custom-command", "Amount", tools.ToolProperty{
		Type:        "number",
		Description: "How much to apply",
	})

	if summary != "How much to apply" {
		t.Errorf("a plain option's description must be printed as written: %q", summary)
	}
}
