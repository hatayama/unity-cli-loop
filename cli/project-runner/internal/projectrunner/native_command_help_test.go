package projectrunner

import (
	"bytes"
	"context"
	"strings"
	"testing"
)

// Verifies await-pause-point --help lists every flag the runner accepts for
// that command, since the dispatcher forwards this help request here instead
// of keeping its own hardcoded options table.
func TestRunProjectLocalAwaitPausePointHelpListsExpectedFlags(t *testing.T) {
	t.Chdir(t.TempDir())

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunProjectLocal(context.Background(), []string{"await-pause-point", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("await-pause-point --help failed: code=%d stderr=%s", code, stderr.String())
	}
	for _, flag := range []string{"--id", "--file", "--line", "--timeout-seconds", "--matching-logs-max-count", "--captured-variables", "--expect", "--trigger", "--resume-play"} {
		if !strings.Contains(stdout.String(), flag) {
			t.Fatalf("await-pause-point --help must list %s: %s", flag, stdout.String())
		}
	}
	if !strings.Contains(stdout.String(), "--project-path") {
		t.Fatalf("await-pause-point --help must list the global --project-path option: %s", stdout.String())
	}
}

// Verifies await-pause-point --help prints the full Options section: every usage column, every
// description, %-34s alignment, and sort order. The expected text is a test-local literal so a
// wrong non-empty description still fails.
func TestRunProjectLocalAwaitPausePointHelpOptionsSection(t *testing.T) {
	const expectedOptions = `Options:
  --captured-variable-names <value>  Restrict CapturedVariables to these comma-separated names
  --captured-variables <value>       How much of each captured variable to include in the response; values: full|names
  --expect <value>                   Compare a captured variable against an expected value (repeatable; name=value)
  --file <value>                     Project-relative source file of a file:line pause point. Requires --line; mutually exclusive with --id
  --id <value>                       Pause-point marker id matching UloopPausePoint.Pause or the id returned by enable-pause-point
  --line <value>                     1-based source line of a file:line pause point. Requires --file; mutually exclusive with --id
  --matching-logs-max-count <value>  Maximum Console logs matching the marker id to include on a hit
  --resume-play                      After confirming the marker is armed, resume PlayMode if paused (before --trigger), so a paused-arm workflow can fire input in one call
  --timeout-seconds <value>          Seconds to wait for a hit before timing out
  --trigger <value>                  Runs a single uloop subcommand in-process right after arming/registration. Pass the subcommand without the leading 'uloop', e.g. "simulate-keyboard --action Press --key Space"
`
	assertNativeCommandHelpOptionsSection(t, "await-pause-point", expectedOptions)
}

// Verifies pause-point-status --help lists the flags it accepts.
func TestRunProjectLocalPausePointStatusHelpListsExpectedFlags(t *testing.T) {
	t.Chdir(t.TempDir())

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunProjectLocal(context.Background(), []string{"pause-point-status", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("pause-point-status --help failed: code=%d stderr=%s", code, stderr.String())
	}
	for _, flag := range []string{"--id", "--file", "--line", "--captured-variables"} {
		if !strings.Contains(stdout.String(), flag) {
			t.Fatalf("pause-point-status --help must list %s: %s", flag, stdout.String())
		}
	}
	if strings.Contains(stdout.String(), "--timeout-seconds") {
		t.Fatalf("pause-point-status --help must not list await-pause-point-only flags: %s", stdout.String())
	}
}

// Verifies pause-point-status --help prints the full Options section. Same test-local-literal
// rule as TestRunProjectLocalAwaitPausePointHelpOptionsSection.
func TestRunProjectLocalPausePointStatusHelpOptionsSection(t *testing.T) {
	const expectedOptions = `Options:
  --captured-variable-names <value>  Restrict CapturedVariables to these comma-separated names
  --captured-variables <value>       How much of each captured variable to include in the response; values: full|names
  --expect <value>                   Compare a captured variable against an expected value (repeatable; name=value)
  --file <value>                     Project-relative source file of a file:line pause point. Requires --line; mutually exclusive with --id
  --id <value>                       Pause-point marker id matching UloopPausePoint.Pause or the id returned by enable-pause-point
  --line <value>                     1-based source line of a file:line pause point. Requires --file; mutually exclusive with --id
`
	assertNativeCommandHelpOptionsSection(t, "pause-point-status", expectedOptions)
}

// Verifies list --help prints the complete names-only option contract, including the usage and
// global options sections that callers need after the unknown-option recovery guidance.
func TestRunProjectLocalListHelpOutput(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"list", "--help"}, &stdout, &stderr)
	if code != 0 {
		t.Fatalf("list --help failed: code=%d stderr=%s", code, stderr.String())
	}

	const expected = "Usage:\n" +
		"  uloop list [options]\n" +
		"\n" +
		"Show Unity tools currently exposed by the Editor\n" +
		"\n" +
		"Options:\n" +
		"  --names                            Show command names only, one per line\n" +
		"\n" +
		"Global options:\n" +
		"  --project-path <path>   Run against a Unity project outside the current directory\n"
	if stdout.String() != expected {
		t.Fatalf("list --help output mismatch:\n got:\n%s\nwant:\n%s", stdout.String(), expected)
	}
}

