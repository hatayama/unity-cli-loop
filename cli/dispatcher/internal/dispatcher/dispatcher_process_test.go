package dispatcher

import (
	"bytes"
	"context"
	"io"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

func TestTryHandleDispatcherInfoRequestPrintsDispatcherHelpWithoutArgs(t *testing.T) {
	// Verifies a bare invocation outside a project prints the dispatcher's own help.
	var stdout bytes.Buffer

	handled, code := tryHandleDispatcherInfoRequest(nil, &stdout)

	if !handled || code != 0 || !strings.Contains(stdout.String(), "Dispatcher. Finds the Unity project, then dispatches live Unity tool commands.") {
		t.Fatalf("unexpected result: handled=%t code=%d stdout=%s", handled, code, stdout.String())
	}
}

func TestTryHandlePreConnectionRequestRoutesCompileCheckAndPassesThroughToolCommands(t *testing.T) {
	// Verifies compile-check is handled before connecting, and a Unity tool command is left for the project runner.
	deps := fakeDispatcherRunDeps(t)
	deps.launch = isolatedLaunchTestDeps(t)
	var stderr bytes.Buffer

	compileArgs := []string{clicore.CompileCheckCommandName, "--bogus"}
	handled, code := tryHandlePreConnectionRequestWithDeps(context.Background(), compileArgs, compileArgs[0], compileArgs[1:], t.TempDir(), "", io.Discard, &stderr, deps)
	if !handled || code != 1 || !strings.Contains(stderr.String(), "Unknown compile-check option: --bogus") {
		t.Fatalf("compile-check was not routed: handled=%t code=%d stderr=%s", handled, code, stderr.String())
	}

	var stdout bytes.Buffer
	stderr.Reset()
	toolArgs := []string{"get-logs"}
	handled, code = tryHandlePreConnectionRequestWithDeps(context.Background(), toolArgs, toolArgs[0], nil, t.TempDir(), "", &stdout, &stderr, deps)
	if handled || code != 0 || stdout.Len() != 0 || stderr.Len() != 0 {
		t.Fatalf("a tool command must pass through: handled=%t code=%d stdout=%s stderr=%s", handled, code, stdout.String(), stderr.String())
	}
}
