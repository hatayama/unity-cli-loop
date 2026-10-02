package dispatcher

import (
	"bytes"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

func TestTryHandleCommandHelpRendersDispatcherOwnedCommandsWithoutProject(t *testing.T) {
	// Verifies dispatcher-owned help needs no project, lists options only when the command has them,
	// and shows global options only for commands that act on a project.
	cases := []struct {
		command           string
		wantOptions       bool
		wantGlobalOptions bool
	}{
		{command: clicore.UninstallCommandName},
		{command: clicore.SkillsCommandName, wantGlobalOptions: true},
		{command: clicore.LaunchCommandName, wantOptions: true, wantGlobalOptions: true},
		{command: clicore.InstallCommandName, wantOptions: true},
	}
	for _, testCase := range cases {
		t.Run(testCase.command, func(t *testing.T) {
			var stdout bytes.Buffer
			var stderr bytes.Buffer

			handled, code := tryHandleCommandHelp(testCase.command, t.TempDir(), "", &stdout, &stderr)

			if !handled || code != 0 || stderr.Len() != 0 {
				t.Fatalf("result mismatch: handled=%t code=%d stderr=%s", handled, code, stderr.String())
			}
			output := stdout.String()
			description, _ := nativeCommandDescription(testCase.command)
			if !strings.HasPrefix(output, "Usage:\n  uloop "+testCase.command) || !strings.Contains(output, description) {
				t.Fatalf("missing usage or description: %s", output)
			}
			if got := strings.Contains(output, "Options:\n"); got != testCase.wantOptions {
				t.Fatalf("options section presence = %t, want %t: %s", got, testCase.wantOptions, output)
			}
			if got := strings.Contains(output, "Global options:"); got != testCase.wantGlobalOptions {
				t.Fatalf("global options presence = %t, want %t: %s", got, testCase.wantGlobalOptions, output)
			}
		})
	}
}

func TestTryHandleCommandHelpReportsUnknownCommandInProject(t *testing.T) {
	// Verifies help for a command the resolved project does not expose fails with UNKNOWN_COMMAND instead of empty help.
	projectRoot := createDispatcherUnityProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	handled, code := tryHandleCommandHelp("no-such-tool", projectRoot, projectRoot, &stdout, &stderr)

	if !handled || code != 1 || stdout.Len() != 0 {
		t.Fatalf("result mismatch: handled=%t code=%d stdout=%s", handled, code, stdout.String())
	}
	if !strings.Contains(stderr.String(), `"ErrorCode": "UNKNOWN_COMMAND"`) {
		t.Fatalf("expected UNKNOWN_COMMAND: %s", stderr.String())
	}
}

func TestPrintToolHelpOmitsOptionsMarkerForToolWithoutOptions(t *testing.T) {
	// Verifies a tool with no visible options prints a bare usage line without the [options] marker or section.
	var stdout bytes.Buffer

	printToolHelp(clicore.ToolDefinition{Name: "custom-tool"}, "", &stdout)

	output := stdout.String()
	if !strings.HasPrefix(output, "Usage:\n  uloop custom-tool\n") || strings.Contains(output, "Options:\n") {
		t.Fatalf("unexpected help: %s", output)
	}
}

func TestNativeCommandDescriptionRejectsUnknownCommand(t *testing.T) {
	// Verifies a name outside the native command registry has no description.
	if description, ok := nativeCommandDescription("no-such-command"); ok || description != "" {
		t.Fatalf("unexpected description: %q ok=%t", description, ok)
	}
}
