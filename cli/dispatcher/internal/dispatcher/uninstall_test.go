package dispatcher

import (
	"bytes"
	"context"
	"strings"
	"testing"
)

func TestRunDispatcherUninstallHelpDoesNotRequireUnityProject(t *testing.T) {
	// Verifies uninstall help is available before Unity project resolution.
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{"uninstall", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("uninstall help failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "uloop uninstall") {
		t.Fatalf("uninstall help output mismatch: %s", stdout.String())
	}
}

func TestWriteUninstallPathCompletionForWindowsMentionsUserPathRemoval(t *testing.T) {
	// Verifies Windows uninstall output matches the automatic User PATH cleanup.
	var stdout bytes.Buffer

	writeUninstallPathCompletion(&stdout, "windows")

	if !strings.Contains(stdout.String(), "User PATH entry will be removed") {
		t.Fatalf("uninstall completion output mismatch: %s", stdout.String())
	}
}

func TestWriteUninstallPathCompletionForDarwinMentionsManualPathCleanup(t *testing.T) {
	// Verifies POSIX uninstall output tells the user to open a new terminal and does not contradict the PATH block removal the embedded script already reported.
	var stdout bytes.Buffer

	writeUninstallPathCompletion(&stdout, "darwin")

	if !strings.Contains(stdout.String(), "Open a new terminal to apply the PATH change") {
		t.Fatalf("uninstall completion output mismatch: %s", stdout.String())
	}
	if strings.Contains(stdout.String(), "PATH settings were not changed") {
		t.Fatalf("uninstall completion output must not claim PATH was untouched: %s", stdout.String())
	}
}

func TestPrintUninstallHelpDescribesPosixPathBlockRemoval(t *testing.T) {
	// Verifies uninstall help states that the shell profile PATH block is removed on macOS and Linux.
	var stdout bytes.Buffer

	printUninstallHelp(&stdout)

	if !strings.Contains(stdout.String(), "On macOS and Linux, removes the uloop PATH block from the shell profile.") {
		t.Fatalf("uninstall help output mismatch: %s", stdout.String())
	}
}
