package projectrunner

import "encoding/json"

// earlierCompileResultWarning tells the reader that a reattached result was compiled from the
// scripts as they were when the timed-out compile started.
const earlierCompileResultWarning = "This result comes from the earlier compile that timed out, so it does not include script changes saved after that compile started. If you changed scripts since then, run `uloop compile` again to compile the current scripts."

// addEarlierCompileResultWarning puts earlierCompileResultWarning at the front of the result's
// Warning. A result that is not a JSON object with a string (or absent/null) Warning is returned
// unchanged, so the command still reports what Unity answered.
func addEarlierCompileResultWarning(result json.RawMessage) json.RawMessage {
	fields := map[string]json.RawMessage{}
	if json.Unmarshal(result, &fields) != nil || fields == nil {
		return result
	}
	existing := ""
	if raw, ok := fields["Warning"]; ok && string(raw) != "null" {
		if json.Unmarshal(raw, &existing) != nil {
			return result
		}
	}
	text := earlierCompileResultWarning
	if existing != "" {
		text = text + " " + existing
	}
	encodedText, err := json.Marshal(text)
	if err != nil {
		return result
	}
	fields["Warning"] = encodedText

	encoded, ok := encodeCompileResultFields(fields)
	if !ok {
		return result
	}
	return encoded
}
