package projectrunner

import (
	"encoding/json"
	"strings"
	"testing"
)

// capturedVariableNamesFilterResponse is the fixture both --captured-variable-names test functions
// filter: three current variables, two of which also appear in one history frame.
func capturedVariableNamesFilterResponse() pausePointStatusResponse {
	return pausePointStatusResponse{
		CapturedVariables: []pausePointCapturedVariable{
			{Name: "velocity", Scope: "Local", TypeName: "Vector3", Value: pausePointVariableValue("(1,0,0)")},
			{Name: "this", Scope: "This", TypeName: "PlayerController", Value: pausePointVariableValue("PlayerController")},
			{Name: "health", Scope: "Local", TypeName: "Int32", Value: pausePointVariableValue("100")},
		},
		CapturedVariableHistory: []pausePointCapturedHistoryFrame{
			{
				HitSequence: 1,
				CapturedVariables: []pausePointCapturedVariable{
					{Name: "velocity", Scope: "Local", TypeName: "Vector3", Value: pausePointVariableValue("(0,0,0)")},
					{Name: "health", Scope: "Local", TypeName: "Int32", Value: pausePointVariableValue("100")},
				},
			},
		},
	}
}

// TestFilterPausePointCapturedVariablesByName verifies the --captured-variable-names filter:
// single-name selection, multi-name selection, a name with no match, and that it composes with
// the --captured-variables mode (filter narrows first, then mode strips values).
func TestFilterPausePointCapturedVariablesByName(t *testing.T) {
	t.Run("single name keeps only the matching variable", func(t *testing.T) {
		assertFilterKeepsSingleCapturedVariableName(t)
	})
	t.Run("multiple names keep all matching variables in order", func(t *testing.T) {
		assertFilterKeepsCapturedVariableNamesInOrder(t)
	})
	t.Run("no matching name empties the arrays and sets the no-match flag", func(t *testing.T) {
		assertFilterReportsNoMatchingCapturedVariableName(t)
	})
	t.Run("empty pre-filter snapshot keeps no-match flag but skips Warning", func(t *testing.T) {
		assertFilterSkipsWarningOnEmptyPreFilterSnapshot(t)
	})
	t.Run("composes with captured-variables names mode: filter first, then strip values", func(t *testing.T) {
		assertFilterComposesWithCapturedVariablesNamesMode(t)
	})
	t.Run("empty names list leaves the response unchanged", func(t *testing.T) {
		assertFilterLeavesResponseUnchangedWhenNamesEmpty(t)
	})
}

func assertFilterKeepsSingleCapturedVariableName(t *testing.T) {
	t.Helper()
	result := filterPausePointCapturedVariablesByName(capturedVariableNamesFilterResponse(), []string{"velocity"})
	if len(result.CapturedVariables) != 1 || result.CapturedVariables[0].Name != "velocity" {
		t.Fatalf("expected only velocity to survive: %#v", result.CapturedVariables)
	}
	if len(result.CapturedVariableHistory[0].CapturedVariables) != 1 {
		t.Fatalf("expected history frame filtered to 1 entry: %#v", result.CapturedVariableHistory)
	}
	if result.CapturedVariableNameFilterNoMatch {
		t.Fatal("expected CapturedVariableNameFilterNoMatch to be false when a match exists")
	}
}

func assertFilterKeepsCapturedVariableNamesInOrder(t *testing.T) {
	t.Helper()
	result := filterPausePointCapturedVariablesByName(capturedVariableNamesFilterResponse(), []string{"velocity", "this"})
	if len(result.CapturedVariables) != 2 {
		t.Fatalf("expected velocity and this to survive: %#v", result.CapturedVariables)
	}
	if result.CapturedVariables[0].Name != "velocity" || result.CapturedVariables[1].Name != "this" {
		t.Fatalf("expected original order preserved: %#v", result.CapturedVariables)
	}
}

func assertFilterReportsNoMatchingCapturedVariableName(t *testing.T) {
	t.Helper()
	result := filterPausePointCapturedVariablesByName(capturedVariableNamesFilterResponse(), []string{"doesNotExist"})
	if len(result.CapturedVariables) != 0 {
		t.Fatalf("expected no captured variables to survive: %#v", result.CapturedVariables)
	}
	if len(result.CapturedVariableHistory[0].CapturedVariables) != 0 {
		t.Fatalf("expected history frame emptied: %#v", result.CapturedVariableHistory)
	}
	if !result.CapturedVariableNameFilterNoMatch {
		t.Fatal("expected CapturedVariableNameFilterNoMatch to be true when nothing matches")
	}
	const wantWarning = "No captured variable matched the requested names; the hit captured other variables. Check CapturedVariableNamesNotFound for the names that were absent."
	if result.Warning != wantWarning {
		t.Fatalf("expected human-readable Warning for no-match filter: %q", result.Warning)
	}
}

