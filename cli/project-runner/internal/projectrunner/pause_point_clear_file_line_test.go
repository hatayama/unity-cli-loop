package projectrunner

import (
	"bytes"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies --all cannot be combined with --file or --line on clear-pause-point.
func TestExtractPausePointClearFileLineFlagsRejectsAllWithFileLine(t *testing.T) {
	tests := []struct {
		name string
		args []string
	}{
		{
			name: "all with file and line",
			args: []string{"--all", "--file", "Assets/Scripts/Marker.cs", "--line", "42"},
		},
		{
			name: "all with file only",
			args: []string{"--all", "--file", "Assets/Scripts/Marker.cs"},
		},
		{
			name: "all with line only",
			args: []string{"--all", "--line", "42"},
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			_, _, err := extractPausePointClearFileLineFlags(pausePointClearCommandName, test.args)
			if err == nil || err.Error() != "--all cannot be combined with --file or --line." {
				t.Fatalf("clear --all with file:line error = %v", err)
			}
		})
	}
}

// Verifies --file/--line on a different command are left untouched.
func TestExtractPausePointClearFileLineFlagsIgnoresOtherCommands(t *testing.T) {
	args := []string{"--file", "Assets/Scripts/Marker.cs", "--line", "42"}
	remaining, queryID, err := extractPausePointClearFileLineFlags("get-logs", args)
	if err != nil {
		t.Fatalf("extract failed: %v", err)
	}
	if queryID != "" || len(remaining) != 4 {
		t.Fatalf("other commands must pass args through: %s %#v", queryID, remaining)
	}
}

// Verifies prepareDynamicToolParams injects the composed file:line id into params["Id"],
// so removing the extract or apply wiring in runner_commands.go fails this test.
func TestPrepareDynamicToolParamsInjectsClearPausePointFileLineID(t *testing.T) {
	tool, ok := clicore.FindTool(clicore.LoadDefaultTools(), pausePointClearCommandName)
	if !ok {
		t.Fatal("clear-pause-point was not found in default tools")
	}

	var stderr bytes.Buffer
	params, _, ok := prepareDynamicToolParams(
		pausePointClearCommandName,
		[]string{"--file", "Assets/Scripts/Marker.cs", "--line", "42"},
		tool,
		unityipc.Connection{ProjectRoot: t.TempDir()},
		"",
		&stderr,
	)
	if !ok {
		t.Fatalf("prepare failed: %s", stderr.String())
	}
	id, isString := params["Id"].(string)
	if !isString || id != "Assets/Scripts/Marker.cs:42" {
		t.Fatalf("Id = %#v, want Assets/Scripts/Marker.cs:42", params["Id"])
	}
}

// Verifies prepareDynamicToolParams rejects --id combined with --file/--line and writes
// the ArgumentError to stderr, so the production extract call is not a no-op.
func TestPrepareDynamicToolParamsRejectsClearPausePointCombinedIDAndFile(t *testing.T) {
	tool, ok := clicore.FindTool(clicore.LoadDefaultTools(), pausePointClearCommandName)
	if !ok {
		t.Fatal("clear-pause-point was not found in default tools")
	}

	var stderr bytes.Buffer
	params, _, ok := prepareDynamicToolParams(
		pausePointClearCommandName,
		[]string{"--id", "marker", "--file", "Assets/Scripts/Marker.cs", "--line", "42"},
		tool,
		unityipc.Connection{ProjectRoot: t.TempDir()},
		"",
		&stderr,
	)
	if ok || params != nil {
		t.Fatalf("expected failure, got ok=%v params=%#v", ok, params)
	}
	if !strings.Contains(stderr.String(), "--id cannot be combined with --file or --line.") {
		t.Fatalf("stderr missing combination error: %s", stderr.String())
	}
}

// Verifies clear-pause-point args without --file/--line pass through untouched with no composed id.
func TestExtractPausePointClearFileLineFlagsPassesThroughWithoutFileLine(t *testing.T) {
	args := []string{"--all", "--other", "value"}

	remaining, queryID, err := extractPausePointClearFileLineFlags(pausePointClearCommandName, args)

	if err != nil || queryID != "" {
		t.Fatalf("unexpected result: queryID=%q err=%v", queryID, err)
	}
	if strings.Join(remaining, " ") != strings.Join(args, " ") {
		t.Fatalf("remaining = %#v, want %#v", remaining, args)
	}
}

// Verifies --file or --id with no value is rejected as a missing value.
func TestExtractPausePointClearFileLineFlagsRejectsMissingValues(t *testing.T) {
	for _, args := range [][]string{{"--file"}, {"--file", "Assets/A.cs", "--line", "3", "--id"}} {
		_, _, err := extractPausePointClearFileLineFlags(pausePointClearCommandName, args)
		if requireArgumentError(t, err).ExpectedType != "string" {
			t.Fatalf("args %v: expected a missing-value error, got %v", args, err)
		}
	}
}

// Verifies a composed file:line id never overwrites an explicit Id param.
func TestApplyPausePointClearFileLineIDRejectsExplicitID(t *testing.T) {
	params := map[string]any{pausePointClearIdPropertyName: "named"}

	err := applyPausePointClearFileLineID(params, "Assets/A.cs:3")

	if argumentError := requireArgumentError(t, err); argumentError.Option != "--id" {
		t.Fatalf("Option = %q, want --id", argumentError.Option)
	}
	if params[pausePointClearIdPropertyName] != "named" {
		t.Fatalf("explicit Id must be kept: %#v", params)
	}
}
