package projectrunner

import "testing"

// TestFilterPausePointCapturedVariablesByName verifies the --captured-variable-names filter:
// single-name selection, multi-name selection, a name with no match, and that it composes with
// the --captured-variables mode (filter narrows first, then mode strips values).
func TestFilterPausePointCapturedVariablesByName(t *testing.T) {
	baseResponse := func() pausePointStatusResponse {
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

	t.Run("single name keeps only the matching variable", func(t *testing.T) {
		result := filterPausePointCapturedVariablesByName(baseResponse(), []string{"velocity"})
		if len(result.CapturedVariables) != 1 || result.CapturedVariables[0].Name != "velocity" {
			t.Fatalf("expected only velocity to survive: %#v", result.CapturedVariables)
		}
		if len(result.CapturedVariableHistory[0].CapturedVariables) != 1 {
			t.Fatalf("expected history frame filtered to 1 entry: %#v", result.CapturedVariableHistory)
		}
		if result.CapturedVariableNameFilterNoMatch {
			t.Fatal("expected CapturedVariableNameFilterNoMatch to be false when a match exists")
		}
	})

	t.Run("multiple names keep all matching variables in order", func(t *testing.T) {
		result := filterPausePointCapturedVariablesByName(baseResponse(), []string{"velocity", "this"})
		if len(result.CapturedVariables) != 2 {
			t.Fatalf("expected velocity and this to survive: %#v", result.CapturedVariables)
		}
		if result.CapturedVariables[0].Name != "velocity" || result.CapturedVariables[1].Name != "this" {
			t.Fatalf("expected original order preserved: %#v", result.CapturedVariables)
		}
	})

	t.Run("no matching name empties the arrays and sets the no-match flag", func(t *testing.T) {
		result := filterPausePointCapturedVariablesByName(baseResponse(), []string{"doesNotExist"})
		if len(result.CapturedVariables) != 0 {
			t.Fatalf("expected no captured variables to survive: %#v", result.CapturedVariables)
		}
		if len(result.CapturedVariableHistory[0].CapturedVariables) != 0 {
			t.Fatalf("expected history frame emptied: %#v", result.CapturedVariableHistory)
		}
		if !result.CapturedVariableNameFilterNoMatch {
			t.Fatal("expected CapturedVariableNameFilterNoMatch to be true when nothing matches")
		}
	})

	t.Run("composes with captured-variables names mode: filter first, then strip values", func(t *testing.T) {
		filtered := filterPausePointCapturedVariablesByName(baseResponse(), []string{"velocity"})
		result := applyPausePointCapturedVariablesMode(filtered, pausePointCapturedVariablesModeNames)
		if len(result.CapturedVariables) != 1 || result.CapturedVariables[0].Name != "velocity" {
			t.Fatalf("expected only velocity to survive the filter: %#v", result.CapturedVariables)
		}
		if result.CapturedVariables[0].Value != nil {
			t.Fatalf("expected Value stripped by names mode: %#v", result.CapturedVariables[0])
		}
	})

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

	t.Run("empty names list leaves the response unchanged", func(t *testing.T) {
		original := baseResponse()
		result := filterPausePointCapturedVariablesByName(original, nil)
		if len(result.CapturedVariables) != len(original.CapturedVariables) {
			t.Fatalf("expected response unchanged with no names filter: %#v", result.CapturedVariables)
		}
	})
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
