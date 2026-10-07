//go:build darwin

package dispatcher

import (
	"bytes"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
)

// Prints every catchable signal that still carries SA_SIGINFO, one per line. A freshly exec'd
// process has no handlers, so SA_SIGINFO there can only be the leaked flag. Other flags are allowed
// because zsh, which a user can select as /bin/sh, leaves SA_RESTART behind, and no runtime treats
// SA_RESTART as a handler. SIGFPE is skipped because perl itself sets it to SIG_IGN during startup.
const reportUncleanSignalsPerlScript = `use POSIX;
for my $signal (1 .. 31) {
	next if $signal == SIGKILL || $signal == SIGSTOP || $signal == SIGFPE;
	my $action = POSIX::SigAction->new;
	sigaction($signal, undef, $action) or die "sigaction $signal: $!";
	next if !($action->flags & SA_SIGINFO);
	printf "signal %d handler=%s flags=0x%x\n", $signal, $action->handler, $action->flags;
}`

func requirePerl(t *testing.T) string {
	t.Helper()
	perlPath, err := exec.LookPath("perl")
	if err != nil {
		t.Skip("perl is required to observe the launched process's signal state")
	}
	return perlPath
}

func runUnityLaunchCommand(t *testing.T, executablePath string, launchArgs []string) (string, int) {
	t.Helper()
	command := newUnityLaunchCommand(executablePath, launchArgs)
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	command.Stdout = &stdout
	command.Stderr = &stderr
	if err := command.Run(); err != nil {
		t.Fatalf("launch command failed: %v\nstderr: %s", err, stderr.String())
	}
	return stdout.String(), command.Process.Pid
}

func TestNewUnityLaunchCommandStartsEditorWithDefaultSignalDispositions(t *testing.T) {
	// Verifies the Editor does not inherit SA_SIGINFO on SIG_DFL signals from the Go runtime, which crashes NativeAOT children such as il2cpp.
	perlPath := requirePerl(t)

	output, _ := runUnityLaunchCommand(t, perlPath, []string{"-e", reportUncleanSignalsPerlScript})

	if output != "" {
		t.Fatalf("launched process inherited non-default signal dispositions:\n%s", output)
	}
}

func TestNewUnityLaunchCommandPassesArgumentsVerbatim(t *testing.T) {
	// Verifies arguments reach the Editor unchanged even when they contain spaces, quotes, or shell expansions.
	perlPath := requirePerl(t)
	launchArgs := []string{"-projectPath", "/path with spaces/it's \"quoted\"", "$HOME", "*", ""}

	// "--" stops perl from reading "-projectPath" as one of its own switches.
	output, _ := runUnityLaunchCommand(t, perlPath, append([]string{"-e", `print join("\0", @ARGV)`, "--"}, launchArgs...))

	if output != strings.Join(launchArgs, "\x00") {
		t.Fatalf("arguments changed on the way to the Editor: got %q want %q", output, launchArgs)
	}
}

func TestNewUnityLaunchCommandKeepsReportedPidForEditor(t *testing.T) {
	// Verifies the PID launch records is the Editor's own PID, because launch tracks and focuses the Editor by it.
	perlPath := requirePerl(t)

	output, pid := runUnityLaunchCommand(t, perlPath, []string{"-e", `print $$`})

	if output != strconv.Itoa(pid) {
		t.Fatalf("Editor PID mismatch: Editor reported %q, launch recorded %d", output, pid)
	}
}

func TestNewUnityLaunchCommandFailsToStartWhenEditorIsNotExecutable(t *testing.T) {
	// Verifies an Editor binary that cannot be executed fails at Start instead of letting launch wait out the startup timeout.
	unityPath := filepath.Join(t.TempDir(), "Unity")
	if err := os.WriteFile(unityPath, []byte("not executable"), 0o644); err != nil {
		t.Fatalf("failed to write fake Editor binary: %v", err)
	}

	command := newUnityLaunchCommand(unityPath, []string{"-projectPath", t.TempDir()})
	err := command.Start()

	if err == nil {
		_ = command.Wait()
		t.Fatal("expected Start to fail for a non-executable Editor binary")
	}
}
