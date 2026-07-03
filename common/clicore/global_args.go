package clicore

import (
	"strconv"
	"strings"
)

func ParseGlobalProjectPath(args []string) ([]string, string, error) {
	remaining := make([]string, 0, len(args))
	projectPath := ""

	for index := 0; index < len(args); index++ {
		arg := args[index]
		if arg != "--"+ProjectPathFlagName && !strings.HasPrefix(arg, "--"+ProjectPathFlagName+"=") {
			remaining = append(remaining, arg)
			continue
		}

		name, value, consumedNext, err := ParseFlagValue(arg, args, index)
		if err != nil {
			return nil, "", err
		}
		if name != ProjectPathFlagName {
			remaining = append(remaining, arg)
			continue
		}
		projectPath = value
		if consumedNext {
			index++
		}
	}

	return remaining, projectPath, nil
}

func ParseFlagValue(arg string, args []string, index int) (string, string, bool, error) {
	trimmed := strings.TrimPrefix(arg, "--")
	if trimmed == "" {
		return "", "", false, &ArgumentError{
			Message:     "Invalid option: " + arg,
			Option:      arg,
			NextActions: []string{"Use `--option value` or `--option=value`."},
		}
	}

	if strings.Contains(trimmed, "=") {
		parts := strings.SplitN(trimmed, "=", 2)
		if parts[1] == "" {
			return "", "", false, MissingValueArgumentError("--" + parts[0])
		}
		return parts[0], parts[1], false, nil
	}

	if index+1 >= len(args) || IsNextOptionToken(args[index+1]) {
		return "", "", false, MissingValueArgumentError("--" + trimmed)
	}

	return trimmed, args[index+1], true, nil
}

func IsNextOptionToken(value string) bool {
	if !strings.HasPrefix(value, "-") {
		return false
	}
	if _, err := strconv.ParseFloat(value, 64); err == nil {
		return false
	}
	return true
}

func IsUnknownLeadingOption(command string) bool {
	return strings.HasPrefix(command, "-")
}

func IsVersionRequest(args []string) bool {
	return len(args) == 1 && (args[0] == "--version" || args[0] == "-v")
}

func IsVersionJSONRequest(args []string) bool {
	return len(args) == 2 && (args[0] == "--version" || args[0] == "-v") && args[1] == "--json"
}

func IsHelpRequest(args []string) bool {
	return len(args) == 1 && (args[0] == "--help" || args[0] == "-h")
}

func ContainsHelpRequest(args []string) bool {
	for _, arg := range args {
		if arg == "--help" || arg == "-h" {
			return true
		}
	}
	return false
}

// ShouldHandleCompletionRequest reports whether args target the native completion
// command or its list-commands/list-options helper flags, so run.go can route them
// before falling back to the project-connected Unity tool dispatch path.
func ShouldHandleCompletionRequest(args []string) bool {
	if len(args) == 0 {
		return false
	}

	switch args[0] {
	case ListCommandsFlag, ListOptionsFlag, CompletionCommand:
		return true
	default:
		return false
	}
}
