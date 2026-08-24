package projectrunner

import (
	"fmt"
	"sort"
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
)

// Why not 1 or 2: kebab prefixes like "no" (negated booleans) and single letters would
// pair unrelated flags. Three characters is the shortest token that still encodes a concept.
const minSharedKebabTokenLength = 3

type visibleToolOption struct {
	name     string
	property clicore.ToolProperty
}

func unexpectedArgumentError(tool clicore.ToolDefinition, arg string, lastConsumedArrayOption string) *clierrors.ArgumentError {
	suggestions := enumValueOptionSuggestions(tool, arg)
	if lastConsumedArrayOption != "" {
		suggestions = append([]string{
			fmt.Sprintf(
				"Pass multiple values as one comma-separated list: %s value1,value2",
				lastConsumedArrayOption,
			),
		}, suggestions...)
	}
	return &clierrors.ArgumentError{
		Message:     "Unexpected argument: " + arg,
		Received:    arg,
		Command:     tool.Name,
		NextActions: prependSuggestions(suggestions, "Pass tool inputs as `--option value` pairs."),
	}
}

func unknownToolOptionError(tool clicore.ToolDefinition, flagName string) *clierrors.ArgumentError {
	return &clierrors.ArgumentError{
		Message: "Unknown option for " + tool.Name + ": --" + flagName,
		Option:  "--" + flagName,
		Command: tool.Name,
		NextActions: prependSuggestions(
			unknownOptionSuggestions(tool, flagName),
			"Run `uloop "+tool.Name+" --help` to inspect supported options.",
		),
	}
}

func unknownOptionSuggestions(tool clicore.ToolDefinition, flagName string) []string {
	// Why enum first: `--status` is the enum value itself, not a misspelled option name.
	// A name-similarity suggestion would hide the option+value form the caller actually needs.
	enumSuggestions := enumValueOptionSuggestions(tool, flagName)
	if len(enumSuggestions) > 0 {
		return enumSuggestions
	}

	if suggestion, ok := closestOptionNameSuggestion(tool, flagName); ok {
		return []string{suggestion}
	}

	return sharedTokenOptionSuggestions(tool, flagName)
}

func enumValueOptionSuggestions(tool clicore.ToolDefinition, received string) []string {
	suggestions := make([]string, 0)
	for _, option := range visibleToolOptions(tool) {
		matchedValue := matchingEnumValue(option.property.Enum, received)
		if matchedValue == "" {
			continue
		}
		suggestions = append(suggestions, didYouMeanOptionValue(tool.Name, option.name, matchedValue))
	}
	return suggestions
}

func matchingEnumValue(enumValues []string, received string) string {
	for _, enumValue := range enumValues {
		if strings.EqualFold(enumValue, received) {
			return enumValue
		}
	}
	return ""
}

func closestOptionNameSuggestion(tool clicore.ToolDefinition, flagName string) (string, bool) {
	unknown := strings.ToLower(flagName)
	threshold := max(2, len(unknown)/3)
	bestName := ""
	bestDistance := threshold + 1

	for _, option := range visibleToolOptions(tool) {
		distance := levenshteinDistance(unknown, strings.ToLower(option.name))
		if distance > threshold || distance >= bestDistance {
			continue
		}
		bestDistance = distance
		bestName = option.name
	}

	if bestName == "" {
		return "", false
	}
	return didYouMeanOption(tool.Name, bestName), true
}

func sharedTokenOptionSuggestions(tool clicore.ToolDefinition, flagName string) []string {
	unknown := strings.ToLower(flagName)
	matches := make([]visibleToolOption, 0)
	bestDistance := -1
	for _, option := range visibleToolOptions(tool) {
		if !hasSharedKebabToken(flagName, option.name) {
			continue
		}
		distance := levenshteinDistance(unknown, strings.ToLower(option.name))
		if bestDistance < 0 || distance < bestDistance {
			bestDistance = distance
			matches = []visibleToolOption{option}
			continue
		}
		if distance == bestDistance {
			matches = append(matches, option)
		}
	}

	suggestions := make([]string, 0, len(matches))
	for _, option := range matches {
		suggestions = append(suggestions, didYouMeanOption(tool.Name, option.name))
	}
	return suggestions
}

