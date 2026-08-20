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
	for _, flag := range []string{"--id", "--timeout-seconds", "--matching-logs-max-count", "--captured-variables", "--expect", "--trigger", "--resume-play"} {
		if !strings.Contains(stdout.String(), flag) {
			t.Fatalf("await-pause-point --help must list %s: %s", flag, stdout.String())
		}
	}
	if !strings.Contains(stdout.String(), "--project-path") {
		t.Fatalf("await-pause-point --help must list the global --project-path option: %s", stdout.String())
	}
}

// Verifies await-pause-point --help prints option descriptions, not names alone. The sentence is a
// test-local literal so a renderer that drops the description column still fails even if the
// tooldocs table is complete.
func TestRunProjectLocalAwaitPausePointHelpDescribesTrigger(t *testing.T) {
	t.Chdir(t.TempDir())

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunProjectLocal(context.Background(), []string{"await-pause-point", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("await-pause-point --help failed: code=%d stderr=%s", code, stderr.String())
	}
	const expectedTriggerDescription = "Runs a single uloop subcommand in-process right after arming/registration"
	if !strings.Contains(stdout.String(), expectedTriggerDescription) {
		t.Fatalf("await-pause-point --help must describe --trigger: %s", stdout.String())
	}
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
	for _, flag := range []string{"--id", "--captured-variables"} {
		if !strings.Contains(stdout.String(), flag) {
			t.Fatalf("pause-point-status --help must list %s: %s", flag, stdout.String())
		}
	}
	if strings.Contains(stdout.String(), "--timeout-seconds") {
		t.Fatalf("pause-point-status --help must not list await-pause-point-only flags: %s", stdout.String())
	}
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

// Verifies list/sync/focus-window --help print only the global --project-path
// option, since these commands take no command-specific flags.
func TestRunProjectLocalNoOptionCommandsHelpListsOnlyGlobalOption(t *testing.T) {
	for _, command := range []string{"list", "sync", "focus-window"} {
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
