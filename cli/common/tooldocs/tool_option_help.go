package tooldocs

import (
	"fmt"
	"sort"
	"strings"

	"github.com/hatayama/unity-cli-loop/common/tools"
)

// --code-file is CLI-side sugar for execute-dynamic-code: it loads a C# source file into
// the Code parameter instead of requiring the value inline, so option-name and
// option-help listings need to know about it even though Unity's tool schema does not.
const (
	DynamicCodeFileFlagName    = "code-file"
	DynamicCodeFileOptionName  = "--" + DynamicCodeFileFlagName
	DynamicCodeFileOptionUsage = DynamicCodeFileOptionName + " <path>"
)

const DynamicCodeFileOptionDescription = "Read C# code from a file instead of --code when shell quoting would alter multiline code"

const (
	RunTestsSkipCompileFlagName    = "skip-compile"
	RunTestsSkipCompileOptionName  = "--" + RunTestsSkipCompileFlagName
	RunTestsSkipCompileOptionUsage = RunTestsSkipCompileOptionName
)

const RunTestsSkipCompileOptionDescription = "Skip the automatic compile before running tests; use only while validating active hot-reload patches."

// optionValuesSeparator joins the accepted values of an option in help output.
const optionValuesSeparator = "|"

const (
	compileCommandName            = "compile"
	executeDynamicCodeCommandName = "execute-dynamic-code"
	runTestsCommandName           = "run-tests"
)

// OptionHelpEntry is one row of a tool's --help option listing.
type OptionHelpEntry struct {
	Name        string
	Usage       string
	Description string
}

func VisibleOptionHelpEntriesForTool(tool tools.ToolDefinition) []OptionHelpEntry {
	schema := tool.EffectiveInputSchema()
	entries := make([]OptionHelpEntry, 0, len(schema.Properties))
	for propertyName, property := range schema.Properties {
		if property.Hidden {
			continue
		}

		optionName := "--" + OptionNameForProperty(tool.Name, propertyName, property)
		entries = append(entries, OptionHelpEntry{
			Name:        optionName,
			Usage:       optionUsage(optionName, property),
			Description: optionDescription(tool.Name, propertyName, property),
		})
	}

	entries = appendDynamicCodeFileOptionHelpEntry(tool, entries)
	entries = appendRunTestsSkipCompileOptionHelpEntry(tool, entries)
	entries = appendPausePointEnableCLIOnlyOptionHelpEntries(tool.Name, entries)
	sort.Slice(entries, func(i int, j int) bool {
		return entries[i].Name < entries[j].Name
	})
	return entries
}

func optionUsage(optionName string, property tools.ToolProperty) string {
	if IsBooleanProperty(property) {
		return optionName
	}
	return optionName + " <" + optionValueName(property) + ">"
}

func optionValueName(property tools.ToolProperty) string {
	switch strings.ToLower(property.Type) {
	case "integer":
		return "integer"
	case "number":
		return "number"
	case "array":
		return "value[,value]"
	case "object":
		return "json"
	default:
		return "value"
	}
}

func optionDescription(toolName string, propertyName string, property tools.ToolProperty) string {
	if isCompileReloadExternalSceneChangesOption(toolName, propertyName, property) {
		return OptionSummary(toolName, propertyName, property) + "; default: auto-reload enabled"
	}

	parts := []string{}
	if description := OptionSummary(toolName, propertyName, property); description != "" {
		parts = append(parts, description)
	}
	// An empty-string default is what Unity reports for any unset string parameter, so rendering it
	// would print a bare "default: " with nothing after it.
	if propertyDefault := property.EffectiveDefault(); propertyDefault != nil && propertyDefault != "" {
		parts = append(parts, "default: "+defaultValueText(propertyDefault, property.Enum))
	}
	if len(property.Enum) > 0 {
		parts = append(parts, "values: "+strings.Join(property.Enum, optionValuesSeparator))
	}
	return strings.Join(parts, "; ")
}

func defaultValueText(value any, enumValues []string) string {
	if boolValue, ok := value.(bool); ok {
		if boolValue {
			return "enabled"
		}
		return "disabled"
	}
	if enumValue, ok := EnumValueForNumericDefault(value, enumValues); ok {
		return enumValue
	}
	return fmt.Sprint(value)
}

// OptionSummary is the help text for one option. A description that came from a skill parameter table
// is printed verbatim, including for a negated boolean flag: those rows are already written from the
// flag's point of view ("Exclude component information" for --no-include-components).
//
// Only a description with no skill behind it - a project-local custom command - is synthesized. Its
// author wrote the property in the positive sense, so printing it against a --no-<name> flag would
// read as the opposite of what the flag does. The branch is on where the text came from, never on how
// the text is worded.
func OptionSummary(toolName string, propertyName string, property tools.ToolProperty) string {
	if IsNegatedBooleanProperty(property) && !property.SkillSourcedDescription {
		return "Disable " + pascalToWords(propertyName)
	}
	return FirstHelpLine(property.Description)
}

func appendDynamicCodeFileOptionName(tool tools.ToolDefinition, options []string) []string {
	if tool.Name != executeDynamicCodeCommandName {
		return options
	}
	for _, option := range options {
		if option == DynamicCodeFileOptionName {
			return options
		}
	}
	return append(options, DynamicCodeFileOptionName)
}

func appendDynamicCodeFileOptionHelpEntry(tool tools.ToolDefinition, entries []OptionHelpEntry) []OptionHelpEntry {
	if tool.Name != executeDynamicCodeCommandName {
		return entries
	}
	if hasOptionHelpEntry(entries, DynamicCodeFileOptionName) {
		return entries
	}
	return append(entries, OptionHelpEntry{
		Name:        DynamicCodeFileOptionName,
		Usage:       DynamicCodeFileOptionUsage,
		Description: DynamicCodeFileOptionDescription,
	})
}

func appendRunTestsSkipCompileOptionHelpEntry(tool tools.ToolDefinition, entries []OptionHelpEntry) []OptionHelpEntry {
	if tool.Name != runTestsCommandName {
		return entries
	}
	if hasOptionHelpEntry(entries, RunTestsSkipCompileOptionName) {
		return entries
	}
	return append(entries, OptionHelpEntry{
		Name:        RunTestsSkipCompileOptionName,
		Usage:       RunTestsSkipCompileOptionUsage,
		Description: RunTestsSkipCompileOptionDescription,
	})
}