func visibleToolOptions(tool clicore.ToolDefinition) []visibleToolOption {
	schema := tool.EffectiveInputSchema()
	propertyNames := make([]string, 0, len(schema.Properties))
	for propertyName := range schema.Properties {
		propertyNames = append(propertyNames, propertyName)
	}
	sort.Strings(propertyNames)

	options := make([]visibleToolOption, 0, len(propertyNames))
	for _, propertyName := range propertyNames {
		property := schema.Properties[propertyName]
		if property.Hidden {
			continue
		}
		options = append(options, visibleToolOption{
			name:     tooldocs.OptionNameForProperty(tool.Name, propertyName, property),
			property: property,
		})
	}
	options = appendDynamicCodeFileSuggestionOption(tool, options)
	return appendPausePointEnableSuggestionOptions(tool, options)
}

func appendDynamicCodeFileSuggestionOption(tool clicore.ToolDefinition, options []visibleToolOption) []visibleToolOption {
	if tool.Name != clicore.ExecuteDynamicCodeCommandName {
		return options
	}
	return appendSuggestionOption(options, tooldocs.DynamicCodeFileFlagName)
}

func appendPausePointEnableSuggestionOptions(tool clicore.ToolDefinition, options []visibleToolOption) []visibleToolOption {
	if tool.Name != pausePointEnableCommandName {
		return options
	}
	for _, option := range tooldocs.PausePointEnableCLIOnlyOptions() {
		options = appendSuggestionOption(options, option.FlagName)
	}
	return options
}

func appendSuggestionOption(options []visibleToolOption, name string) []visibleToolOption {
	for _, option := range options {
		if option.name == name {
			return options
		}
	}
	return append(options, visibleToolOption{name: name})
}

func hasSharedKebabToken(left string, right string) bool {
	for _, leftToken := range strings.Split(strings.ToLower(left), "-") {
		if len(leftToken) < minSharedKebabTokenLength {
			continue
		}
		for _, rightToken := range strings.Split(strings.ToLower(right), "-") {
			if leftToken == rightToken {
				return true
			}
		}
	}
	return false
}

func prependSuggestions(suggestions []string, fallback string) []string {
	if len(suggestions) == 0 {
		return []string{fallback}
	}
	nextActions := make([]string, 0, len(suggestions)+1)
	nextActions = append(nextActions, suggestions...)
	return append(nextActions, fallback)
}

func didYouMeanOption(toolName string, optionName string) string {
	return "Did you mean: uloop " + toolName + " --" + optionName
}

func didYouMeanOptionValue(toolName string, optionName string, value string) string {
	return didYouMeanOption(toolName, optionName) + " " + value
}

// levenshteinDistance is local so suggestion matching does not touch cli/common
// (and therefore does not trip shared-release-input stamping).
func levenshteinDistance(left string, right string) int {
	if left == right {
		return 0
	}
	if len(left) == 0 {
		return len(right)
	}
	if len(right) == 0 {
		return len(left)
	}

	previous := make([]int, len(right)+1)
	current := make([]int, len(right)+1)
	for column := 0; column <= len(right); column++ {
		previous[column] = column
	}

	for row := 1; row <= len(left); row++ {
		current[0] = row
		for column := 1; column <= len(right); column++ {
			deletionCost := previous[column] + 1
			insertionCost := current[column-1] + 1
			substitutionCost := previous[column-1]
			if left[row-1] != right[column-1] {
				substitutionCost++
			}
			current[column] = min(deletionCost, insertionCost, substitutionCost)
		}
		previous, current = current, previous
	}

	return previous[len(right)]
}
