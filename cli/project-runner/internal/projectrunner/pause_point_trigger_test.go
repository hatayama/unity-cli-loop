package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies the quote-aware tokenizer splits a --trigger value into argv-style tokens, including
// values that contain a space when quoted, and rejects an unterminated quote.
func TestTokenizePausePointTriggerValue(t *testing.T) {
	cases := []struct {
		name    string
		raw     string
		want    []string
		wantErr bool
	}{
		{
			name: "plain unquoted tokens",
			raw:  "simulate-keyboard --action Press --key Space --duration 5",
			want: []string{"simulate-keyboard", "--action", "Press", "--key", "Space", "--duration", "5"},
		},
		{
			name: "double-quoted value with a space",
			raw:  `simulate-mouse-input --action Move --position "10 20"`,
			want: []string{"simulate-mouse-input", "--action", "Move", "--position", "10 20"},
		},
		{
			name: "single-quoted value with a space",
			raw:  `simulate-mouse-input --action Move --position '10 20'`,
			want: []string{"simulate-mouse-input", "--action", "Move", "--position", "10 20"},
		},
		{
			name:    "unterminated quote",
			raw:     `simulate-keyboard --key "Space`,
			wantErr: true,
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			got, err := tokenizePausePointTriggerValue(testCase.raw)
			if testCase.wantErr {
				if err == nil {
					t.Fatalf("expected an error, got tokens: %#v", got)
				}
				return
			}
			if err != nil {
				t.Fatalf("unexpected error: %v", err)
			}
			if len(got) != len(testCase.want) {
				t.Fatalf("token count mismatch: got %#v, want %#v", got, testCase.want)
			}
			for index, token := range got {
				if token != testCase.want[index] {
					t.Fatalf("token[%d] mismatch: got %q, want %q", index, token, testCase.want[index])
				}
			}
		})
	}
}

// Verifies --trigger parsing accepts a plain command and rejects the shapes that cannot behave
// sensibly when dispatched from inside a pause-point wait: another pause-point wait command, and
// an explicit --project-path override.
func TestParsePausePointTriggerCommand(t *testing.T) {
	cases := []struct {
		name        string
		value       string
		wantCommand string
		wantArgs    []string
		wantErrText string
	}{
		{
			name:        "plain trigger command",
			value:       "simulate-keyboard --action Press --key Space --duration 5",
			wantCommand: "simulate-keyboard",
			wantArgs:    []string{"--action", "Press", "--key", "Space", "--duration", "5"},
		},
		{
			name:        "empty value is missing",
			value:       "",
			wantErrText: "requires a value",
		},
		{
			name:        "rejects await-pause-point as trigger target",
			value:       "await-pause-point --id other",
			wantErrText: "cannot make progress",
		},
		{
			name:        "rejects enable-pause-point as trigger target",
			value:       "enable-pause-point --id other",
			wantErrText: "cannot make progress",
		},
		{
			name:        "rejects an explicit --project-path",
			value:       "simulate-keyboard --action Press --project-path /tmp/OtherProject",
			wantErrText: "cannot include --project-path",
		},
		{
			name:        "rejects --project-path= form",
			value:       "simulate-keyboard --action Press --project-path=/tmp/OtherProject",
			wantErrText: "cannot include --project-path",
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			command, args, err := parsePausePointTriggerCommand("await-pause-point", testCase.value)
			if testCase.wantErrText != "" {
				if err == nil {
					t.Fatalf("expected an error containing %q", testCase.wantErrText)
				}
				if !strings.Contains(err.Error(), testCase.wantErrText) {
					t.Fatalf("error mismatch: got %v, want contains %q", err, testCase.wantErrText)
				}
				return
			}
			if err != nil {
				t.Fatalf("unexpected error: %v", err)
			}
			if command != testCase.wantCommand {
				t.Fatalf("command mismatch: got %q, want %q", command, testCase.wantCommand)
			}
			if len(args) != len(testCase.wantArgs) {
				t.Fatalf("args mismatch: got %#v, want %#v", args, testCase.wantArgs)
			}
			for index, arg := range args {
				if arg != testCase.wantArgs[index] {
					t.Fatalf("args[%d] mismatch: got %q, want %q", index, arg, testCase.wantArgs[index])
				}
			}
		})
	}
}

