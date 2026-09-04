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
