package dispatcher

import (
	"strconv"
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

func applyLaunchOption(options *launchOptions, args []string, index int) (int, error) {
	arg := args[index]
	if next, handled, err := applyLaunchBooleanFlag(options, arg, index); handled {
		return next, err
	}
	if next, handled, err := applyLaunchKeyedOption(options, args, index); handled {
		return next, err
	}
	if isUnsupportedLaunchHubOption(arg) {
		return index, unsupportedLaunchHubOptionError(arg)
	}
	if strings.HasPrefix(arg, "-") {
		return index, unknownLaunchOptionError(arg)
	}
	return applyLaunchProjectPathArgument(options, arg, index)
}

// applyLaunchBooleanFlag handles restart/quit/delete-recovery. Why a separate
// helper: those flags share "set a bool and keep the current index" behavior,
// and leaving them in applyLaunchOption kept the switch above the cyclop limit.
func applyLaunchBooleanFlag(options *launchOptions, arg string, index int) (int, bool, error) {
	switch arg {
	case "-r", "--restart":
		options.restart = true
		return index, true, nil
	case "-q", "--quit":
		options.quit = true
		return index, true, nil
	case "-d", "--delete-recovery":
		options.deleteRecovery = true
		return index, true, nil
	default:
		return index, false, nil
	}
}

// applyLaunchKeyedOption handles value-taking launch flags. Why not fold these
// into applyLaunchBooleanFlag: equals-form and next-token consumption are
// unique to keyed options.
func applyLaunchKeyedOption(options *launchOptions, args []string, index int) (int, bool, error) {
	arg := args[index]
	switch {
	case arg == "--editor-version" || strings.HasPrefix(arg, "--editor-version="):
		next, err := applyLaunchEditorVersionOption(options, args, index)
		return next, true, err
	case arg == "-p" || arg == "--platform" || strings.HasPrefix(arg, "--platform="):
		next, err := applyLaunchPlatformOption(options, args, index)
		return next, true, err
	case arg == "--max-depth" || strings.HasPrefix(arg, "--max-depth="):
		next, err := applyLaunchMaxDepthOption(options, args, index)
		return next, true, err
	default:
		return index, false, nil
	}
}

func isUnsupportedLaunchHubOption(arg string) bool {
	return arg == "-a" ||
		arg == "-f" ||
		isUnsupportedLaunchHubLongOption(arg, "--add-unity-hub") ||
		isUnsupportedLaunchHubLongOption(arg, "--favorite") ||
		isUnsupportedLaunchHubLongOption(arg, "--unity-hub-entry")
}

func isUnsupportedLaunchHubLongOption(arg string, option string) bool {
	return arg == option || strings.HasPrefix(arg, option+"=")
}

func unsupportedLaunchHubOptionError(arg string) error {
	return &clierrors.ArgumentError{
		Message:     "Native launch does not support Unity Hub registration options.",
		Option:      arg,
		Command:     clicore.LaunchCommandName,
		NextActions: []string{"Remove the Unity Hub registration option and retry `uloop launch`."},
	}
}

func unknownLaunchOptionError(arg string) error {
	return &clierrors.ArgumentError{
		Message:     "Unknown launch option: " + arg,
		Option:      arg,
		Command:     clicore.LaunchCommandName,
		NextActions: []string{"Run `uloop launch --help` to inspect supported launch options."},
	}
}

func applyLaunchPlatformOption(options *launchOptions, args []string, index int) (int, error) {
	value, consumed, err := readLaunchOptionValue(args[index], args, index)
	if err != nil {
		return index, err
	}
	options.platform = value
	return nextLaunchOptionIndex(index, consumed), nil
}

func applyLaunchEditorVersionOption(options *launchOptions, args []string, index int) (int, error) {
	value, consumed, err := readLaunchOptionValue(args[index], args, index)
	if err != nil {
		return index, err
	}
	options.editorVersion = value
	return nextLaunchOptionIndex(index, consumed), nil
}

func applyLaunchMaxDepthOption(options *launchOptions, args []string, index int) (int, error) {
	value, consumed, err := readLaunchOptionValue(args[index], args, index)
	if err != nil {
		return index, err
	}
	maxDepth, err := strconv.Atoi(value)
	if err != nil || maxDepth < -1 {
		return index, clierrors.InvalidValueArgumentError("--max-depth", value, "integer >= -1")
	}
	options.maxDepth = maxDepth
	return nextLaunchOptionIndex(index, consumed), nil
}

func applyLaunchProjectPathArgument(options *launchOptions, arg string, index int) (int, error) {
	if options.projectPath != "" {
		return index, &clierrors.ArgumentError{
			Message:     "Unexpected extra launch argument: " + arg,
			Received:    arg,
			Command:     clicore.LaunchCommandName,
			NextActions: []string{"Pass only one project path to `uloop launch`."},
		}
	}
	options.projectPath = arg
	return index, nil
}

func nextLaunchOptionIndex(index int, consumed bool) int {
	if consumed {
		return index + 1
	}
	return index
}

func readLaunchOptionValue(option string, args []string, index int) (string, bool, error) {
	if strings.Contains(option, "=") {
		parts := strings.SplitN(option, "=", 2)
		if parts[1] == "" {
			return "", false, clierrors.MissingValueArgumentError(parts[0])
		}
		return parts[1], false, nil
	}
	if index+1 >= len(args) || isInvalidLaunchOptionValue(option, args[index+1]) {
		return "", false, clierrors.MissingValueArgumentError(option)
	}
	return args[index+1], true, nil
}

func isInvalidLaunchOptionValue(option string, value string) bool {
	if option == "--max-depth" {
		return clicore.IsNextOptionToken(value)
	}
	return strings.HasPrefix(value, "-")
}
