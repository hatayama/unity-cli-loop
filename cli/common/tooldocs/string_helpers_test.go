package tooldocs

import "testing"

// Verifies that the first non-blank line is returned trimmed and that a blank description yields nothing.
func TestFirstHelpLine(t *testing.T) {
	cases := map[string]string{
		"\n  First line  \nSecond line": "First line",
		"\n  \n\t":                      "",
		"":                              "",
	}
	for description, expected := range cases {
		if actual := FirstHelpLine(description); actual != expected {
			t.Errorf("FirstHelpLine(%q) = %q, want %q", description, actual, expected)
		}
	}
}