// Verifies runPausePointTriggerSync passes a successfully dispatched trigger command's raw JSON
// response through untouched, and reports a dispatch-level failure (unparseable output) as Error
// instead, falling back to a synthesized message when the dispatched command wrote nothing to
// stderr.
func TestRunPausePointTriggerSync(t *testing.T) {
	originalDispatch := dispatchPausePointTriggerCommand
	defer func() { dispatchPausePointTriggerCommand = originalDispatch }()

	t.Run("passes through a successful trigger response", func(t *testing.T) {
		dispatchPausePointTriggerCommand = func(
			ctx context.Context,
			connection unityipc.Connection,
			command string,
			commandArgs []string,
			startPath string,
			stdout io.Writer,
			stderr io.Writer,
		) int {
			_, _ = stdout.Write([]byte(`{"Success":true,"InterruptedByPausePoint":true}`))
			return 0
		}

		result := runPausePointTriggerSync(
			context.Background(), unityipc.Connection{}, "", "simulate-keyboard", []string{"--action", "Press"},
			"simulate-keyboard --action Press")

		if !result.Completed {
			t.Fatalf("expected Completed=true, got %#v", result)
		}
		if result.Error != "" {
			t.Fatalf("expected no error, got %#v", result)
		}
		var response map[string]any
		if err := json.Unmarshal(result.Response, &response); err != nil {
			t.Fatalf("Response is not valid JSON: %v (%#v)", err, result)
		}
		if response["InterruptedByPausePoint"] != true {
			t.Fatalf("response passthrough mismatch: %#v", response)
		}
	})

	t.Run("reports the dispatched command's stderr on dispatch failure", func(t *testing.T) {
		dispatchPausePointTriggerCommand = func(
			ctx context.Context,
			connection unityipc.Connection,
			command string,
			commandArgs []string,
			startPath string,
			stdout io.Writer,
			stderr io.Writer,
		) int {
			_, _ = stderr.Write([]byte("unknown command: bogus-command"))
			return 1
		}

		result := runPausePointTriggerSync(
			context.Background(), unityipc.Connection{}, "", "bogus-command", nil, "bogus-command")

		if !result.Completed {
			t.Fatalf("expected Completed=true (the dispatch itself returned), got %#v", result)
		}
		if result.Response != nil {
			t.Fatalf("expected no Response on failure, got %#v", result)
		}
		if result.Error != "unknown command: bogus-command" {
			t.Fatalf("error mismatch: %#v", result)
		}
	})

	t.Run("synthesizes an error when the dispatched command wrote nothing at all", func(t *testing.T) {
		dispatchPausePointTriggerCommand = func(
			ctx context.Context,
			connection unityipc.Connection,
			command string,
			commandArgs []string,
			startPath string,
			stdout io.Writer,
			stderr io.Writer,
		) int {
			return 1
		}

		result := runPausePointTriggerSync(
			context.Background(), unityipc.Connection{}, "", "bogus-command", nil, "bogus-command")

		if result.Error == "" || !strings.Contains(result.Error, "exited with code 1") {
			t.Fatalf("expected a synthesized error message, got %#v", result)
		}
	})
}

// Verifies waitForPausePoint embeds the trigger result once the trigger completes before the
// pause-point wait settles, and reports Completed=false (without blocking further) when the
// trigger is still running after the short join grace window.
func TestWaitForPausePointJoinsTriggerResult(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	defer func() {
		queryPausePointStatus = originalQuery
		dispatchPausePointTriggerCommand = originalDispatch
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}

	t.Run("trigger completes before the wait settles", func(t *testing.T) {
		dispatchPausePointTriggerCommand = func(
			ctx context.Context,
			connection unityipc.Connection,
			command string,
			commandArgs []string,
			startPath string,
			stdout io.Writer,
			stderr io.Writer,
		) int {
			_, _ = stdout.Write([]byte(`{"Success":true}`))
			return 0
		}

		_, _, triggerResult, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
			id:             "jump",
			timeoutSeconds: 1,
			timeout:        time.Second,
			triggerCommand: "simulate-keyboard",
			triggerArgs:    []string{"--action", "Press"},
		})
		if err != nil {
			t.Fatalf("waitForPausePoint failed: %v", err)
		}
		if triggerResult == nil || !triggerResult.Completed {
			t.Fatalf("expected a completed trigger result, got %#v", triggerResult)
		}
		if triggerResult.Command != "simulate-keyboard --action Press" {
			t.Fatalf("command mismatch: %#v", triggerResult)
		}
	})

	t.Run("trigger still running past the join grace window reports Completed=false", func(t *testing.T) {
		release := make(chan struct{})
		t.Cleanup(func() { close(release) })

		dispatchPausePointTriggerCommand = func(
			ctx context.Context,
			connection unityipc.Connection,
			command string,
			commandArgs []string,
			startPath string,
			stdout io.Writer,
			stderr io.Writer,
		) int {
			<-release
			_, _ = stdout.Write([]byte(`{"Success":true}`))
			return 0
		}

		_, _, triggerResult, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
			id:             "jump",
			timeoutSeconds: 1,
			timeout:        time.Second,
			triggerCommand: "simulate-keyboard",
			triggerArgs:    []string{"--action", "Press"},
		})
		if err != nil {
			t.Fatalf("waitForPausePoint failed: %v", err)
		}
		if triggerResult == nil || triggerResult.Completed {
			t.Fatalf("expected an in-flight (not completed) trigger result, got %#v", triggerResult)
		}
		if triggerResult.Command != "simulate-keyboard --action Press" {
			t.Fatalf("command mismatch: %#v", triggerResult)
		}
	})
}

