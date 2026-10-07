//go:build darwin

package dispatcher

import "os/exec"

// Resets every catchable signal to SIG_DFL without SA_SIGINFO, then replaces the shell with the Editor so the
// Editor keeps the PID that Start reported. The Editor path arrives as $0 and its arguments as $@,
// so none of them is ever parsed by the shell.
const resetSignalsThenExecScript = `trap - HUP INT QUIT ILL TRAP ABRT EMT FPE BUS SEGV SYS PIPE ALRM TERM URG TSTP CONT CHLD TTIN TTOU IO XCPU XFSZ VTALRM PROF WINCH INFO USR1 USR2; exec "$0" "$@"`

// newPlatformUnityLaunchCommand starts the Editor through /bin/sh so that it starts with clean
// signal dispositions.
//
// Why not exec the Editor directly: on macOS a child of a Go process inherits SA_SIGINFO with a
// SIG_DFL handler for every signal the Go runtime handled (golang/go#81009). .NET NativeAOT tools
// that the Editor runs, such as il2cpp, chain to that inherited SIGUSR1 action during GC thread
// suspension and jump to address 0. The shell's `trap -` assigns SIG_DFL without SA_SIGINFO, which
// holds for bash, dash, and zsh, the shells a user can select as /bin/sh. Remove this
// once the Go toolchain that builds the dispatcher includes the upstream runtime fix.
func newPlatformUnityLaunchCommand(unityPath string, launchArgs []string) *exec.Cmd {
	command := exec.Command("/bin/sh", append([]string{"-c", resetSignalsThenExecScript, unityPath}, launchArgs...)...)
	// Why: the shell itself always starts, so an Editor binary that cannot be executed would only
	// make the shell exit, and launch would wait out its startup timeout instead of reporting it.
	if _, err := exec.LookPath(unityPath); err != nil {
		command.Err = err
	}
	return command
}
