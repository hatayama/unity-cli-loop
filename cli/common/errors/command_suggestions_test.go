package clierrors

import (
	"reflect"
	"testing"
)

// Verifies suggestions are ordered by ascending Levenshtein distance.
func TestSuggestCommandsOrdersByDistance(t *testing.T) {
	suggestions := suggestCommands("compil", []string{"launch", "compile", "clear-console"})

	if len(suggestions) == 0 || suggestions[0] != "compile" {
		t.Fatalf("expected compile first, got %#v", suggestions)
	}
}

// Verifies equal distances break ties by ascending command name.
func TestSuggestCommandsBreaksDistanceTiesByName(t *testing.T) {
	suggestions := suggestCommands("ab", []string{"ac", "ad", "aa"})

	expected := []string{"aa", "ac", "ad"}
	if !reflect.DeepEqual(suggestions, expected) {
		t.Fatalf("tie-break order mismatch: got %#v, want %#v", suggestions, expected)
	}
}

// Verifies more than five candidates are clipped to maxCommandSuggestions.
func TestSuggestCommandsCapsAtFive(t *testing.T) {
	available := []string{"a", "b", "c", "d", "e", "f", "g"}
	suggestions := suggestCommands("z", available)

	if len(suggestions) != maxCommandSuggestions {
		t.Fatalf("expected %d suggestions, got %#v", maxCommandSuggestions, suggestions)
	}
}

// Verifies fewer than five candidates returns the full sorted list.
func TestSuggestCommandsReturnsAllWhenFewerThanCap(t *testing.T) {
	available := []string{"compile", "launch"}
	suggestions := suggestCommands("compil", available)

	if len(suggestions) != 2 {
		t.Fatalf("expected both candidates, got %#v", suggestions)
	}
	if suggestions[0] != "compile" {
		t.Fatalf("expected compile first, got %#v", suggestions)
	}
}

// Verifies an empty available-command list yields an empty suggestion list.
func TestSuggestCommandsReturnsEmptyForEmptyAvailableList(t *testing.T) {
	suggestions := suggestCommands("compile", nil)

	if len(suggestions) != 0 {
		t.Fatalf("expected empty suggestions, got %#v", suggestions)
	}
}

// Verifies identical strings have distance zero and single-char edits distance one.
func TestLevenshteinDistanceBasicCases(t *testing.T) {
	if distance := levenshteinDistance("compile", "compile"); distance != 0 {
		t.Fatalf("identical distance: got %d", distance)
	}
	if distance := levenshteinDistance("compil", "compile"); distance != 1 {
		t.Fatalf("one-edit distance: got %d", distance)
	}
	if distance := levenshteinDistance("", "abc"); distance != 3 {
		t.Fatalf("empty-to-string distance: got %d", distance)
	}
}
