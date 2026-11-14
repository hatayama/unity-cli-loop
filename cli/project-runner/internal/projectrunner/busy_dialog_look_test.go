package projectrunner

import (
	"context"
	"encoding/json"
	"errors"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// busyRPCError is a busy answer whose data carries the given fields of the request holding the slot.
func busyRPCError(t *testing.T, fields map[string]any) error {
	t.Helper()
	data := map[string]any{"type": "server_busy", "runningToolName": "compile"}
	for name, value := range fields {
		data[name] = value
	}
	encoded, err := json.Marshal(data)
	if err != nil {
		t.Fatalf("encode busy data: %v", err)
	}
	return &unityipc.RPCError{Code: -32603, Message: "busy", Data: encoded}
}

func waitingHolderBusy(t *testing.T, stallSeconds float64) error {
	return busyRPCError(t, map[string]any{"runningToolPhase": "WaitingForMainThread", "secondsSinceLastMainThreadTick": stallSeconds})
}

// countingDialogProbe answers every look with the given dialogs and counts the looks.
type countingDialogProbe struct {
	dialogs []statusDialog
	looks   int
}

func (probe *countingDialogProbe) probe(context.Context, unityipc.Connection) (statusReport, error) {
	probe.looks++
	if len(probe.dialogs) == 0 {
		return newStatusReport(statusStateBusy, "busy"), nil
	}
	return blockedByDialogStatusReport(probe.dialogs...), nil
}

// Verifies a busy answer whose slot holder waits for a main thread still for a second looks once and
// returns the dialog it finds.
func TestBusyDialogLookFindsADialogBehindAWaitingHolder(t *testing.T) {
	probe := &countingDialogProbe{dialogs: []statusDialog{commandDialogPlayerStoppedDialog()}}
	look := busyDialogLook{}

	dialogs := look.observe(context.Background(), unityipc.Connection{}, waitingHolderBusy(t, 3.5), probe.probe)

	if len(dialogs) != 1 || probe.looks != 1 {
		t.Fatalf("dialogs = %v, looks = %d, want the dialog after one look", dialogs, probe.looks)
	}
}

// Verifies a look that finds no dialog is not repeated for later busy answers of the same send.
func TestBusyDialogLookLooksOnlyOncePerSend(t *testing.T) {
	probe := &countingDialogProbe{}
	look := busyDialogLook{}

	first := look.observe(context.Background(), unityipc.Connection{}, waitingHolderBusy(t, 3.5), probe.probe)
	second := look.observe(context.Background(), unityipc.Connection{}, waitingHolderBusy(t, 4.5), probe.probe)

	if first != nil || second != nil || probe.looks != 1 {
		t.Fatalf("dialogs = %v then %v, looks = %d, want none after one look", first, second, probe.looks)
	}
}

// Verifies a busy answer is not looked behind when its holder runs its own code, its main thread ran
// within the last second, the package predates the phase, the error is not busy, or there is no probe.
func TestBusyDialogLookDoesNotLookWithoutAWaitingStillHolder(t *testing.T) {
	cases := map[string]struct {
		err      error
		useProbe bool
	}{
		"holder runs its code": {err: busyRPCError(t, map[string]any{"runningToolPhase": "Executing", "secondsSinceLastMainThreadTick": 3.5}), useProbe: true},
		"main thread just ran": {err: waitingHolderBusy(t, 0.5), useProbe: true},
		"no phase":             {err: busyRPCError(t, map[string]any{"secondsSinceLastMainThreadTick": 3.5}), useProbe: true},
		"not busy":             {err: errors.New("rejected"), useProbe: true},
		"no probe":             {err: waitingHolderBusy(t, 3.5)},
	}
	for name, tc := range cases {
		t.Run(name, func(t *testing.T) {
			probe := &countingDialogProbe{dialogs: []statusDialog{commandDialogPlayerStoppedDialog()}}
			var probeFunc func(context.Context, unityipc.Connection) (statusReport, error)
			if tc.useProbe {
				probeFunc = probe.probe
			}
			look := busyDialogLook{}

			dialogs := look.observe(context.Background(), unityipc.Connection{}, tc.err, probeFunc)

			if dialogs != nil || probe.looks != 0 {
				t.Fatalf("dialogs = %v, looks = %d, want no look", dialogs, probe.looks)
			}
		})
	}
}

// serveBusyWithHolderData accepts every request and refuses it as busy with the given answer.
func serveBusyWithHolderData(t *testing.T, answer string) unityipc.Connection {
	t.Helper()
	listener := newLoopbackIpcListener(t)
	accepted := `{"jsonrpc":"2.0","result":{"accepted":true},"uloop":{"phase":"accepted","heartbeatIntervalSeconds":10},"id":1}`
	go func() {
		for {
			conn, acceptErr := listener.Accept()
			if acceptErr != nil {
				return
			}
			if _, readErr := readScriptedIPCRequest(conn); readErr == nil {
				_ = unityipc.Write(conn, []byte(accepted))
				_ = unityipc.Write(conn, []byte(answer))
			}
			_ = conn.Close()
		}
	}()
	return unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: listener.Addr().Network(), Address: listener.Addr().String()},
		ProjectRoot: t.TempDir(),
	}
}

