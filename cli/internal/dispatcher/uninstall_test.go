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
	// Verifies macOS uninstall output keeps the manual PATH cleanup guidance.
	var stdout bytes.Buffer

	writeUninstallPathCompletion(&stdout, "darwin")

	if !strings.Contains(stdout.String(), "PATH settings were not changed") {
		t.Fatalf("uninstall completion output mismatch: %s", stdout.String())
	}
}
