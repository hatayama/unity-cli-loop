package clicore

import (
	"path/filepath"
	"reflect"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clitest"
)

// Verifies that disabled tools are read from .uloop/settings.tools.json and that a missing, blank,
// unparsable, or field-less settings file yields an empty list.
func TestLoadDisabledTools(t *testing.T) {
	cases := []struct {
		name     string
		content  *string
		expected []string
	}{
		{name: "missing file", expected: []string{}},
		{name: "blank file", content: stringPointer("  \n"), expected: []string{}},
		{name: "invalid json", content: stringPointer("{"), expected: []string{}},
		{name: "no disabled tools", content: stringPointer(`{"other":true}`), expected: []string{}},
		{name: "disabled tools", content: stringPointer(`{"disabledTools":["compile","get-logs"]}`), expected: []string{"compile", "get-logs"}},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			projectRoot := t.TempDir()
			if testCase.content != nil {
				clitest.WriteProjectFile(t, projectRoot, filepath.Join(uloopSettingsDir, toolSettingsFile), *testCase.content)
			}

			actual := LoadDisabledTools(projectRoot)

			if !reflect.DeepEqual(actual, testCase.expected) {
				t.Fatalf("disabled tools mismatch: actual=%#v expected=%#v", actual, testCase.expected)
			}
		})
	}
}

func stringPointer(value string) *string {
	return &value
}

// Verifies that a tool counts as disabled only when its exact name is listed.
func TestIsToolDisabledByToolSettings(t *testing.T) {
	disabledTools := []string{"compile", "get-logs"}
	if !IsToolDisabledByToolSettings("get-logs", disabledTools) {
		t.Fatal("listed tool should be disabled")
	}
	if IsToolDisabledByToolSettings("get-log", disabledTools) {
		t.Fatal("unlisted tool should not be disabled")
	}
	if IsToolDisabledByToolSettings("compile", nil) {
		t.Fatal("no settings should disable nothing")
	}
}
