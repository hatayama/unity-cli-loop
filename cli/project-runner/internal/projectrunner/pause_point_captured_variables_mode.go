package projectrunner

import (
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/tooldocs"
)

// pausePointCapturedVariablesMode controls how much of each captured variable's data the CLI
// emits. "full" (default) returns every field; "names" drops Value so agents with wide,
// field-heavy classes can pull a lightweight list first and fetch specific values afterward
// (via TryGetCapturedValue or pause-point-status) instead of paying for every value up front.
type pausePointCapturedVariablesMode string

// Defined from the tooldocs values rather than repeating the literals: the same two strings are
// rendered as --captured-variables' accepted values in both option listings.
const (
	pausePointCapturedVariablesModeFull  pausePointCapturedVariablesMode = tooldocs.PausePointCapturedVariablesModeFull
	pausePointCapturedVariablesModeNames pausePointCapturedVariablesMode = tooldocs.PausePointCapturedVariablesModeNames
)

func parsePausePointCapturedVariablesMode(value string) (pausePointCapturedVariablesMode, error) {
	switch value {
	case "", string(pausePointCapturedVariablesModeFull):
		return pausePointCapturedVariablesModeFull, nil
	case string(pausePointCapturedVariablesModeNames):
		return pausePointCapturedVariablesModeNames, nil
	default:
		return "", clierrors.InvalidValueArgumentError(
			"--"+tooldocs.PausePointCapturedVariablesFlagName, value, "full or names")
	}
}

// applyPausePointCapturedVariablesMode strips Value from every captured variable (including
// history frames) when mode is "names", leaving Name/Scope/TypeName as the lightweight result.
func applyPausePointCapturedVariablesMode(
	response pausePointStatusResponse,
	mode pausePointCapturedVariablesMode,
) pausePointStatusResponse {
	if mode != pausePointCapturedVariablesModeNames {
		return response
	}

	response.CapturedVariables = stripPausePointCapturedVariableValues(response.CapturedVariables)

	history := make([]pausePointCapturedHistoryFrame, len(response.CapturedVariableHistory))
	for index, frame := range response.CapturedVariableHistory {
		frame.CapturedVariables = stripPausePointCapturedVariableValues(frame.CapturedVariables)
		history[index] = frame
	}
	response.CapturedVariableHistory = history

	return response
}

func stripPausePointCapturedVariableValues(variables []pausePointCapturedVariable) []pausePointCapturedVariable {
	stripped := make([]pausePointCapturedVariable, len(variables))
	for index, variable := range variables {
		variable.Value = nil
		stripped[index] = variable
	}
	return stripped
}
