package projectrunner

import (
	"fmt"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
)

type pausePointStatusOptions struct {
	id                    string
	idProvided            bool
	listMode              bool
	listModePerMarkerFlag string
	queryTarget           pausePointQueryTarget
	capturedVariablesMode pausePointCapturedVariablesMode
	capturedVariableNames []string
	expectations          []pausePointExpectation
}

func parsePausePointStatusOptions(args []string) (pausePointStatusOptions, error) {
	options := pausePointStatusOptions{capturedVariablesMode: pausePointCapturedVariablesModeFull}

	for index := 0; index < len(args); index++ {
		name, value, consumedNext, err := clicore.ParseFlagValue(args[index], args, index)
		if err != nil {
			return pausePointStatusOptions{}, err
		}

		if applyErr := applyPausePointStatusFlag(&options, name, value); applyErr != nil {
			return pausePointStatusOptions{}, applyErr
		}
		if consumedNext {
			index++
		}
	}

	if !options.idProvided && !options.queryTarget.hasFile && !options.queryTarget.hasLine {
		if options.listModePerMarkerFlag != "" {
			return pausePointStatusOptions{}, pausePointStatusListModeOptionError(options.listModePerMarkerFlag)
		}
		options.listMode = true
		return options, nil
	}

	queryID, targetErr := resolvePausePointQueryID(
		options.id,
		options.idProvided,
		options.queryTarget,
		clicore.PausePointStatusUserCommandName)
	if targetErr != nil {
		return pausePointStatusOptions{}, targetErr
	}
	options.id = queryID

	return options, nil
}

func applyPausePointStatusFlag(options *pausePointStatusOptions, name string, value string) error {
	switch name {
	case PausePointIDFlagName:
		options.id = value
		options.idProvided = true
	case PausePointFileFlagName:
		options.queryTarget.file = value
		options.queryTarget.hasFile = true
	case PausePointLineFlagName:
		return setPausePointQueryTargetLine(&options.queryTarget, value)
	case tooldocs.PausePointCapturedVariablesFlagName:
		return applyPausePointStatusCapturedVariablesFlag(options, name, value)
	case tooldocs.PausePointCapturedVariableNamesFlagName:
		options.capturedVariableNames = parsePausePointCapturedVariableNames(value)
		recordPausePointStatusListModePerMarkerFlag(options, name)
	case tooldocs.PausePointExpectFlagName:
		return applyPausePointStatusExpectFlag(options, name, value)
	default:
		return pausePointUnknownOptionError(clicore.PausePointStatusUserCommandName, name)
	}

	return nil
}

func applyPausePointStatusCapturedVariablesFlag(
	options *pausePointStatusOptions,
	name string,
	value string,
) error {
	mode, err := parsePausePointCapturedVariablesMode(value)
	if err != nil {
		return err
	}

	options.capturedVariablesMode = mode
	recordPausePointStatusListModePerMarkerFlag(options, name)
	return nil
}

func applyPausePointStatusExpectFlag(options *pausePointStatusOptions, name string, value string) error {
	expectation, err := parsePausePointExpectFlagValue(value)
	if err != nil {
		return err
	}

	options.expectations = append(options.expectations, expectation)
	recordPausePointStatusListModePerMarkerFlag(options, name)
	return nil
}

func recordPausePointStatusListModePerMarkerFlag(options *pausePointStatusOptions, flagName string) {
	if options.listModePerMarkerFlag == "" {
		options.listModePerMarkerFlag = flagName
	}
}

func pausePointStatusListModeOptionError(flagName string) *clierrors.ArgumentError {
	return &clierrors.ArgumentError{
		Message: fmt.Sprintf("--%s requires --id or --file with --line.", flagName),
		Option:  "--" + flagName,
		Command: clicore.PausePointStatusUserCommandName,
	}
}
