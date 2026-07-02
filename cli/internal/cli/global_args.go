package cli

import (
	"strconv"
	"strings"
)

func parseGlobalProjectPath(args []string) ([]string, string, error) {
	remaining := make([]string, 0, len(args))
	projectPath := ""

	for index := 0; index < len(args); index++ {
		arg := args[index]
		if arg != "--"+projectPathFlagName && !strings.HasPrefix(arg, "--"+projectPathFlagName+"=") {
			remaining = append(remaining, arg)
			continue
		}

		name, value, consumedNext, err := parseFlagValue(arg, args, index)
		if err != nil {
			return nil, "", err
		}
		if name != projectPathFlagName {
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

func parseFlagValue(arg string, args []string, index int) (string, string, bool, error) {
	trimmed := strings.TrimPrefix(arg, "--")
	if trimmed == "" {
		return "", "", false, &argumentError{
			message:     "Invalid option: " + arg,
			option:      arg,
			nextActions: []string{"Use `--option value` or `--option=value`."},
		}
	}

	if strings.Contains(trimmed, "=") {
		parts := strings.SplitN(trimmed, "=", 2)
		if parts[1] == "" {
			return "", "", false, missingValueArgumentError("--" + parts[0])
		}
		return parts[0], parts[1], false, nil
	}

	if index+1 >= len(args) || isNextOptionToken(args[index+1]) {
		return "", "", false, missingValueArgumentError("--" + trimmed)
	}

	return trimmed, args[index+1], true, nil
}

func isNextOptionToken(value string) bool {
	if !strings.HasPrefix(value, "-") {
		return false
	}
	if _, err := strconv.ParseFloat(value, 64); err == nil {
		return false
	}
	return true
}

func isUnknownLeadingOption(command string) bool {
	return strings.HasPrefix(command, "-")
}

func isVersionRequest(args []string) bool {
	return len(args) == 1 && (args[0] == "--version" || args[0] == "-v")
}

func isVersionJSONRequest(args []string) bool {
	return len(args) == 2 && (args[0] == "--version" || args[0] == "-v") && args[1] == "--json"
}

func isHelpRequest(args []string) bool {
	return len(args) == 1 && (args[0] == "--help" || args[0] == "-h")
}

func containsHelpRequest(args []string) bool {
	for _, arg := range args {
		if arg == "--help" || arg == "-h" {
			return true
		}
	}
	return false
}

// shouldHandleCompletionRequest reports whether args target the native completion
// command or its list-commands/list-options helper flags, so run.go can route them
// before falling back to the project-connected Unity tool dispatch path.
func shouldHandleCompletionRequest(args []string) bool {
	if len(args) == 0 {
		return false
	}

	switch args[0] {
	case listCommandsFlag, listOptionsFlag, completionCommand:
		return true
	default:
		return false
	}
}
