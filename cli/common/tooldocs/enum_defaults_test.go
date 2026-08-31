package tooldocs

import (
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/tools"
)

// Verifies an enum property whose cached default is the C# enum's ordinal renders the member name
// in --help. Unity's schema cache serializes enum defaults as numbers, so --help showed
// "default: 0" for a value that has to be passed as "Press".
func TestOptionDescriptionRendersEnumDefaultByName(t *testing.T) {
	property := tools.ToolProperty{
		Type:         "string",
		Description:  "Keyboard action",
		DefaultValue: float64(0),
		Enum:         []string{"Press", "KeyDown", "KeyUp", "ReleaseAll"},
	}

	description := optionDescription("simulate-keyboard", "Action", property)
	if !strings.Contains(description, "default: Press") {
		t.Errorf("description = %q, want it to contain %q", description, "default: Press")
	}
}

// Verifies a string default that already names an enum member is passed through untouched.
func TestOptionDescriptionKeepsNamedEnumDefault(t *testing.T) {
	property := tools.ToolProperty{
		Type:         "string",
		Description:  "Pause point mode",
		DefaultValue: "single-shot",
		Enum:         []string{"single-shot", "repeat"},
	}

	description := optionDescription("enable-pause-point", "Mode", property)
	if !strings.Contains(description, "default: single-shot") {
		t.Errorf("description = %q, want it to contain %q", description, "default: single-shot")
	}
}

// Verifies a numeric default on a property with no enum stays numeric, so --timeout-seconds does
// not get mistaken for an enum ordinal.
func TestOptionDescriptionKeepsNumericDefaultWithoutEnum(t *testing.T) {
	property := tools.ToolProperty{
		Type:         "number",
		Description:  "Timeout",
		DefaultValue: float64(30),
		Enum:         nil,
	}

	description := optionDescription("await-pause-point", "TimeoutSeconds", property)
	if !strings.Contains(description, "default: 30") {
		t.Errorf("description = %q, want it to contain %q", description, "default: 30")
	}
}

// Verifies an ordinal outside the enum's range is left as-is rather than reported as some other
// member: guessing a name there would be worse than showing the raw value.
func TestEnumValueForNumericDefaultRejectsOutOfRangeOrdinal(t *testing.T) {
	if value, ok := EnumValueForNumericDefault(float64(7), []string{"Press", "KeyDown"}); ok {
		t.Errorf("out-of-range ordinal resolved to %q, want no conversion", value)
	}
}

// Verifies a fractional default is not treated as an ordinal, since no enum member can match it.
func TestEnumValueForNumericDefaultRejectsFractionalOrdinal(t *testing.T) {
	if value, ok := EnumValueForNumericDefault(0.5, []string{"Press", "KeyDown"}); ok {
		t.Errorf("fractional ordinal resolved to %q, want no conversion", value)
	}
}
