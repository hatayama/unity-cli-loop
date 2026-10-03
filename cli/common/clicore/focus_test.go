package clicore

import (
	"bytes"
	"context"
	"encoding/json"
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

// Verifies that focus-window fails with a JSON error on stderr when the process lookup fails or finds
// no Unity process, and never attempts to focus.
func TestRunFocusWindowReportsLookupFailures(t *testing.T) {
	cases := []struct {
		name            string
		process         *UnityProcess
		lookupError     error
		expectedMessage string
	}{
		{name: "lookup error", lookupError: fmt.Errorf("ps failed"), expectedMessage: "ps failed"},
		{name: "no process", expectedMessage: "No running Unity process found for this project"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			deps := focusWindowDeps{
				findRunningUnityProcess: func(context.Context, string) (*UnityProcess, error) {
					return testCase.process, testCase.lookupError
				},
				focusUnityProcess: func(context.Context, int) error {
					t.Fatal("focus must not be attempted")
					return nil
				},
			}
			var stdout bytes.Buffer
			var stderr bytes.Buffer

			code := runFocusWindow(context.Background(), t.TempDir(), &stdout, &stderr, deps)

			assertFocusFailure(t, code, stdout.String(), stderr.Bytes(), testCase.expectedMessage)
		})
	}
}

func assertFocusFailure(t *testing.T, code int, stdout string, stderr []byte, expectedMessage string) {
	t.Helper()
	if code != 1 || stdout != "" {
		t.Fatalf("expected exit 1 with no stdout, got %d and %q", code, stdout)
	}
	var response focusResponse
	if err := json.Unmarshal(stderr, &response); err != nil {
		t.Fatalf("stderr is not a JSON response: %v (%q)", err, string(stderr))
	}
	if response.Success || response.Message != expectedMessage {
		t.Fatalf("unexpected focus response: %#v", response)
	}
}
