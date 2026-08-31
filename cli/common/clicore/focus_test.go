package clicore

import (
	"bytes"
	"context"
	"fmt"
	"strings"
	"testing"
)

// Verifies focus-window persists successful focus attempts to CLI Vibe logs.
func TestRunFocusWindowWritesFocusSuccessVibeLog(t *testing.T) {
	enableCliVibeLog(t)

	deps := focusWindowDeps{
		findRunningUnityProcess: func(context.Context, string) (*UnityProcess, error) {
			return &UnityProcess{Pid: 321}, nil
		},
		focusUnityProcess: func(context.Context, int) error {
			return nil
		},
	}

	projectRoot := t.TempDir()
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runFocusWindow(context.Background(), projectRoot, &stdout, &stderr, deps)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	for _, expected := range []string{
		`"operation":"cli_focus_window_focus_attempt"`,
		`"operation":"cli_focus_window_focus_success"`,
		`"command":"focus-window"`,
		`"pid":321`,
	} {
		if !strings.Contains(logContent, expected) {
			t.Fatalf("CLI Vibe log missing %q:\n%s", expected, logContent)
		}
	}
}

// Verifies focus-window persists failed focus attempts to CLI Vibe logs.
func TestRunFocusWindowWritesFocusFailureVibeLog(t *testing.T) {
	enableCliVibeLog(t)

	deps := focusWindowDeps{
		findRunningUnityProcess: func(context.Context, string) (*UnityProcess, error) {
			return &UnityProcess{Pid: 654}, nil
		},
		focusUnityProcess: func(context.Context, int) error {
			return fmt.Errorf("window denied")
		},
	}

	projectRoot := t.TempDir()
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runFocusWindow(context.Background(), projectRoot, &stdout, &stderr, deps)

	if code != 1 {
		t.Fatalf("exit code mismatch: %d stdout=%s", code, stdout.String())
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	for _, expected := range []string{
		`"operation":"cli_focus_window_focus_attempt"`,
		`"operation":"cli_focus_window_focus_failed"`,
		`"command":"focus-window"`,
		`"pid":654`,
		`"focusError":"window denied"`,
	} {
		if !strings.Contains(logContent, expected) {
			t.Fatalf("CLI Vibe log missing %q:\n%s", expected, logContent)
		}
	}
}
