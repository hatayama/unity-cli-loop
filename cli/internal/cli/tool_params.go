package cli

import (
	"encoding/json"
	"strconv"
	"strings"
)

func buildToolParams(args []string, tool toolDefinition) (map[string]any, string, error) {
	params := map[string]any{}
	projectPath := ""

	for index := 0; index < len(args); index++ {
		arg := args[index]
		if !strings.HasPrefix(arg, "--") {
			return nil, "", &argumentError{
				message:     "Unexpected argument: " + arg,
				received:    arg,
				command:     tool.Name,
				nextActions: []string{"Pass tool inputs as `--option value` pairs."},
			}
		}

		flag, err := parseToolFlag(arg)
		if err != nil {
			return nil, "", err
		}

		if flag.name == projectPathFlagName {
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

		propertyName, property, negated, ok := findProperty(tool, flag.name)
		if !ok {
			return nil, "", &argumentError{
				message:     "Unknown option for " + tool.Name + ": --" + flag.name,
				option:      "--" + flag.name,
				command:     tool.Name,
				nextActions: []string{"Run `uloop --list-options " + tool.Name + "` to inspect supported options."},
			}
		}

		option := "--" + flag.name
		if isBooleanProperty(property) {
			if flag.hasValue {
				return nil, "", booleanValueArgumentError(option, flag.value)
			}
			if index+1 < len(args) && !isNextOptionToken(args[index+1]) {
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
		return parsedToolFlag{}, &argumentError{
			message:     "Invalid option: " + arg,
			option:      arg,
			nextActions: []string{"Use `--option` for boolean flags or `--option value` for valued options."},
		}
	}

	if strings.Contains(trimmed, "=") {
		parts := strings.SplitN(trimmed, "=", 2)
		if parts[1] == "" {
			return parsedToolFlag{}, missingValueArgumentError("--" + parts[0])
		}
		return parsedToolFlag{name: parts[0], value: parts[1], hasValue: true}, nil
	}

	return parsedToolFlag{name: trimmed}, nil
}

func flagValue(flag parsedToolFlag, args []string, index int) (string, bool, error) {
	if flag.hasValue {
		return flag.value, false, nil
	}

	if index+1 >= len(args) || isNextOptionToken(args[index+1]) {
		return "", false, missingValueArgumentError("--" + flag.name)
	}

	return args[index+1], true, nil
}

func convertValue(value string, property toolProperty, option string) (any, error) {
	switch strings.ToLower(property.Type) {
	case "boolean":
		return convertBooleanValue(value, option)
	case "integer":
		parsed, err := strconv.Atoi(value)
		if err != nil {
			return nil, invalidValueArgumentError(option, value, "integer")
		}
		return parsed, nil
	case "number":
		parsed, err := strconv.ParseFloat(value, 64)
		if err != nil {
			return nil, invalidValueArgumentError(option, value, "number")
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
		return false, invalidValueArgumentError(option, value, "boolean")
	}
}

func convertArrayValue(value string, option string) (any, error) {
	if strings.HasPrefix(value, "[") {
		var parsed []any
		if err := json.Unmarshal([]byte(value), &parsed); err != nil {
			return nil, invalidValueArgumentError(option, value, "array")
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
		return nil, invalidValueArgumentError(option, value, "object")
	}
	if parsed == nil {
		return nil, invalidValueArgumentError(option, value, "object")
	}
	return parsed, nil
}

func booleanValueArgumentError(option string, received string) *argumentError {
	return &argumentError{
		message:      "Boolean option does not accept a value: " + received,
		option:       option,
		received:     received,
		expectedType: "flag",
		nextActions:  []string{"Use `" + option + "` without a value."},
	}
}
