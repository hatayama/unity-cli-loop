package projectrunner

import (
	"testing"
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
