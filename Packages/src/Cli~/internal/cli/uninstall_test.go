package cli

import (
	"bytes"
	"context"
	"strings"
	"testing"
)

func TestRunProjectLocalUninstallHelpDoesNotRequireUnityProject(t *testing.T) {
	// Verifies uninstall help is available before Unity project resolution.
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"uninstall", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("uninstall help failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "uloop uninstall") {
		t.Fatalf("uninstall help output mismatch: %s", stdout.String())
	}
}