func assertNativeCommandHelpOptionsSection(t *testing.T, command string, expectedOptions string) {
	t.Helper()
	t.Chdir(t.TempDir())

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunProjectLocal(context.Background(), []string{command, "--help"}, &stdout, &stderr)
	if code != 0 {
		t.Fatalf("%s --help failed: code=%d stderr=%s", command, code, stderr.String())
	}

	actual, ok := nativeCommandHelpOptionsSection(stdout.String())
	if !ok {
		t.Fatalf("%s --help has no Options section: %s", command, stdout.String())
	}
	if actual != expectedOptions {
		t.Fatalf("%s --help Options section mismatch:\n got:\n%s\nwant:\n%s", command, actual, expectedOptions)
	}
}

func nativeCommandHelpOptionsSection(output string) (string, bool) {
	// Why not compare stdout as-is: Windows test hosts can surface CRLF even though the
	// writer emits \n, and a golden that only matches LF would fail there for an unrelated reason.
	normalized := strings.ReplaceAll(output, "\r\n", "\n")
	const header = "Options:\n"
	start := strings.Index(normalized, header)
	if start < 0 {
		return "", false
	}
	rest := normalized[start:]
	end := strings.Index(rest, "\nGlobal options:")
	if end < 0 {
		return "", false
	}
	return rest[:end], true
}

// Verifies runner-owned pause-point commands close their help with the instruction to load the
// pause-point skill. The dispatcher never renders these commands' help, so its own closing line
// cannot reach them.
func TestRunProjectLocalPausePointHelpPointsAtTheSkill(t *testing.T) {
	for _, command := range []string{"await-pause-point", "pause-point-status"} {
		t.Run(command, func(t *testing.T) {
			t.Chdir(t.TempDir())

			var stdout bytes.Buffer
			var stderr bytes.Buffer
			code := RunProjectLocal(context.Background(), []string{command, "--help"}, &stdout, &stderr)

			if code != 0 {
				t.Fatalf("%s --help failed: code=%d stderr=%s", command, code, stderr.String())
			}
			if !strings.Contains(stdout.String(), "uloop-pause-point skill") {
				t.Fatalf("%s --help must point at the pause-point skill: %s", command, stdout.String())
			}
		})
	}
}

// Verifies sync/focus-window --help print only the global --project-path
// option, since these commands take no command-specific flags.
func TestRunProjectLocalNoOptionCommandsHelpListsOnlyGlobalOption(t *testing.T) {
	for _, command := range []string{"sync", "focus-window"} {
		t.Run(command, func(t *testing.T) {
			t.Chdir(t.TempDir())

			var stdout bytes.Buffer
			var stderr bytes.Buffer
			code := RunProjectLocal(context.Background(), []string{command, "--help"}, &stdout, &stderr)

			if code != 0 {
				t.Fatalf("%s --help failed: code=%d stderr=%s", command, code, stderr.String())
			}
			if !strings.Contains(stdout.String(), "--project-path") {
				t.Fatalf("%s --help must list the global --project-path option: %s", command, stdout.String())
			}
			if strings.Contains(stdout.String(), "Options:") {
				t.Fatalf("%s --help must not print a command-specific Options section: %s", command, stdout.String())
			}
		})
	}
}
