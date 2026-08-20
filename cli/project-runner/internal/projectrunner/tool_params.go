package projectrunner

import (
	"encoding/json"
	"fmt"
	"strconv"
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

func buildToolParams(args []string, tool clicore.ToolDefinition) (map[string]any, string, error) {
	params := map[string]any{}
	projectPath := ""

	for index := 0; index < len(args); index++ {
		arg := args[index]
		if !strings.HasPrefix(arg, "--") {
			return nil, "", unexpectedArgumentError(tool, arg)
		}

		flag, err := parseToolFlag(arg)
		if err != nil {
			return nil, "", err
		}

		if flag.name == tooldocs.ProjectPathFlagName {
			value, consumedNext, err := flagValue(flag, args, index)
			if err != nil {
				return nil, "", err
			}
			projectPath = value
			if consumedNext {
				index++
			}
			continue
		}

		propertyName, property, negated, ok := tooldocs.FindProperty(tool, flag.name)
		if !ok {
			return nil, "", unknownToolOptionError(tool, flag.name)
		}

		option := "--" + flag.name
		if tooldocs.IsBooleanProperty(property) {
			if flag.hasValue {
				return nil, "", booleanValueArgumentError(option, flag.value)
			}
			if index+1 < len(args) && !clicore.IsNextOptionToken(args[index+1]) {
				return nil, "", booleanValueArgumentError(option, args[index+1])
			}
			params[propertyName] = !negated
			continue
		}

		value, consumedNext, err := flagValue(flag, args, index)
		if err != nil {
			return nil, "", err
		}
		if consumedNext {
			index++
		}

		converted, err := convertValue(value, property, option)
		if err != nil {
			return nil, "", err
		}
		params[propertyName] = converted
	}

	return params, projectPath, nil
}

type parsedToolFlag struct {
	name     string
	value    string
	hasValue bool
}

func parseToolFlag(arg string) (parsedToolFlag, error) {
	trimmed := strings.TrimPrefix(arg, "--")
	if trimmed == "" {
		return parsedToolFlag{}, &clierrors.ArgumentError{
			Message:     "Invalid option: " + arg,
			Option:      arg,
			NextActions: []string{"Use `--option` for boolean flags or `--option value` for valued options."},
		}
	}

	if strings.Contains(trimmed, "=") {
		parts := strings.SplitN(trimmed, "=", 2)
		if parts[1] == "" {
			return parsedToolFlag{}, clierrors.MissingValueArgumentError("--" + parts[0])
		}
		return parsedToolFlag{name: parts[0], value: parts[1], hasValue: true}, nil
	}

	return parsedToolFlag{name: trimmed}, nil
}

func flagValue(flag parsedToolFlag, args []string, index int) (string, bool, error) {
	if flag.hasValue {
		return flag.value, false, nil
	}

	if index+1 >= len(args) || clicore.IsNextOptionToken(args[index+1]) {
		return "", false, clierrors.MissingValueArgumentError("--" + flag.name)
	}

	return args[index+1], true, nil
}

func convertValue(value string, property clicore.ToolProperty, option string) (any, error) {
	switch strings.ToLower(property.Type) {
	case "boolean":
		return convertBooleanValue(value, option)
	case "integer":
		parsed, err := strconv.Atoi(value)
		if err != nil {
			return nil, clierrors.InvalidValueArgumentError(option, value, "integer")
		}
		return parsed, nil
	case "number":
		parsed, err := strconv.ParseFloat(value, 64)
		if err != nil {
			return nil, clierrors.InvalidValueArgumentError(option, value, "number")
		}
		return parsed, nil
	case "array":
		return convertArrayValue(value, option)
	case "object":
		return convertObjectValue(value, option)
	case "string":
		return convertStringValue(value, property, option)
	default:
		return value, nil
	}
}

// The C# side matches enum values case-insensitively (CaseInsensitiveStringEnumConverter),
// so rejecting an invalid value here must use the same comparison rule the receiving side
// uses, or a value this check accepts could still be rejected on the Unity side.
func convertStringValue(value string, property clicore.ToolProperty, option string) (any, error) {
	if len(property.Enum) == 0 {
		return value, nil
	}

	for _, candidate := range property.Enum {
		if strings.EqualFold(candidate, value) {
			return value, nil
		}
	}

	validValues := strings.Join(property.Enum, ", ")
	return nil, &clierrors.ArgumentError{
		Message:      fmt.Sprintf("Invalid value for %s: %s (valid values: %s)", option, value, validValues),
		Option:       option,
		Received:     value,
		ExpectedType: validValues,
		NextActions:  []string{fmt.Sprintf("Pass one of: %s for `%s`.", validValues, option)},
	}
}

func convertBooleanValue(value string, option string) (bool, error) {
	switch strings.ToLower(value) {
	case "true":
		return true, nil
	case "false":
		return false, nil
	default:
		return false, clierrors.InvalidValueArgumentError(option, value, "boolean")
	}
}

func convertArrayValue(value string, option string) (any, error) {
	if strings.HasPrefix(value, "[") {
		var parsed []any
		if err := json.Unmarshal([]byte(value), &parsed); err != nil {
			return nil, clierrors.InvalidValueArgumentError(option, value, "array")
		}
		return parsed, nil
	}

	parts := strings.Split(value, ",")
	result := make([]string, 0, len(parts))
	for _, part := range parts {
		result = append(result, strings.TrimSpace(part))
	}
	return result, nil
}

func convertObjectValue(value string, option string) (map[string]any, error) {
	var parsed map[string]any
	if err := json.Unmarshal([]byte(value), &parsed); err != nil {
		return nil, clierrors.InvalidValueArgumentError(option, value, "object")
	}
	if parsed == nil {
		return nil, clierrors.InvalidValueArgumentError(option, value, "object")
	}
	return parsed, nil
}

func booleanValueArgumentError(option string, received string) *clierrors.ArgumentError {
	return &clierrors.ArgumentError{
		Message:      "Boolean option does not accept a value: " + received,
		Option:       option,
		Received:     received,
		ExpectedType: "flag",
		NextActions:  []string{"Use `" + option + "` without a value."},
	}
}
