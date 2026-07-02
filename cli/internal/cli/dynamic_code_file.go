package cli

import (
	"fmt"
	"os"
	"strings"
)

const dynamicCodeCodePropertyName = "Code"

// extractDynamicCodeFileFlag pulls --code-file out of execute-dynamic-code args before
// generic tool parsing, because the flag is CLI-side sugar and not part of the Unity schema.
func extractDynamicCodeFileFlag(command string, args []string) ([]string, string, error) {
	if command != executeDynamicCodeCommandName {
		return args, "", nil
	}

	remaining := make([]string, 0, len(args))
	path := ""
	for index := 0; index < len(args); index++ {
		arg := args[index]
		if arg != "--"+dynamicCodeFileFlagName && !strings.HasPrefix(arg, "--"+dynamicCodeFileFlagName+"=") {
			remaining = append(remaining, arg)
			continue
		}

		name, value, consumedNext, err := parseFlagValue(arg, args, index)
		if err != nil {
			return nil, "", err
		}
		if name != dynamicCodeFileFlagName {
			remaining = append(remaining, arg)
			continue
		}
		path = value
		if consumedNext {
			index++
		}
	}

	return remaining, path, nil
}

// applyDynamicCodeFileParam loads the snippet file into the Code parameter, so long C#
// sources avoid shell quoting entirely.
func applyDynamicCodeFileParam(params map[string]any, path string) error {
	if path == "" {
		return nil
	}

	if _, exists := params[dynamicCodeCodePropertyName]; exists {
		return &argumentError{
			message:     "--code and --code-file cannot be combined",
			option:      "--" + dynamicCodeFileFlagName,
			command:     executeDynamicCodeCommandName,
			nextActions: []string{"Pass the C# source either inline with `--code` or from a file with `--code-file <path>`."},
		}
	}

	content, err := os.ReadFile(path)
	if err != nil {
		return fmt.Errorf("failed to read --%s %s: %w", dynamicCodeFileFlagName, path, err)
	}

	params[dynamicCodeCodePropertyName] = string(content)
	return nil
}
