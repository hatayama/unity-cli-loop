package projectrunner

import (
	"encoding/json"
	"strconv"
	"strings"

	"github.com/hatayama/unity-cli-loop/cli/internal/clicore"
)

func buildToolParams(args []string, tool clicore.ToolDefinition) (map[string]any, string, error) {
	params := map[string]any{}
	projectPath := ""

	for index := 0; index < len(args); index++ {
		arg := args[index]
		if !strings.HasPrefix(arg, "--") {
			return nil, "", &clicore.ArgumentError{
				Message:     "Unexpected argument: " + arg,
				Received:    arg,
				Command:     tool.Name,
				NextActions: []string{"Pass tool inputs as `--option value` pairs."},
			}
		}

		flag, err := parseToolFlag(arg)
		if err != nil {
			return nil, "", err
		}

		if flag.name == clicore.ProjectPathFlagName {
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

		propertyName, property, negated, ok := clicore.FindProperty(tool, flag.name)
		if !ok {
			return nil, "", &clicore.ArgumentError{
				Message:     "Unknown option for " + tool.Name + ": --" + flag.name,
				Option:      "--" + flag.name,
				Command:     tool.Name,
				NextActions: []string{"Run `uloop --list-options " + tool.Name + "` to inspect supported options."},
			}
		}

		option := "--" + flag.name
		if clicore.IsBooleanProperty(property) {
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
		return parsedToolFlag{}, &clicore.ArgumentError{
			Message:     "Invalid option: " + arg,
			Option:      arg,
			NextActions: []string{"Use `--option` for boolean flags or `--option value` for valued options."},
		}
	}

	if strings.Contains(trimmed, "=") {
		parts := strings.SplitN(trimmed, "=", 2)
		if parts[1] == "" {
			return parsedToolFlag{}, clicore.MissingValueArgumentError("--" + parts[0])
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
		return "", false, clicore.MissingValueArgumentError("--" + flag.name)
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
			return nil, clicore.InvalidValueArgumentError(option, value, "integer")
		}
		return parsed, nil
	case "number":
		parsed, err := strconv.ParseFloat(value, 64)
		if err != nil {
			return nil, clicore.InvalidValueArgumentError(option, value, "number")
		}
		return parsed, nil
	case "array":
		return convertArrayValue(value, option)
	case "object":
		return convertObjectValue(value, option)
	default:
		return value, nil
	}
}

func convertBooleanValue(value string, option string) (bool, error) {
	switch strings.ToLower(value) {
	case "true":
		return true, nil
	case "false":
		return false, nil
	default:
		return false, clicore.InvalidValueArgumentError(option, value, "boolean")
	}
}

func convertArrayValue(value string, option string) (any, error) {
	if strings.HasPrefix(value, "[") {
		var parsed []any
		if err := json.Unmarshal([]byte(value), &parsed); err != nil {
			return nil, clicore.InvalidValueArgumentError(option, value, "array")
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
		return nil, clicore.InvalidValueArgumentError(option, value, "object")
	}
	if parsed == nil {
		return nil, clicore.InvalidValueArgumentError(option, value, "object")
	}
	return parsed, nil
}

func booleanValueArgumentError(option string, received string) *clicore.ArgumentError {
	return &clicore.ArgumentError{
		Message:      "Boolean option does not accept a value: " + received,
		Option:       option,
		Received:     received,
		ExpectedType: "flag",
		NextActions:  []string{"Use `" + option + "` without a value."},
	}
}
