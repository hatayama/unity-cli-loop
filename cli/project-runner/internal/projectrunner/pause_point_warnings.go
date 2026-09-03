package projectrunner

import (
	"slices"
	"strings"
)

// pausePointEnableTimeWarningPrefix marks an enable-time patch diagnostic listed next to hit-time
// warnings. Without it, text such as "may not hit on pre-existing GameObjects" reads as a
// contradiction of the Hit it is reported with.
const pausePointEnableTimeWarningPrefix = "At enable time: "

// applyPausePointWarnings makes Warnings the single aggregate for a pause-point response and
// Warning its joined form, so no caller can read a Warning whose topics are missing from Warnings.
// cliWarnings are the CLI-side additions, appended after whatever Unity already reported.
func applyPausePointWarnings(
	response pausePointStatusResponse,
	cliWarnings ...string,
) pausePointStatusResponse {
	warnings := []string{}
	for _, warning := range pausePointUnityWarningEntries(response) {
		warnings = appendPausePointWarningEntry(warnings, warning)
	}
	for _, warning := range cliWarnings {
		warnings = appendPausePointWarningEntry(warnings, warning)
	}

	if len(warnings) == 0 {
		response.Warning = ""
		response.Warnings = nil
		return response
	}

	response.Warning = joinPausePointWarnings(warnings...)
	response.Warnings = warnings
	return response
}

// pausePointUnityWarningEntries reads Unity's own warnings as a list. A package generation that
// sends only the joined Warning string still contributes its text as one entry rather than
// dropping out of the aggregate.
func pausePointUnityWarningEntries(response pausePointStatusResponse) []string {
	if len(response.Warnings) > 0 {
		return response.Warnings
	}
	if response.Warning == "" {
		return nil
	}
	return []string{response.Warning}
}

// pausePointEnableTimeWarningEntries prefixes the enable response's warnings so they can join a
// hit response's Warnings without their "may not hit" wording contradicting the hit.
func pausePointEnableTimeWarningEntries(fields enablePausePointPropagatedFields) []string {
	source := fields.Warnings
	if len(source) == 0 {
		if fields.Warning == "" {
			return nil
		}
		source = []string{fields.Warning}
	}

	prefixed := make([]string, 0, len(source))
	for _, warning := range source {
		if warning == "" {
			continue
		}
		prefixed = append(prefixed, pausePointEnableTimeWarningPrefix+warning)
	}
	return prefixed
}

// joinPausePointWarnings concatenates warnings for one response, dropping empty ones and repeats.
// Warning is only ever this join of Warnings, never a channel of its own.
func joinPausePointWarnings(warnings ...string) string {
	unique := make([]string, 0, len(warnings))
	for _, warning := range warnings {
		if warning == "" || slices.Contains(unique, warning) {
			continue
		}
		unique = append(unique, warning)
	}
	return strings.Join(unique, " ")
}

func appendPausePointWarningEntry(existing []string, warning string) []string {
	if warning == "" || slices.Contains(existing, warning) {
		return existing
	}
	return append(existing, warning)
}
