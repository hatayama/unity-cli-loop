package projectrunner

import (
	"slices"
	"strings"
)

const (
	// pausePointCapturedVariablesTruncatedNote explains why CapturedVariablesTruncated
	// can stay true after --captured-variable-names dropped every clipped variable.
	pausePointCapturedVariablesTruncatedNote = "the truncation flag refers to a variable excluded by --captured-variable-names; every variable listed here is complete."
)

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

	// Why before filtering: pause-point-status runs this filter without a hit gate. An unhit
	// marker has empty CapturedVariables/history, which would otherwise look like a name miss
	// and a Warning blaming the requested names would misdiagnose "not hit yet".
	hadCapturedVariables := pausePointResponseHasCapturedVariables(response)
	hadListedTruncated := pausePointResponseHasTruncatedCapturedVariable(response)

	nameSet := make(map[string]struct{}, len(names))
	for _, name := range names {
		nameSet[name] = struct{}{}
	}

	matchedNames := map[string]struct{}{}

	filteredCurrent, currentMatchCount := filterCapturedVariablesByNameSet(response.CapturedVariables, nameSet)
	collectCapturedVariableNames(filteredCurrent, matchedNames)
	response.CapturedVariables = filteredCurrent
	totalMatchCount := currentMatchCount

	history := make([]pausePointCapturedHistoryFrame, len(response.CapturedVariableHistory))
	for index, frame := range response.CapturedVariableHistory {
		filteredFrame, frameMatchCount := filterCapturedVariablesByNameSet(frame.CapturedVariables, nameSet)
		collectCapturedVariableNames(filteredFrame, matchedNames)
		frame.CapturedVariables = filteredFrame
		totalMatchCount += frameMatchCount
		history[index] = frame
	}
	response.CapturedVariableHistory = history

	response.CapturedVariableNameFilterNoMatch = totalMatchCount == 0
	response.CapturedVariableNamesNotFound = unmatchedCapturedVariableNames(names, matchedNames)
	// Why Warning only when hadCapturedVariables: machine-readable flags still fire on empty
	// snapshots (unchanged), but a human Warning must not claim a name miss when the hit has
	// not produced any variables yet.
	if hadCapturedVariables && response.CapturedVariableNameFilterNoMatch {
		response.Warning = joinPausePointWarnings(
			response.Warning,
			"No captured variable matched the requested names; the hit captured other variables. Check CapturedVariableNamesNotFound for the names that were absent.")
	}

	return applyPausePointCapturedVariablesTruncatedNote(response, hadListedTruncated)
}

// pausePointResponseHasCapturedVariables reports whether the snapshot already holds any
// captured variable (current or history) before a name filter runs.
func pausePointResponseHasCapturedVariables(response pausePointStatusResponse) bool {
	if len(response.CapturedVariables) > 0 {
		return true
	}
	for _, frame := range response.CapturedVariableHistory {
		if len(frame.CapturedVariables) > 0 {
			return true
		}
	}
	return false
}

// unmatchedCapturedVariableNames lists the requested names that matched nothing, keeping the order
// they were requested in so the report reads back against the flag value the caller wrote. A name
// matched anywhere — current variables or any history frame — counts as found. A name requested
// twice is reported once: the list answers "which names have no value", not "how many times each
// was asked for".
func unmatchedCapturedVariableNames(names []string, matchedNames map[string]struct{}) []string {
	notFound := make([]string, 0, len(names))
	for _, name := range names {
		if _, ok := matchedNames[name]; ok {
			continue
		}
		if slices.Contains(notFound, name) {
			continue
		}
		notFound = append(notFound, name)
	}
	if len(notFound) == 0 {
		return nil
	}
	return notFound
}

func collectCapturedVariableNames(variables []pausePointCapturedVariable, names map[string]struct{}) {
	for _, variable := range variables {
		names[variable.Name] = struct{}{}
	}
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

// applyPausePointCapturedVariablesTruncatedNote records that the Unity truncation
// flag is about a variable the name filter excluded. The flag itself is left
// unchanged: clearing it would hide that a captured value was clipped.
// Count is no longer a preview-clip signal: Unity now counts clipped previews
// in TruncatedVariableCount, so a non-zero count must not suppress the note.
func applyPausePointCapturedVariablesTruncatedNote(
	response pausePointStatusResponse,
	hadListedTruncatedBeforeFilter bool,
) pausePointStatusResponse {
	if !response.CapturedVariablesTruncated {
		return response
	}
	if pausePointResponseHasTruncatedCapturedVariable(response) {
		return response
	}
	if !hadListedTruncatedBeforeFilter {
		return response
	}

	response.CapturedVariablesTruncatedNote = pausePointCapturedVariablesTruncatedNote
	return response
}

// pausePointResponseHasTruncatedCapturedVariable reports whether any remaining
// captured variable (current or history) still has Truncated set.
func pausePointResponseHasTruncatedCapturedVariable(response pausePointStatusResponse) bool {
	for _, variable := range response.CapturedVariables {
		if variable.Truncated {
			return true
		}
	}
	for _, frame := range response.CapturedVariableHistory {
		for _, variable := range frame.CapturedVariables {
			if variable.Truncated {
				return true
			}
		}
	}
	return false
}