func assertFilterSkipsWarningOnEmptyPreFilterSnapshot(t *testing.T) {
	t.Helper()
	// Verifies an unhit status (no variables yet) does not blame the requested names.
	result := filterPausePointCapturedVariablesByName(pausePointStatusResponse{}, []string{"velocity"})
	if !result.CapturedVariableNameFilterNoMatch {
		t.Fatal("expected CapturedVariableNameFilterNoMatch to stay true on an empty snapshot")
	}
	if result.Warning != "" {
		t.Fatalf("expected no Warning when the snapshot had no variables before filtering: %q", result.Warning)
	}
}

func assertFilterComposesWithCapturedVariablesNamesMode(t *testing.T) {
	t.Helper()
	filtered := filterPausePointCapturedVariablesByName(capturedVariableNamesFilterResponse(), []string{"velocity"})
	result := applyPausePointCapturedVariablesMode(filtered, pausePointCapturedVariablesModeNames)
	if len(result.CapturedVariables) != 1 || result.CapturedVariables[0].Name != "velocity" {
		t.Fatalf("expected only velocity to survive the filter: %#v", result.CapturedVariables)
	}
	if result.CapturedVariables[0].Value != nil {
		t.Fatalf("expected Value stripped by names mode: %#v", result.CapturedVariables[0])
	}
}

func assertFilterLeavesResponseUnchangedWhenNamesEmpty(t *testing.T) {
	t.Helper()
	original := capturedVariableNamesFilterResponse()
	result := filterPausePointCapturedVariablesByName(original, nil)
	if len(result.CapturedVariables) != len(original.CapturedVariables) {
		t.Fatalf("expected response unchanged with no names filter: %#v", result.CapturedVariables)
	}
}

// TestParsePausePointCapturedVariableNames verifies comma-splitting, whitespace trimming, and
// that empty entries are dropped.
func TestParsePausePointCapturedVariableNames(t *testing.T) {
	cases := map[string][]string{
		"":                        nil,
		"velocity":                {"velocity"},
		"velocity,this":           {"velocity", "this"},
		"velocity, this , health": {"velocity", "this", "health"},
		"velocity,,this":          {"velocity", "this"},
	}

	for input, expected := range cases {
		names := parsePausePointCapturedVariableNames(input)
		if len(names) != len(expected) {
			t.Fatalf("input %q: length mismatch: got %#v, want %#v", input, names, expected)
		}
		for index, name := range names {
			if name != expected[index] {
				t.Fatalf("input %q: name[%d] mismatch: got %q, want %q", input, index, name, expected[index])
			}
		}
	}
}

// TestFilterPausePointCapturedVariablesByNameReportsNotFound verifies which requested names are
// reported as matching nothing: request order is preserved, a history-only match counts as found, a
// repeat is reported once, and the all-or-nothing flag stays consistent with the list.
func TestFilterPausePointCapturedVariablesByNameReportsNotFound(t *testing.T) {
	baseResponse := capturedVariableNamesFilterResponse

	t.Run("reports which requested names matched nothing, in the requested order", func(t *testing.T) {
		result := filterPausePointCapturedVariablesByName(
			baseResponse(), []string{"shield", "velocity", "armor"})
		if len(result.CapturedVariableNamesNotFound) != 2 ||
			result.CapturedVariableNamesNotFound[0] != "shield" ||
			result.CapturedVariableNamesNotFound[1] != "armor" {
			t.Fatalf("expected the unmatched names in request order: %#v", result.CapturedVariableNamesNotFound)
		}
		if result.CapturedVariableNameFilterNoMatch {
			t.Fatal("a partial match must not set CapturedVariableNameFilterNoMatch")
		}
	})

	t.Run("a name matched only in history is not reported as missing", func(t *testing.T) {
		response := baseResponse()
		response.CapturedVariables = nil
		result := filterPausePointCapturedVariablesByName(response, []string{"health"})
		if len(result.CapturedVariableNamesNotFound) != 0 {
			t.Fatalf("a history-only match must count as found: %#v", result.CapturedVariableNamesNotFound)
		}
	})

	t.Run("all names missing sets both the list and the no-match flag", func(t *testing.T) {
		result := filterPausePointCapturedVariablesByName(baseResponse(), []string{"shield", "armor"})
		if len(result.CapturedVariableNamesNotFound) != 2 {
			t.Fatalf("expected both names reported missing: %#v", result.CapturedVariableNamesNotFound)
		}
		if !result.CapturedVariableNameFilterNoMatch {
			t.Fatal("expected CapturedVariableNameFilterNoMatch to stay true when nothing matches")
		}
	})

	t.Run("a name requested twice is reported missing once", func(t *testing.T) {
		result := filterPausePointCapturedVariablesByName(
			baseResponse(), []string{"shield", "shield"})
		if len(result.CapturedVariableNamesNotFound) != 1 {
			t.Fatalf("expected the repeated name once: %#v", result.CapturedVariableNamesNotFound)
		}
	})

	t.Run("every name matching leaves the missing list empty", func(t *testing.T) {
		result := filterPausePointCapturedVariablesByName(baseResponse(), []string{"velocity", "health"})
		if result.CapturedVariableNamesNotFound != nil {
			t.Fatalf("expected no missing names: %#v", result.CapturedVariableNamesNotFound)
		}
	})
}

