package projectrunner

import (
	"strconv"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
)

// applyWaitForPausePointFlag applies one named await-pause-point flag. Why a
// helper: the per-flag validation stays with the flag it belongs to, so the
// parser loop does not accumulate every branch into one cyclop finding.
func applyWaitForPausePointFlag(options *waitForPausePointOptions, name string, value string) error {
	switch name {
	case PausePointIDFlagName:
		options.id = value
		options.idProvided = true
		return nil
	case PausePointFileFlagName:
		options.queryTarget.file = value
		options.queryTarget.hasFile = true
		return nil
	case PausePointLineFlagName:
		return setPausePointQueryTargetLine(&options.queryTarget, value)

	case PausePointTimeoutFlagName:
		return applyWaitForPausePointTimeout(options, value)
	case PausePointLogsMaxCountFlagName:
		return applyWaitForPausePointLogsMaxCount(options, value)
	case tooldocs.PausePointCapturedVariablesFlagName:
		return applyWaitForPausePointCapturedVariables(options, value)
	case tooldocs.PausePointCapturedVariableNamesFlagName:
		options.capturedVariableNames = parsePausePointCapturedVariableNames(value)
		return nil
	case tooldocs.PausePointExpectFlagName:
		return applyWaitForPausePointExpect(options, value)
	case tooldocs.PausePointTriggerFlagName:
		return applyWaitForPausePointTrigger(options, value)
	case tooldocs.PausePointResumePlayFlagName:
		return applyWaitForPausePointResumePlayValue(options, value)
	default:
		return pausePointUnknownOptionError(clicore.PausePointAwaitCommandName, name)
	}
}

func applyWaitForPausePointTimeout(options *waitForPausePointOptions, value string) error {
	timeoutSeconds, parseErr := parsePausePointTimeoutSeconds(value)
	if parseErr != nil {
		return parseErr
	}
	options.timeoutSeconds = timeoutSeconds
	options.timeout = time.Duration(timeoutSeconds) * time.Second
	return nil
}

func applyWaitForPausePointLogsMaxCount(options *waitForPausePointOptions, value string) error {
	maxCount, parseErr := strconv.Atoi(value)
	if parseErr != nil || maxCount <= 0 {
		return clierrors.InvalidValueArgumentError(
			"--"+PausePointLogsMaxCountFlagName, value, "positive integer")
	}
	options.matchingLogsMaxCount = maxCount
	return nil
}

func applyWaitForPausePointCapturedVariables(options *waitForPausePointOptions, value string) error {
	mode, parseErr := parsePausePointCapturedVariablesMode(value)
	if parseErr != nil {
		return parseErr
	}
	options.capturedVariablesMode = mode
	return nil
}

func applyWaitForPausePointExpect(options *waitForPausePointOptions, value string) error {
	expectation, parseErr := parsePausePointExpectFlagValue(value)
	if parseErr != nil {
		return parseErr
	}
	options.expectations = append(options.expectations, expectation)
	return nil
}

func applyWaitForPausePointTrigger(options *waitForPausePointOptions, value string) error {
	triggerCommand, triggerArgs, parseErr := parsePausePointTriggerCommand(clicore.PausePointAwaitCommandName, value)
	if parseErr != nil {
		return parseErr
	}
	options.triggerCommand = triggerCommand
	options.triggerArgs = triggerArgs
	return nil
}

func applyWaitForPausePointResumePlayValue(options *waitForPausePointOptions, value string) error {
	// --resume-play=true style is accepted; any other value is rejected so a typo cannot
	// silently disable the resume step.
	if value != "true" && value != "1" {
		return clierrors.InvalidValueArgumentError(
			"--"+tooldocs.PausePointResumePlayFlagName, value, "boolean flag (pass with no value)")
	}
	options.resumePlay = true
	return nil
}
