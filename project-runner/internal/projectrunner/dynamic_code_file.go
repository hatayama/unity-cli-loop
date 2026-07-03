package projectrunner

import (
	"fmt"
	"os"
	"strings"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

const dynamicCodeCodePropertyName = "Code"

// extractDynamicCodeFileFlag pulls --code-file out of execute-dynamic-code args before
// generic tool parsing, because the flag is CLI-side sugar and not part of the Unity schema.
func extractDynamicCodeFileFlag(command string, args []string) ([]string, string, error) {
	if command != clicore.ExecuteDynamicCodeCommandName {
		return args, "", nil
	}

	remaining := make([]string, 0, len(args))
	path := ""
	for index := 0; index < len(args); index++ {
		arg := args[index]
		if arg != "--"+clicore.DynamicCodeFileFlagName && !strings.HasPrefix(arg, "--"+clicore.DynamicCodeFileFlagName+"=") {
			remaining = append(remaining, arg)
			continue
		}

		name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
		if err != nil {
			return nil, "", err
		}
		if name != clicore.DynamicCodeFileFlagName {
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
		return &clicore.ArgumentError{
			Message:     "--code and --code-file cannot be combined",
			Option:      "--" + clicore.DynamicCodeFileFlagName,
			Command:     clicore.ExecuteDynamicCodeCommandName,
			NextActions: []string{"Pass the C# source either inline with `--code` or from a file with `--code-file <path>`."},
		}
	}

	content, err := os.ReadFile(path)
	if err != nil {
		return fmt.Errorf("failed to read --%s %s: %w", clicore.DynamicCodeFileFlagName, path, err)
	}

	params[dynamicCodeCodePropertyName] = string(content)
	return nil
}
