package dispatcher

import (
	"context"
	"errors"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/project"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	quitEditorBridgeCommandName = "quit-editor"
	// Long enough for an Editor whose main thread is free to run the request, and short enough that a
	// frozen Editor, which never runs it, is force-killed without a long wait.
	launchQuitRequestTimeout = 5 * time.Second
	// How long an Editor that took the quit request gets to save its window layout and exit before it is
	// force-killed.
	launchQuitExitTimeout = 30 * time.Second
)

// launchStopMethod names how --restart or --quit stopped the running Editor.
type launchStopMethod string

const (
	launchStopMethodQuit      launchStopMethod = "Quit"
	launchStopMethodForceKill launchStopMethod = "ForceKill"
)

// stopUnityProcess ends the running Editor for --restart and --quit. It first asks the Editor to quit,
// which saves the window layout and discards unsaved scene changes without a dialog, and force-kills it
// when the Editor does not take the request or does not exit in time.
func stopUnityProcess(ctx context.Context, projectRoot string, pid int, isV2 bool, deps launchDeps) (launchStopMethod, error) {
	// Why not for a V2 project: its server predates quit-editor, so asking would only delay the kill.
	if !isV2 && deps.requestUnityQuit(ctx, projectRoot) {
		err := deps.waitForUnityProcessExit(ctx, projectRoot, pid, launchProcessExitPoll, launchQuitExitTimeout)
		if err == nil {
			return launchStopMethodQuit, nil
		}
		var exitTimeout launchProcessExitTimeoutError
		if !errors.As(err, &exitTimeout) {
			return "", err
		}
	}
	if err := deps.killUnityProcess(pid); err != nil {
		return "", err
	}
	if err := deps.waitForUnityProcessExit(ctx, projectRoot, pid, launchProcessExitPoll, launchProcessExitTimeout); err != nil {
		return "", err
	}
	return launchStopMethodForceKill, nil
}

// requestUnityQuit asks the project's Editor to quit and reports whether it took the request.
func requestUnityQuit(ctx context.Context, projectRoot string) bool {
	connection, err := project.ResolveConnection(projectRoot, projectRoot)
	if err != nil {
		return false
	}
	return sendUnityQuitRequest(ctx, connection, launchQuitRequestTimeout)
}

// sendUnityQuitRequest sends quit-editor once. The Editor took the request when it answered, or when the
// connection dropped after the request reached it, which is how an Editor that exits before its answer
// is written looks. A rejection, an unreachable server, and no answer in time all mean it did not.
func sendUnityQuitRequest(ctx context.Context, connection unityipc.Connection, timeout time.Duration) bool {
	requestContext, cancel := context.WithTimeout(ctx, timeout)
	defer cancel()
	outcome, err := unityipc.NewClient(connection, clicontract.ProjectRunnerVersion()).
		SendWithProgressOutcome(requestContext, quitEditorBridgeCommandName, map[string]any{}, nil)
	if err == nil {
		return true
	}
	return outcome.RequestDispatched && requestContext.Err() == nil && clierrors.IsTransportDisconnectError(err)
}
