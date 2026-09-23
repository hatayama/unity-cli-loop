package clierrors

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const expectedMainThreadWaitBusyMessage = "'get-logs' was not executed because 'execute-dynamic-code' holds Unity's single-flight slot but is not running tool code: it is waiting for the Editor main thread, which has not responded for 42s (e.g. a synchronous asset refresh or script compilation). No uloop command can run until the main thread resumes. Wait for the Editor to become responsive; restart with `uloop launch -r` only if it stays unresponsive for several minutes."

// Verifies a busy rejection whose holder only waits for a main thread stalled past the threshold
// says the holder is waiting, not running, and names the stall length.
func TestClassifyServerBusyRPCError_WhenHolderWaitsForStalledMainThread_ReportsMainThreadWait(t *testing.T) {
	err := &unityipc.RPCError{
		Code:    -32603,
		Message: "Unity is busy running 'execute-dynamic-code'. Retry 'get-logs' after the running tool completes.",
		Data: json.RawMessage(
			`{"type":"server_busy","runningToolName":"execute-dynamic-code","requestedToolName":"get-logs","secondsSinceLastMainThreadTick":42.7,"runningToolElapsedSeconds":45,"runningToolPhase":"WaitingForMainThread"}`),
	}

	cliErr := ClassifyError(err, ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "get-logs"})

	if cliErr.Message != expectedMainThreadWaitBusyMessage {
		t.Fatalf("message mismatch: %s", cliErr.Message)
	}
	if cliErr.ErrorCode != errorCodeUnityServerBusy {
		t.Fatalf("error code mismatch: %s", cliErr.ErrorCode)
	}
}

// Verifies the main-thread wait guidance tells the caller to wait for the Editor and to restart
// only after a long stall, and repeats no retry claim the CLI may not have made.
func TestUnityServerBusyNextActions_WhenHolderWaitsForStalledMainThread_GuidesWaitingForEditor(t *testing.T) {
	stallSeconds := 42.0
	data := serverBusyErrorData{
		RunningToolName:                "execute-dynamic-code",
		SecondsSinceLastMainThreadTick: &stallSeconds,
		RunningToolPhase:               "WaitingForMainThread",
	}

	actions := unityServerBusyNextActions(data)

	expected := []string{
		"'execute-dynamic-code' is waiting for the Editor main thread, not running tool code, so waiting for it to finish does not help on its own.",
		"Wait for the Editor to become responsive (a synchronous asset refresh or script compilation can block it for minutes), then run the command again.",
		"Restart with `uloop launch -r` only if the Editor stays unresponsive for several minutes.",
	}
	if strings.Join(actions, "\n") != strings.Join(expected, "\n") {
		t.Fatalf("actions mismatch: %#v", actions)
	}
	for _, action := range actions {
		if strings.Contains(action, "retried") {
			t.Fatalf("main-thread wait guidance must not claim a retry: %#v", actions)
		}
	}
}

// Verifies the main-thread wait wording is used only when the holder waits and the stall passed
// the threshold; other combinations keep the running-tool wording.
func TestUnityServerBusyMessage_WhenMainThreadWaitIsNotEstablished_KeepsRunningToolWording(t *testing.T) {
	shortStall := 2.0
	longStall := 42.0
	cases := map[string]serverBusyErrorData{
		"waiting but main thread ticking": {
			RunningToolName:                "execute-dynamic-code",
			SecondsSinceLastMainThreadTick: &shortStall,
			RunningToolPhase:               "WaitingForMainThread",
		},
		"executing during stall": {
			RunningToolName:                "execute-dynamic-code",
			SecondsSinceLastMainThreadTick: &longStall,
			RunningToolPhase:               "Executing",
		},
		"phase unknown during stall": {
			RunningToolName:                "execute-dynamic-code",
			SecondsSinceLastMainThreadTick: &longStall,
		},
		"waiting without tick": {
			RunningToolName:  "execute-dynamic-code",
			RunningToolPhase: "WaitingForMainThread",
		},
	}

	for name, data := range cases {
		t.Run(name, func(t *testing.T) {
			message := unityServerBusyMessage("fallback", data, "get-logs")
			if strings.Contains(message, "waiting for the Editor main thread") {
				t.Fatalf("expected running-tool wording, got: %s", message)
			}
			for _, action := range unityServerBusyNextActions(data) {
				if strings.Contains(action, "waiting for the Editor main thread") {
					t.Fatalf("expected running-tool guidance, got: %#v", action)
				}
			}
		})
	}
}
