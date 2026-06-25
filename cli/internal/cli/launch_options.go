package cli

import (
	"strconv"
	"strings"
)

func applyLaunchOption(options *launchOptions, args []string, index int) (int, error) {
	arg := args[index]
	switch {
	case arg == "-r" || arg == "--restart":
		options.restart = true
		return index, nil
	case arg == "-q" || arg == "--quit":
		options.quit = true
		return index, nil
	case arg == "-d" || arg == "--delete-recovery":
		options.deleteRecovery = true
		return index, nil
	case isUnsupportedLaunchHubOption(arg):
		return index, unsupportedLaunchHubOptionError(arg)
	case arg == "-p" || arg == "--platform" || strings.HasPrefix(arg, "--platform="):
		return applyLaunchPlatformOption(options, args, index)
	case arg == "--max-depth" || strings.HasPrefix(arg, "--max-depth="):
		return applyLaunchMaxDepthOption(options, args, index)
	case strings.HasPrefix(arg, "-"):
		return index, unknownLaunchOptionError(arg)
	default:
		return applyLaunchProjectPathArgument(options, arg, index)
	}
}

func isUnsupportedLaunchHubOption(arg string) bool {
	return arg == "-a" ||
		arg == "-f" ||
		arg == "--add-unity-hub" ||
		arg == "--favorite" ||
		arg == "--unity-hub-entry"
}

func unsupportedLaunchHubOptionError(arg string) error {
	return &argumentError{
		message:     "Native launch does not support Unity Hub registration options.",
		option:      arg,
		command:     launchCommandName,
		nextActions: []string{"Remove the Unity Hub registration option and retry `uloop launch`."},
	}
}

func unknownLaunchOptionError(arg string) error {
	return &argumentError{
		message:     "Unknown launch option: " + arg,
		option:      arg,
		command:     launchCommandName,
		nextActions: []string{"Run `uloop launch --help` to inspect supported launch options."},
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

func applyLaunchMaxDepthOption(options *launchOptions, args []string, index int) (int, error) {
	value, consumed, err := readLaunchOptionValue(args[index], args, index)
	if err != nil {
		return index, err
	}
	maxDepth, err := strconv.Atoi(value)
	if err != nil {
		return index, invalidValueArgumentError("--max-depth", value, "integer")
	}
	options.maxDepth = maxDepth
	return nextLaunchOptionIndex(index, consumed), nil
}

func applyLaunchProjectPathArgument(options *launchOptions, arg string, index int) (int, error) {
	if options.projectPath != "" {
		return index, &argumentError{
			message:     "Unexpected extra launch argument: " + arg,
			received:    arg,
			command:     launchCommandName,
			nextActions: []string{"Pass only one project path to `uloop launch`."},
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
			return "", false, missingValueArgumentError(parts[0])
		}
		return parts[1], false, nil
	}
	if index+1 >= len(args) || isInvalidLaunchOptionValue(option, args[index+1]) {
		return "", false, missingValueArgumentError(option)
	}
	return args[index+1], true, nil
}

func isInvalidLaunchOptionValue(option string, value string) bool {
	if option == "--max-depth" {
		return isNextOptionToken(value)
	}
	return strings.HasPrefix(value, "-")
}