const waitingHolderBusyAnswer = `{"jsonrpc":"2.0","error":{"code":-32603,"message":"busy","data":{"type":"server_busy","runningToolName":"compile","runningToolPhase":"WaitingForMainThread","secondsSinceLastMainThreadTick":3.5,"runningToolElapsedSeconds":3}},"id":1}`

func busyLookSendDeps(probe *countingDialogProbe) connectionRetryDeps {
	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = runningUnityProcessLookup
	deps.retryPoll = time.Millisecond
	deps.retryTimeout = 300 * time.Millisecond
	deps.dialogWatch = dialogWatchDeps{}
	deps.earlyDialogReport = earlyDialogReportDeps{
		probeEditorStatus:    probe.probe,
		reportBlockingDialog: func(context.Context, unityipc.Connection) error { return nil },
		waitFor:              func(time.Duration) <-chan time.Time { return make(chan time.Time) },
	}
	return deps
}

// Verifies a send refused as busy by a holder waiting behind a dialog stops at once with the same
// BLOCKED_BY_DIALOG as the check before sending, not the busy error, after one look.
func TestSendRefusedBehindADialogReportsItAsNotSent(t *testing.T) {
	probe := &countingDialogProbe{dialogs: []statusDialog{commandDialogPlayerStoppedDialog()}}

	_, err := sendWithTransientConnectionRetryWithDeps(context.Background(),
		serveBusyWithHolderData(t, waitingHolderBusyAnswer), "get-logs", map[string]any{}, nil, 0, busyLookSendDeps(probe))

	var blocked blockedByDialogError
	if !errors.As(err, &blocked) || isUnityServerBusyRPCError(err) || probe.looks != 1 {
		t.Fatalf("err = %v, looks = %d, want BLOCKED_BY_DIALOG that is not busy after one look", err, probe.looks)
	}
	cliError := clierrors.ClassifyError(err, clierrors.ErrorContext{Command: "get-logs"})
	if cliError.ErrorCode != "BLOCKED_BY_DIALOG" || !cliError.SafeToRetry {
		t.Fatalf("classified = %s safeToRetry=%v, want BLOCKED_BY_DIALOG safe to retry", cliError.ErrorCode, cliError.SafeToRetry)
	}
}

// Verifies a send that hands busy straight back, as hot reload does, never looks.
func TestSendReturningBusyAtOnceDoesNotLook(t *testing.T) {
	probe := &countingDialogProbe{dialogs: []statusDialog{commandDialogPlayerStoppedDialog()}}
	deps := busyLookSendDeps(probe)
	deps.returnBusyWithoutRetry = true

	_, err := sendWithTransientConnectionRetryWithDeps(context.Background(),
		serveBusyWithHolderData(t, waitingHolderBusyAnswer), "get-logs", map[string]any{}, nil, 0, deps)

	if !isUnityServerBusyRPCError(err) || probe.looks != 0 {
		t.Fatalf("err = %v, looks = %d, want the busy error without a look", err, probe.looks)
	}
}
