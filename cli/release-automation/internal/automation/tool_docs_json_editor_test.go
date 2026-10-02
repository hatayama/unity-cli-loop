package automation

import (
	"strings"
	"testing"
)

// Verifies only tool and inputSchema/parameterSchema property descriptions are rewritten: a description outside "tools", one under another schema key, and escaped quotes around the old value leave every other byte untouched.
func TestReplaceCatalogDescriptionsRewritesOnlyCatalogDescriptions(t *testing.T) {
	content := `{"description":"root","other":[{"description":"keep"}],"tools":[{"description":"say \"hi\" \\","parameterSchema":{"properties":{"P":{"description":"old p"}}},"otherSchema":{"properties":{"P":{"description":"keep"}}},"name":"t"}]}`
	replacements := map[descriptionKey]string{
		{Tool: "t"}:                "new <tool>",
		{Tool: "t", Property: "P"}: "new p",
	}

	edited, err := replaceCatalogDescriptions([]byte(content), replacements)
	if err != nil {
		t.Fatalf("replaceCatalogDescriptions failed: %v", err)
	}
	want := `{"description":"root","other":[{"description":"keep"}],"tools":[{"description":"new <tool>","parameterSchema":{"properties":{"P":{"description":"new p"}}},"otherSchema":{"properties":{"P":{"description":"keep"}}},"name":"t"}]}`
	if string(edited) != want {
		t.Fatalf("edited =\n%s\nwant\n%s", edited, want)
	}
}

// Verifies malformed JSON and a tool with no name field fail instead of producing a partially edited catalog.
func TestReplaceCatalogDescriptionsRejectsUnusableCatalogs(t *testing.T) {
	cases := []struct {
		name    string
		content string
		wantErr string
	}{
		{"truncated object", `{"tools":[{"description":"d"`, "EOF"},
		{"truncated value", `{"tools":[{"description":`, "EOF"},
		{"invalid token", `{"tools":[{"description":"d",]}`, "invalid character"},
		{"tool without name", `{"tools":[{"description":"d"}]}`, "tool at index 0 has no name field"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			_, err := replaceCatalogDescriptions([]byte(testCase.content), map[descriptionKey]string{})

			if err == nil || !strings.Contains(err.Error(), testCase.wantErr) {
				t.Fatalf("expected error containing %q, got %v", testCase.wantErr, err)
			}
		})
	}
}

// Verifies the literal range is found only when the offset ends a string, and an opening quote preceded by an odd number of backslashes is skipped as escaped.
func TestStringLiteralRange(t *testing.T) {
	cases := []struct {
		name      string
		content   string
		endOffset int
		wantStart int
		wantOK    bool
	}{
		{"zero offset", `"a"`, 0, 0, false},
		{"offset past the end", `"a"`, 4, 0, false},
		{"offset not after a quote", `"a"x`, 4, 0, false},
		{"no opening quote", `a"`, 2, 0, false},
		{"escaped quote inside", `x"a\"b"`, 7, 1, true},
		{"escaped backslash before the closing quote", `x"a\\"`, 6, 1, true},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			start, end, ok := stringLiteralRange([]byte(testCase.content), testCase.endOffset)

			if ok != testCase.wantOK || (ok && (start != testCase.wantStart || end != testCase.endOffset)) {
				t.Fatalf("stringLiteralRange = (%d, %d, %t), want (%d, %d, %t)", start, end, ok, testCase.wantStart, testCase.endOffset, testCase.wantOK)
			}
		})
	}
}
