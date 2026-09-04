package projectrunner

import (
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

// pausePointClearCommandName keeps clear-pause-point as the schema-driven Unity tool it already
// is. --file/--line are CLI-only sugar converted to Id before schema parsing, the same way
// extractDynamicCodeFileFlag converts --code-file to Code.
const pausePointClearCommandName = "clear-pause-point"

const (
	pausePointClearIdPropertyName = "Id"
	pausePointAllFlagName         = "all"
)

// extractPausePointClearFileLineFlags pulls --file/--line out of clear-pause-point args before
// generic tool parsing, because those flags are CLI-side sugar and not part of the Unity schema.
func extractPausePointClearFileLineFlags(command string, args []string) ([]string, string, error) {
	if command != pausePointClearCommandName {
		return args, "", nil
	}

	remaining, target, err := collectPausePointClearFileLineTarget(args)
	if err != nil {
		return nil, "", err
	}
	id, idProvided, hasAll, inspectErr := inspectPausePointClearSchemaFlags(remaining)
	if inspectErr != nil {
		return nil, "", inspectErr
	}
	if hasAll && (target.hasFile || target.hasLine) {
		return nil, "", &clierrors.ArgumentError{
			Message: "--all cannot be combined with --file or --line.",
			Option:  "--" + pausePointAllFlagName,
			Command: command,
		}
	}
	if !target.hasFile && !target.hasLine {
		return remaining, "", nil
	}

	queryID, resolveErr := resolvePausePointQueryID(id, idProvided, target, command)
	if resolveErr != nil {
		return nil, "", resolveErr
	}
	return remaining, queryID, nil
}

// applyPausePointClearFileLineID writes the composed marker id into Id so Unity sees the same
// parameter it already accepts for a named marker.
func applyPausePointClearFileLineID(params map[string]any, queryID string) error {
	if queryID == "" {
		return nil
	}
	if _, exists := params[pausePointClearIdPropertyName]; exists {
		return &clierrors.ArgumentError{
			Message: "--id cannot be combined with --file or --line.",
			Option:  "--" + PausePointIDFlagName,
			Command: pausePointClearCommandName,
		}
	}
	params[pausePointClearIdPropertyName] = queryID
	return nil
}

func collectPausePointClearFileLineTarget(args []string) ([]string, pausePointQueryTarget, error) {
	remaining := make([]string, 0, len(args))
	target := pausePointQueryTarget{}
	for index := 0; index < len(args); index++ {
		arg := args[index]
		if !isPausePointFileOrLineArg(arg) {
			remaining = append(remaining, arg)
			continue
		}

		name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
		if err != nil {
			return nil, pausePointQueryTarget{}, err
		}
		if assignErr := assignPausePointClearFileLineTarget(&target, name, value); assignErr != nil {
			return nil, pausePointQueryTarget{}, assignErr
		}
		if consumedNext {
			index++
		}
	}
	return remaining, target, nil
}

func assignPausePointClearFileLineTarget(target *pausePointQueryTarget, name string, value string) error {
	switch name {
	case PausePointFileFlagName:
		target.file = value
		target.hasFile = true
		return nil
	case PausePointLineFlagName:
		return setPausePointQueryTargetLine(target, value)
	default:
		return nil
	}
}

func inspectPausePointClearSchemaFlags(args []string) (string, bool, bool, error) {
	id := ""
	idProvided := false
	hasAll := false
	for index := 0; index < len(args); index++ {
		arg := args[index]
		if isExactOrEqualsFlag(arg, pausePointAllFlagName) {
			hasAll = true
			continue
		}
		if !isExactOrEqualsFlag(arg, PausePointIDFlagName) {
			continue
		}

		name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
		if err != nil {
			return "", false, false, err
		}
		if name != PausePointIDFlagName {
			continue
		}
		id = value
		idProvided = true
		if consumedNext {
			index++
		}
	}
	return id, idProvided, hasAll, nil
}

func isPausePointFileOrLineArg(arg string) bool {
	return isExactOrEqualsFlag(arg, PausePointFileFlagName) || isExactOrEqualsFlag(arg, PausePointLineFlagName)
}

func isExactOrEqualsFlag(arg string, flagName string) bool {
	option := "--" + flagName
	return arg == option || strings.HasPrefix(arg, option+"=")
}
