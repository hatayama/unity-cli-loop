package dispatcher

import (
	"bytes"
	"context"
	"runtime"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
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

func TestTryHandleUninstallRequestRejectsExtraArguments(t *testing.T) {
	// Verifies uninstall refuses any option before resolving or removing anything.
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	handled, code := tryHandleUninstallRequest(context.Background(), []string{"uninstall", "--force"}, &stdout, &stderr)

	if !handled || code != 1 {
		t.Fatalf("result mismatch: handled=%t code=%d", handled, code)
	}
	if !strings.Contains(stderr.String(), "Unknown uninstall option: --force") {
		t.Fatalf("missing option error: %s", stderr.String())
	}
	if stdout.Len() != 0 {
		t.Fatalf("no removal progress may be printed: %s", stdout.String())
	}
}

func TestTryHandleUninstallRequestReportsUnresolvableInstallDirectory(t *testing.T) {
	// Verifies uninstall stops with code 1 when no install directory can be resolved.
	unsetNativeInstallLocation(t)
	if _, err := resolveUninstallInstallDir(runtime.GOOS); err == nil {
		t.Fatal("precondition failed: the uninstall directory still resolves, so the real uninstaller could run")
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	handled, code := tryHandleUninstallRequest(context.Background(), []string{"uninstall"}, &stdout, &stderr)

	if !handled || code != 1 {
		t.Fatalf("result mismatch: handled=%t code=%d", handled, code)
	}
	if stderr.Len() == 0 || stdout.Len() != 0 {
		t.Fatalf("expected only an error envelope: stdout=%q stderr=%q", stdout.String(), stderr.String())
	}
}

func TestResolveUninstallInstallDir(t *testing.T) {
	// Verifies uninstall honors ULOOP_INSTALL_DIR and reports the uninstall-specific message on unsupported platforms.
	t.Setenv(nativepath.InstallDirEnvName, "/opt/uloop/bin")

	installDir, err := resolveUninstallInstallDir("linux")
	if err != nil || installDir != "/opt/uloop/bin" {
		t.Fatalf("unexpected result: dir=%q err=%v", installDir, err)
	}

	t.Setenv(nativepath.InstallDirEnvName, "")
	if _, err := resolveUninstallInstallDir("plan9"); err == nil || err.Error() != uninstallUnsupportedOSMessage {
		t.Fatalf("expected %q, got %v", uninstallUnsupportedOSMessage, err)
	}
}