// Verifies a --trigger result is embedded in the timeout error envelope's Details, so a caller
// can see what the trigger command did even when the pause-point itself was never hit.
func TestRunWaitForPausePointEmbedsTriggerResultOnTimeout(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalClear := clearPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		clearPausePointStatus = originalClear
		dispatchPausePointTriggerCommand = originalDispatch
		pausePointStatusPoll = originalPoll
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusEnabled,
			IsEnabled:   true,
			EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		}, nil
	}
	clearPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Id: id, Status: pausePointStatusCleared}, nil
	}
	dispatchPausePointTriggerCommand = func(
		ctx context.Context,
		connection unityipc.Connection,
		command string,
		commandArgs []string,
		startPath string,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		_, _ = stdout.Write([]byte(`{"Success":true}`))
		return 0
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        5 * time.Millisecond,
		triggerCommand: "simulate-keyboard",
		triggerArgs:    []string{"--action", "Press"},
	}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected timeout failure, got %d with stdout %s", code, stdout.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	triggerResult, ok := envelope.Error.Details["TriggerResult"].(map[string]any)
	if !ok {
		t.Fatalf("TriggerResult detail missing or wrong shape: %#v", envelope.Error.Details)
	}
	if triggerResult["Command"] != "simulate-keyboard --action Press" {
		t.Fatalf("TriggerResult command mismatch: %#v", triggerResult)
	}
	if triggerResult["Completed"] != true {
		t.Fatalf("TriggerResult should report Completed=true: %#v", triggerResult)
	}
}

// Verifies await-pause-point parses --trigger into the command/args pair and rejects a value that
// targets another pause-point wait.
func TestParseWaitForPausePointOptionsParsesTriggerFlag(t *testing.T) {
	options, err := parseWaitForPausePointOptions([]string{
		"--id", "jump", "--trigger", "simulate-keyboard --action Press --key Space --duration 5",
	})
	if err != nil {
		t.Fatalf("parse failed: %v", err)
	}
	if options.triggerCommand != "simulate-keyboard" {
		t.Fatalf("trigger command mismatch: %#v", options)
	}
	wantArgs := []string{"--action", "Press", "--key", "Space", "--duration", "5"}
	if len(options.triggerArgs) != len(wantArgs) {
		t.Fatalf("trigger args mismatch: %#v", options)
	}
	for index, arg := range options.triggerArgs {
		if arg != wantArgs[index] {
			t.Fatalf("trigger args[%d] mismatch: got %q, want %q", index, arg, wantArgs[index])
		}
	}

	if _, err := parseWaitForPausePointOptions([]string{
		"--id", "jump", "--trigger", "await-pause-point --id other",
	}); err == nil {
		t.Fatalf("expected an error for a trigger targeting another pause-point wait")
	}
}

// Verifies enable-pause-point extracts --trigger alongside --await, and rejects --trigger without
// --await since it would otherwise have no effect.
func TestExtractPausePointEnableAwaitFlagsExtractsTrigger(t *testing.T) {
	remaining, await, _, _, _, triggerCommand, triggerArgs, err := extractPausePointEnableAwaitFlags([]string{
		"--id", "jump", "--await", "--trigger", "simulate-keyboard --action Press --key Space --duration 5",
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if !await {
		t.Fatalf("expected await to be true")
	}
	if triggerCommand != "simulate-keyboard" {
		t.Fatalf("trigger command mismatch: %#v", triggerCommand)
	}
	wantArgs := []string{"--action", "Press", "--key", "Space", "--duration", "5"}
	if len(triggerArgs) != len(wantArgs) {
		t.Fatalf("trigger args mismatch: %#v", triggerArgs)
	}
	for index, arg := range triggerArgs {
		if arg != wantArgs[index] {
			t.Fatalf("trigger args[%d] mismatch: got %q, want %q", index, arg, wantArgs[index])
		}
	}
	if len(remaining) != 2 || remaining[0] != "--id" || remaining[1] != "jump" {
		t.Fatalf("remaining args mismatch: %#v", remaining)
	}
}

// Verifies --trigger without --await is rejected, since it has no effect otherwise.
func TestExtractPausePointEnableAwaitFlagsRequiresAwaitForTrigger(t *testing.T) {
	_, _, _, _, _, _, _, err := extractPausePointEnableAwaitFlags([]string{
		"--id", "jump", "--trigger", "simulate-keyboard --action Press",
	})
	if err == nil {
		t.Fatalf("expected an error")
	}
	if !strings.Contains(err.Error(), "require --await") {
		t.Fatalf("error message mismatch: %v", err)
	}
}
