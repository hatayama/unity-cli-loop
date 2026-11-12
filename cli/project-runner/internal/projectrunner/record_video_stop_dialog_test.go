package projectrunner

import (
	"context"
	"strings"
	"testing"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies a dialog that keeps the one-shot's stop from running is reported as a recording that
// started and goes on, not as a record-video that was not run: retrying would start a second
// recording, so it is not safe to retry, and the last step says how to stop the one that runs.
// Covers both forms a stop can meet: the dialog found while waiting for the server (not sent) and
// Unity's main_thread_blocked answer (not run).
func TestRunRecordVideoOneShotReportsTheRunningRecordingWhenADialogBlocksStop(t *testing.T) {
	dialogs := []statusDialog{commandDialogPlayerStoppedDialog()}
	cases := []struct {
		name string
		err  error
	}{
		{name: "not sent", err: notSentBlockedByDialog(tooldocs.RecordVideoCommandName, dialogs, true)},
		{name: "not run", err: blockedByDialogError{
			command:  tooldocs.RecordVideoCommandName,
			dialogs:  dialogs,
			canPress: true,
			notRun:   commandDialogMainThreadBlockedRPCError(tooldocs.RecordVideoCommandName),
		}},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			fake := newRecordVideoOneShotFake()
			fake.install(t)
			sendRecordVideoRequest = func(context.Context, unityipc.Connection, []string, string) (toolExecutionResult, error) {
				fake.calls = append(fake.calls, "stop")
				return toolExecutionResult{exitCode: 1}, testCase.err
			}

			run := runRecordVideoOneShotForTest(context.Background(), []string{"--seconds", "3"})

			requireRecordVideoCalls(t, fake, "focus", "start", "wait", "stop")
			if run.code != 1 || run.stdout != "" {
				t.Fatalf("run = %+v, want exit 1 with nothing on stdout", run)
			}
			cliError := hotReloadErrorEnvelope(t, run.stderr).Error
			if cliError.ErrorCode != clierrors.ErrorCodeBlockedByDialog || cliError.SafeToRetry {
				t.Fatalf("ErrorCode = %q, SafeToRetry = %v, want BLOCKED_BY_DIALOG, not safe to retry",
					cliError.ErrorCode, cliError.SafeToRetry)
			}
			if strings.Contains(cliError.Message, "was not run") || !strings.Contains(cliError.Message, "recording") {
				t.Fatalf("Message = %q, want it to say the recording started, not that nothing ran", cliError.Message)
			}
			if last := cliError.NextActions[len(cliError.NextActions)-1]; !strings.Contains(last, "uloop record-video --action stop") {
				t.Fatalf("last NextAction = %q, want the step that stops the recording", last)
			}
			if _, present := cliError.Details["Dialogs"]; !present {
				t.Fatalf("Details = %#v, want Dialogs", cliError.Details)
			}
		})
	}
}