// truncatedNameFilterResponse is a Unity snapshot where preview clipping set
// CapturedVariablesTruncated without a variable-count drop (Count==0).
func truncatedNameFilterResponse() pausePointStatusResponse {
	return pausePointStatusResponse{
		CapturedVariablesTruncated: true,
		TruncatedVariableCount:     0,
		CapturedVariables: []pausePointCapturedVariable{
			{Name: "health", Scope: "Local", TypeName: "Int32", Value: pausePointVariableValue("100")},
			{Name: "board", Scope: "Local", TypeName: "Boolean[,]", Value: pausePointVariableValue("[...]"), Truncated: true},
		},
		CapturedVariableHistory: []pausePointCapturedHistoryFrame{
			{
				HitSequence: 1,
				CapturedVariables: []pausePointCapturedVariable{
					{Name: "board", Scope: "Local", TypeName: "Boolean[,]", Value: pausePointVariableValue("[...]"), Truncated: true},
				},
			},
		},
	}
}

const wantCapturedVariablesTruncatedNote = "the truncation flag refers to a variable excluded by --captured-variable-names; every variable listed here is complete."

// TestFilterPausePointCapturedVariablesByNameSetsTruncatedNote verifies the CLI
// explains CapturedVariablesTruncated when --captured-variable-names dropped every
// truncated variable, and that it does not rewrite the truncation flag.
func TestFilterPausePointCapturedVariablesByNameSetsTruncatedNote(t *testing.T) {
	t.Run("note is set when the truncated variable is excluded", func(t *testing.T) {
		assertTruncatedNoteIsSetWhenTruncatedVariableIsExcluded(t)
	})
	t.Run("note is omitted when a truncated variable remains", func(t *testing.T) {
		assertTruncatedNoteIsOmittedWhenTruncatedVariableRemains(t)
	})
	t.Run("note is omitted when no name filter is applied", func(t *testing.T) {
		assertTruncatedNoteIsOmittedWhenNoNameFilterIsApplied(t)
	})
	t.Run("note is omitted when TruncatedVariableCount is already non-zero", func(t *testing.T) {
		assertTruncatedNoteIsOmittedWhenTruncatedVariableCountIsNonZero(t)
	})
	t.Run("note is omitted when CapturedVariablesTruncated is false", func(t *testing.T) {
		assertTruncatedNoteIsOmittedWhenCapturedVariablesTruncatedIsFalse(t)
	})
	t.Run("note is omitted when only a history variable remains truncated", func(t *testing.T) {
		assertTruncatedNoteIsOmittedWhenOnlyHistoryVariableRemainsTruncated(t)
	})
}

func assertTruncatedNoteIsSetWhenTruncatedVariableIsExcluded(t *testing.T) {
	t.Helper()
	result := filterPausePointCapturedVariablesByName(truncatedNameFilterResponse(), []string{"health"})
	if !result.CapturedVariablesTruncated {
		t.Fatal("CapturedVariablesTruncated must stay true; the note explains it")
	}
	if result.CapturedVariablesTruncatedNote != wantCapturedVariablesTruncatedNote {
		t.Fatalf("expected truncated-by-name-filter note: %q", result.CapturedVariablesTruncatedNote)
	}
	if len(result.CapturedVariables) != 1 || result.CapturedVariables[0].Name != "health" {
		t.Fatalf("expected only health to survive: %#v", result.CapturedVariables)
	}
}

func assertTruncatedNoteIsOmittedWhenTruncatedVariableRemains(t *testing.T) {
	t.Helper()
	result := filterPausePointCapturedVariablesByName(truncatedNameFilterResponse(), []string{"board"})
	if result.CapturedVariablesTruncatedNote != "" {
		t.Fatalf("listed truncated values must not get the complete-list note: %q", result.CapturedVariablesTruncatedNote)
	}
	if !result.CapturedVariablesTruncated {
		t.Fatal("CapturedVariablesTruncated must stay true")
	}
}

func assertTruncatedNoteIsOmittedWhenNoNameFilterIsApplied(t *testing.T) {
	t.Helper()
	result := filterPausePointCapturedVariablesByName(truncatedNameFilterResponse(), nil)
	if result.CapturedVariablesTruncatedNote != "" {
		t.Fatalf("unfiltered responses must not get the CLI note: %q", result.CapturedVariablesTruncatedNote)
	}
}

