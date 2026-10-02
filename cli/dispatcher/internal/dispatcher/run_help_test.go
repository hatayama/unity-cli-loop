package dispatcher

import (
	"bytes"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/tools"
)

func TestPrintUnityToolCommandHelpReportsEmptyCache(t *testing.T) {
	// Verifies a project cache with no tools tells the user to sync instead of printing an empty list.
	var stdout bytes.Buffer

	printUnityToolCommandHelp(&stdout, clicore.ToolsCache{}, true)

	if !strings.Contains(stdout.String(), "No cached Unity tools found. Run `uloop sync` while Unity is running.") {
		t.Fatalf("missing empty-cache line: %s", stdout.String())
	}
	if strings.Contains(stdout.String(), "refresh this list") {
		t.Fatalf("an empty cache must stop before the list footer: %s", stdout.String())
	}
}

func TestPrintUnityToolCommandHelpSkipsNativeCommandsAndTruncates(t *testing.T) {
	// Verifies cached tools that shadow native commands are left out and long descriptions are cut with an ellipsis.
	longDescription := strings.Repeat("word ", 40)
	cache := clicore.ToolsCache{Tools: []tools.ToolDefinition{
		{Name: clicore.LaunchCommandName, Description: "Shadowing tool that must not be listed."},
		{Name: "sample-tool", Description: longDescription},
	}}
	var stdout bytes.Buffer

	printUnityToolCommandHelp(&stdout, cache, true)

	output := stdout.String()
	if strings.Contains(output, "Shadowing tool") {
		t.Fatalf("a cached tool named like a native command must be skipped: %s", output)
	}
	want := strings.TrimSpace(longDescription[:maxCommandListDescriptionLength-3]) + "..."
	if !strings.Contains(output, "sample-tool") || !strings.Contains(output, want+"\n") {
		t.Fatalf("missing truncated description %q: %s", want, output)
	}
}
