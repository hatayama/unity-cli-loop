package clicore

import (
	"bytes"
	"strings"
	"testing"
)

// Tests that WriteJSON keeps integers above float64 precision intact instead of
// rewriting them in scientific notation.
func TestWriteJSONPreservesLargeIntegerPrecision(t *testing.T) {
	var stdout bytes.Buffer

	WriteJSON(&stdout, []byte(`{"id":9007199254740993,"timestamp":1751443200000123456}`))

	output := stdout.String()
	for _, expected := range []string{"9007199254740993", "1751443200000123456"} {
		if !strings.Contains(output, expected) {
			t.Fatalf("WriteJSON corrupted large integer %q:\n%s", expected, output)
		}
	}
}

// Tests that WriteJSON indents nested objects for readable CLI output.
func TestWriteJSONIndentsNestedObjects(t *testing.T) {
	var stdout bytes.Buffer

	WriteJSON(&stdout, []byte(`{"result":{"success":true}}`))

	output := stdout.String()
	if !strings.Contains(output, "\n  \"result\": {\n    \"success\": true\n  }\n") {
		t.Fatalf("WriteJSON did not indent nested objects:\n%s", output)
	}
	if !strings.HasSuffix(output, "\n") {
		t.Fatalf("WriteJSON output must end with a newline:\n%q", output)
	}
}

// Tests that WriteJSON falls back to raw output when the payload is not valid JSON.
func TestWriteJSONFallsBackToRawOutputForInvalidJSON(t *testing.T) {
	var stdout bytes.Buffer

	WriteJSON(&stdout, []byte("not-json"))

	if stdout.String() != "not-json\n" {
		t.Fatalf("WriteJSON fallback mismatch: %q", stdout.String())
	}
}
