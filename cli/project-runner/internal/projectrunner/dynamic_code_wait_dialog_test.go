package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

const dynamicCodeReloadRequiredResponse = `{"jsonrpc":"2.0","result":{"Success":true,"Result":"1","DomainReloadWaitRequired":true},"id":1}`

// dynamicCodeAcceptedThenLostResponse is Unity accepting the snippet and then dropping the connection,
// as when the snippet starts a domain reload.
const dynamicCodeAcceptedThenLostResponse = `{"jsonrpc":"2.0","id":1,"uloop":{"phase":"accepted"}}`

// runDynamicCodeReloadWait sends a snippet to a fake Unity that answers with response, and waits for
// the reload with wait.
func runDynamicCodeReloadWait(
	t *testing.T,
	response string,
	wait func(context.Context, string, clicore.DialogFinder) error,
	dialogWatch dialogWatchDeps,
) (int, string) {
	t.Helper()
	// Why: the send recovers a lost connection by asking whether Unity still runs.
	useRunningUnityProcess(t)
	server := startFakeUnityServer(t, writeFakeUnityProject(t), clicore.ExecuteDynamicCodeCommandName, response)
	var stderr bytes.Buffer
	deps := dynamicCodeWaitDeps{waitForToolReadiness: wait, dialogWatch: dialogWatch}

	code := runExecuteDynamicCodeWithDomainReloadWaitWithDeps(
		context.Background(),
		server.connection,
		map[string]any{"Code": "return 1;", clicore.DomainReloadWaitParam: true},
		io.Discard,
		&stderr,
		deps,
	)
	server.receivedRequest(t)
	return code, stderr.String()
}

func decodeDynamicCodeError(t *testing.T, stderr string) clierrors.CLIError {
	t.Helper()
	var envelope clierrors.CLIErrorEnvelope
	if err := json.Unmarshal([]byte(strings.TrimSpace(stderr)), &envelope); err != nil {
		t.Fatalf("stderr is not a JSON error envelope: %v\n%s", err, stderr)
	}
	return envelope.Error
}

func blockedReloadWait(context.Context, string, clicore.DialogFinder) error {
	return clicore.ToolReadinessBlockedByDialogError{
		Dialogs: []statusDialog{commandDialogPlayerStoppedDialog()},
		Cause:   errors.New("connection refused"),
	}
}

// Verifies a reload wait stopped at a dialog, after the snippet's connection dropped or after the
// snippet returned and asked for the wait, reports BLOCKED_BY_DIALOG in the neutral form: it does not
// say whether the snippet ran, is not safe to retry, and keeps the phase and endpoint of a server
// that does not answer (#14, #15).
func TestExecuteDynamicCodeReloadWaitReportsADialogInTheNeutralForm(t *testing.T) {
	cases := map[string]string{
		"connection lost after the snippet was accepted": dynamicCodeAcceptedThenLostResponse,
		"snippet returned and asked for the wait":        dynamicCodeReloadRequiredResponse,
	}
	for name, response := range cases {
		t.Run(name, func(t *testing.T) {
			// Why a look that would find nothing: the wait's own answer must be what is reported.
			code, stderr := runDynamicCodeReloadWait(t, response, blockedReloadWait, newFakeDialogLook().deps())

			cliError := decodeDynamicCodeError(t, stderr)
			if code != 1 || cliError.ErrorCode != clierrors.ErrorCodeBlockedByDialog {
				t.Fatalf("exit code = %d, ErrorCode = %q, want 1 and %s\n%s", code, cliError.ErrorCode, clierrors.ErrorCodeBlockedByDialog, stderr)
			}
			wantMessage := blockedByDialogMessage([]statusDialog{commandDialogPlayerStoppedDialog()}) +
				" uloop execute-dynamic-code stopped waiting."
			if cliError.Message != wantMessage || cliError.SafeToRetry || cliError.Phase != clierrors.ErrorPhaseConnection {
				t.Fatalf("Message = %q, SafeToRetry = %v, Phase = %q, want the neutral form in the connection phase, not safe to retry",
					cliError.Message, cliError.SafeToRetry, cliError.Phase)
			}
			if endpoint, _ := cliError.Details["Endpoint"].(string); endpoint == "" {
				t.Fatalf("Details.Endpoint = %#v, want the endpoint the wait polled", cliError.Details["Endpoint"])
			}
			if last := cliError.NextActions[len(cliError.NextActions)-1]; last != startedCommandDialogClosedNextAction {
				t.Fatalf("last step = %q, want %q", last, startedCommandDialogClosedNextAction)
			}
		})
	}
}

// Verifies a reload wait that timed out with no dialog open reports what it reports today (#16).
func TestExecuteDynamicCodeReloadWaitKeepsATimeoutWithoutADialog(t *testing.T) {
	timedOut := clierrors.UnityServerNotRespondingError{ProjectRoot: "<PROJECT_ROOT>", Cause: errors.New("connection refused")}
	wait := func(context.Context, string, clicore.DialogFinder) error { return timedOut }

	_, stderr := runDynamicCodeReloadWait(t, dynamicCodeReloadRequiredResponse, wait, newFakeDialogLook().deps())

	cliError := decodeDynamicCodeError(t, stderr)
	want := clierrors.ClassifyError(timedOut, clierrors.ErrorContext{Command: clicore.ExecuteDynamicCodeCommandName})
	if cliError.ErrorCode != want.ErrorCode || cliError.Message != want.Message {
		t.Fatalf("error = %q %q, want %q %q", cliError.ErrorCode, cliError.Message, want.ErrorCode, want.Message)
	}
}

// Verifies the reload wait is handed the look uloop status uses, which reads the Editor's own report,
// so a dialog it finds during the wait is the one reported (#16b).
func TestExecuteDynamicCodeReloadWaitLooksLikeUloopStatus(t *testing.T) {
	cases := map[string]string{
		"connection lost after the snippet was accepted": dynamicCodeAcceptedThenLostResponse,
		"snippet returned and asked for the wait":        dynamicCodeReloadRequiredResponse,
	}
	for name, response := range cases {
		t.Run(name, func(t *testing.T) {
			look := newFakeDialogLook(commandDialogPlayerStoppedDialog())
			// Why the dialogs the wait reads are recorded: the explanation after the wait looks again
			// and would report the dialog even when the wait had no look of its own.
			var seenByTheWait []statusDialog
			wait := func(ctx context.Context, _ string, findDialogs clicore.DialogFinder) error {
				if findDialogs == nil {
					return errors.New("no dialog look was handed to the wait")
				}
				dialogs, err := findDialogs(ctx)
				seenByTheWait = dialogs
				if err != nil || len(dialogs) == 0 {
					return errors.New("the look found no dialog")
				}
				return clicore.ToolReadinessBlockedByDialogError{Dialogs: dialogs}
			}

			_, stderr := runDynamicCodeReloadWait(t, response, wait, look.deps())

			cliError := decodeDynamicCodeError(t, stderr)
			if cliError.ErrorCode != clierrors.ErrorCodeBlockedByDialog || len(seenByTheWait) != 1 {
				t.Fatalf("ErrorCode = %q, the wait saw %#v, want %s and the one dialog\n%s",
					cliError.ErrorCode, seenByTheWait, clierrors.ErrorCodeBlockedByDialog, stderr)
			}
		})
	}
}