func assertTruncatedNoteIsOmittedWhenTruncatedVariableCountIsNonZero(t *testing.T) {
	t.Helper()
	response := truncatedNameFilterResponse()
	response.TruncatedVariableCount = 1
	response.TruncatedVariableNames = []string{"extraField"}
	result := filterPausePointCapturedVariablesByName(response, []string{"health"})
	if result.CapturedVariablesTruncatedNote != "" {
		t.Fatalf("count-cap truncation must not be described as a name-filter drop: %q", result.CapturedVariablesTruncatedNote)
	}
}

func assertTruncatedNoteIsOmittedWhenCapturedVariablesTruncatedIsFalse(t *testing.T) {
	t.Helper()
	response := truncatedNameFilterResponse()
	response.CapturedVariablesTruncated = false
	result := filterPausePointCapturedVariablesByName(response, []string{"health"})
	if result.CapturedVariablesTruncatedNote != "" {
		t.Fatalf("a non-truncated snapshot must not get the CLI note: %q", result.CapturedVariablesTruncatedNote)
	}
}

func assertTruncatedNoteIsOmittedWhenOnlyHistoryVariableRemainsTruncated(t *testing.T) {
	t.Helper()
	response := pausePointStatusResponse{
		CapturedVariablesTruncated: true,
		TruncatedVariableCount:     0,
		CapturedVariables: []pausePointCapturedVariable{
			{Name: "health", Scope: "Local", TypeName: "Int32", Value: pausePointVariableValue("100")},
			{Name: "velocity", Scope: "Local", TypeName: "Vector3", Value: pausePointVariableValue("(1,0,0)")},
		},
		CapturedVariableHistory: []pausePointCapturedHistoryFrame{
			{
				HitSequence: 1,
				CapturedVariables: []pausePointCapturedVariable{
					{Name: "health", Scope: "Local", TypeName: "Int32", Value: pausePointVariableValue("100")},
					{Name: "board", Scope: "Local", TypeName: "Boolean[,]", Value: pausePointVariableValue("[...]"), Truncated: true},
				},
			},
		},
	}
	result := filterPausePointCapturedVariablesByName(response, []string{"health", "board"})
	if result.CapturedVariablesTruncatedNote != "" {
		t.Fatalf("a truncated history survivor must not get the complete-list note: %q", result.CapturedVariablesTruncatedNote)
	}
	if len(result.CapturedVariables) != 1 || result.CapturedVariables[0].Truncated {
		t.Fatalf("expected only complete current variables: %#v", result.CapturedVariables)
	}
	if len(result.CapturedVariableHistory) != 1 ||
		len(result.CapturedVariableHistory[0].CapturedVariables) != 2 ||
		!result.CapturedVariableHistory[0].CapturedVariables[1].Truncated {
		t.Fatalf("expected truncated board to remain in history: %#v", result.CapturedVariableHistory)
	}
}

// TestPausePointStatusResponseIncludesCapturedVariablesTruncatedNote verifies the
// CLI note survives json.Marshal under that exact key.
func TestPausePointStatusResponseIncludesCapturedVariablesTruncatedNote(t *testing.T) {
	marshaled, err := json.Marshal(pausePointStatusResponse{
		CapturedVariablesTruncatedNote: pausePointCapturedVariablesTruncatedNote,
	})
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	var decoded map[string]json.RawMessage
	if err := json.Unmarshal(marshaled, &decoded); err != nil {
		t.Fatalf("unmarshal envelope failed: %v", err)
	}

	rawNote, ok := decoded["CapturedVariablesTruncatedNote"]
	if !ok {
		t.Fatalf("CapturedVariablesTruncatedNote missing from JSON: %s", marshaled)
	}

	var note string
	if err := json.Unmarshal(rawNote, &note); err != nil {
		t.Fatalf("unmarshal note failed: %v", err)
	}
	if note != pausePointCapturedVariablesTruncatedNote {
		t.Fatalf("note mismatch: got %#v, want %#v", note, pausePointCapturedVariablesTruncatedNote)
	}
}

// TestPausePointStatusResponseOmitsEmptyCapturedVariablesTruncatedNote verifies an
// empty note is omitted so unfiltered Unity payloads keep their historical shape.
func TestPausePointStatusResponseOmitsEmptyCapturedVariablesTruncatedNote(t *testing.T) {
	marshaled, err := json.Marshal(pausePointStatusResponse{
		CapturedVariablesTruncated: true,
	})
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	if strings.Contains(string(marshaled), "CapturedVariablesTruncatedNote") {
		t.Fatalf("empty CapturedVariablesTruncatedNote must be omitted from JSON: %s", marshaled)
	}
}
