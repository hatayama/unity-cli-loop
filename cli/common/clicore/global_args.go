package clicore

import (
	"strconv"
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
)

func ParseGlobalProjectPath(args []string) ([]string, string, error) {
	remaining := make([]string, 0, len(args))
	projectPath := ""

	for index := 0; index < len(args); index++ {
		arg := args[index]
		if arg != "--"+tooldocs.ProjectPathFlagName && !strings.HasPrefix(arg, "--"+tooldocs.ProjectPathFlagName+"=") {
			remaining = append(remaining, arg)
			continue
		}

		name, value, consumedNext, err := ParseFlagValue(arg, args, index)
		if err != nil {
			return nil, "", err
		}
		if name != tooldocs.ProjectPathFlagName {
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
		return "", "", false, &clierrors.ArgumentError{
			Message:     "Invalid option: " + arg,
			Option:      arg,
			NextActions: []string{"Use `--option value` or `--option=value`."},
		}
	}

	if strings.Contains(trimmed, "=") {
		parts := strings.SplitN(trimmed, "=", 2)
		if parts[1] == "" {
			return "", "", false, clierrors.MissingValueArgumentError("--" + parts[0])
		}
		return parts[0], parts[1], false, nil
	}

	if index+1 >= len(args) || IsNextOptionToken(args[index+1]) {
		return "", "", false, clierrors.MissingValueArgumentError("--" + trimmed)
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
