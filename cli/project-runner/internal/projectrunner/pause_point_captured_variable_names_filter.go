package projectrunner

import "strings"

// parsePausePointCapturedVariableNames splits the comma-separated --captured-variable-names
// value into individual names, trimming surrounding whitespace and dropping empty entries.
func parsePausePointCapturedVariableNames(value string) []string {
	if value == "" {
		return nil
	}

	rawNames := strings.Split(value, ",")
	names := make([]string, 0, len(rawNames))
	for _, rawName := range rawNames {
		name := strings.TrimSpace(rawName)
		if name != "" {
			names = append(names, name)
		}
	}
	return names
}

// filterPausePointCapturedVariablesByName keeps only captured variables (including history
// frames) whose Name exactly matches one of names (case-sensitive). It runs before
// applyPausePointCapturedVariablesMode so the two flags compose: name filter narrows which
// variables appear, then --captured-variables controls how much of each survivor is emitted.
// When names is empty the response is returned unchanged.
func filterPausePointCapturedVariablesByName(
	response pausePointStatusResponse,
	names []string,
) pausePointStatusResponse {
	if len(names) == 0 {
		return response
	}

	nameSet := make(map[string]struct{}, len(names))
	for _, name := range names {
		nameSet[name] = struct{}{}
	}

	filteredCurrent, currentMatchCount := filterCapturedVariablesByNameSet(response.CapturedVariables, nameSet)
	response.CapturedVariables = filteredCurrent
	totalMatchCount := currentMatchCount

	history := make([]pausePointCapturedHistoryFrame, len(response.CapturedVariableHistory))
	for index, frame := range response.CapturedVariableHistory {
		filteredFrame, frameMatchCount := filterCapturedVariablesByNameSet(frame.CapturedVariables, nameSet)
		frame.CapturedVariables = filteredFrame
		totalMatchCount += frameMatchCount
		history[index] = frame
	}
	response.CapturedVariableHistory = history

	response.CapturedVariableNameFilterNoMatch = totalMatchCount == 0
	return response
}

func filterCapturedVariablesByNameSet(
	variables []pausePointCapturedVariable,
	nameSet map[string]struct{},
) ([]pausePointCapturedVariable, int) {
	filtered := make([]pausePointCapturedVariable, 0, len(variables))
	for _, variable := range variables {
		if _, ok := nameSet[variable.Name]; ok {
			filtered = append(filtered, variable)
		}
	}
	return filtered, len(filtered)
}
