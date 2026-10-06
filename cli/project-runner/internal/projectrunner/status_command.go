package projectrunner

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"strconv"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const editorStatusBridgeCommandName = "get-editor-status"

// The same threshold the busy guidance uses to call the Editor main thread stalled, so status
// and a BUSY error agree on it.
const editorStatusMainThreadStallThresholdSeconds = 5.0

const (
	statusStateReady             = "Ready"
	statusStateBusy              = "Busy"
	statusStateStarting          = "Starting"
	statusStateMainThreadBlocked = "MainThreadBlocked"
	statusStateCompiling         = "Compiling"
	statusStateImportingAssets   = "ImportingAssets"
	statusStateNotResponding     = "NotResponding"
	statusStateServerUnavailable = "ServerUnavailable"
	statusStateNotRunning        = "NotRunning"
	statusStateUnreachable       = "Unreachable"
)

// Dependencies that tests replace. A struct instead of package variables, so tests that run in
// parallel cannot see each other's fakes.
type statusCommandDeps struct {
	probeTimeout time.Duration
	// How long to wait before asking again when the first answer reads as a blocked main thread.
	reprobeDelay          time.Duration
	isUnityProcessRunning func(ctx context.Context, projectRoot string) (bool, error)
}

func defaultStatusCommandDeps() statusCommandDeps {
	return statusCommandDeps{
		probeTimeout: 5 * time.Second,
		reprobeDelay: 1 * time.Second,
		isUnityProcessRunning: func(ctx context.Context, projectRoot string) (bool, error) {
			process, err := clicore.FindRunningUnityProcess(ctx, projectRoot)
			return process != nil, err
		},
	}
}

// editorStatusResponse is the get-editor-status answer. The tags are the Editor's PascalCase
// property names.
type editorStatusResponse struct {
	IsBusy                         bool    `json:"IsBusy"`
	RunningToolName                string  `json:"RunningToolName"`
	RunningToolElapsedSeconds      *int    `json:"RunningToolElapsedSeconds"`
	RunningToolPhase               string  `json:"RunningToolPhase"`
	HasEditorState                 bool    `json:"HasEditorState"`
	IsPlaying                      bool    `json:"IsPlaying"`
	IsPaused                       bool    `json:"IsPaused"`
	IsCompiling                    bool    `json:"IsCompiling"`
	IsUpdating                     bool    `json:"IsUpdating"`
	SecondsSinceLastMainThreadTick float64 `json:"SecondsSinceLastMainThreadTick"`
}

// statusReport is what uloop status prints. Fields that do not apply to the state are left out.
type statusReport struct {
	State                          string   `json:"State"`
	Ready                          bool     `json:"Ready"`
	Message                        string   `json:"Message"`
	UnityProcessRunning            *bool    `json:"UnityProcessRunning,omitempty"`
	ConnectionError                string   `json:"ConnectionError,omitempty"`
	RunningToolName                string   `json:"RunningToolName,omitempty"`
	RunningToolElapsedSeconds      *int     `json:"RunningToolElapsedSeconds,omitempty"`
	RunningToolPhase               string   `json:"RunningToolPhase,omitempty"`
	IsPlaying                      *bool    `json:"IsPlaying,omitempty"`
	IsPaused                       *bool    `json:"IsPaused,omitempty"`
	IsCompiling                    *bool    `json:"IsCompiling,omitempty"`
	IsUpdating                     *bool    `json:"IsUpdating,omitempty"`
	SecondsSinceLastMainThreadTick *float64 `json:"SecondsSinceLastMainThreadTick,omitempty"`
	NextActions                    []string `json:"NextActions"`
}

func runStatusCommand(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	return runStatusCommandWithDeps(ctx, connection, args, stdout, stderr, defaultStatusCommandDeps())
}

// runStatusCommandWithDeps prints the Editor's state as JSON and exits 0 only for Ready. It never
// retries the connection, focuses Unity, or takes the execution slot, so it is safe to poll.
func runStatusCommandWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
	deps statusCommandDeps,
) int {
	errorContext := clierrors.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: clicore.StatusCommandName}
	if len(args) > 0 {
		clierrors.WriteClassifiedError(stderr, &clierrors.ArgumentError{
			Message:     "status takes no options: " + args[0],
			Option:      args[0],
			NextActions: []string{"Run `uloop status` with no options other than --project-path."},
		}, errorContext)
		return 1
	}

	report, err := probeEditorStatus(ctx, connection, deps)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, errorContext)
		return 1
	}
	payload, err := json.Marshal(report)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, errorContext)
		return 1
	}
	clicore.WriteJSON(stdout, payload)
	if report.Ready {
		return 0
	}
	return 1
}

// probeEditorStatus asks the Editor once, and asks once more only when the first answer reads
// as a blocked main thread.
func probeEditorStatus(
	ctx context.Context,
	connection unityipc.Connection,
	deps statusCommandDeps,
) (statusReport, error) {
	response, report, err := readEditorStatus(ctx, connection, deps)
	if response == nil {
		return report, err
	}
	if classifyEditorState(*response) != statusStateMainThreadBlocked {
		return reportFromEditorResponse(*response), nil
	}

	// The first probe woke the Editor loop (SignalTick). A loop that was only sleeping runs
	// within the delay, so the second reading tells sleeping from blocked.
	select {
	case <-ctx.Done():
		return statusReport{}, ctx.Err()
	case <-time.After(deps.reprobeDelay):
	}
	response, report, err = readEditorStatus(ctx, connection, deps)
	if response == nil {
		return report, err
	}
	return reportFromEditorResponse(*response), nil
}

// readEditorStatus makes one get-editor-status round trip with its own timeout. A nil response
// means the answer did not arrive or could not be read; report and err then hold what status
// prints instead.
func readEditorStatus(
	ctx context.Context,
	connection unityipc.Connection,
	deps statusCommandDeps,
) (*editorStatusResponse, statusReport, error) {
	probeCtx, cancel := context.WithTimeout(ctx, deps.probeTimeout)
	defer cancel()

	raw, err := unityipc.NewClient(connection, clicontract.ProjectRunnerVersion()).Send(
		probeCtx,
		editorStatusBridgeCommandName,
		map[string]any{},
	)
	if err != nil {
		// The process lookup gets the caller's ctx because probeCtx may already have run out.
		report, reportErr := reportFromProbeError(ctx, connection.ProjectRoot, err, deps)
		return nil, report, reportErr
	}

	// Why not through reportFromProbeError: its disconnect check also matches message text, so an
	// unreadable answer could be mistaken for a dropped connection.
	var response editorStatusResponse
	if err := json.Unmarshal(raw, &response); err != nil {
		return nil, statusReport{}, fmt.Errorf("unexpected %s response: %w", editorStatusBridgeCommandName, err)
	}
	// The Busy message reads the elapsed seconds, and the Editor always sends them while a
	// command holds the slot, so an answer without them is not one status can describe.
	if response.IsBusy && response.RunningToolElapsedSeconds == nil {
		return nil, statusReport{}, fmt.Errorf(
			"unexpected %s response: busy without RunningToolElapsedSeconds",
			editorStatusBridgeCommandName)
	}
	return &response, statusReport{}, nil
}

// reportFromProbeError turns a failed round trip into a state, or returns the error when the
// failure says nothing about whether Unity can take a command.
func reportFromProbeError(
	ctx context.Context,
	projectRoot string,
	err error,
	deps statusCommandDeps,
) (statusReport, error) {
	// Why first: a sandbox denial also arrives as a ConnectionAttemptError, and reporting it as
	// NotRunning would tell a sandboxed agent that Unity is closed.
	if clierrors.IsPermanentConnectError(err) {
		return statusReport{}, err
	}
	// Why before the disconnect check: that check also matches message text such as "EOF", which
	// an Editor error message can contain.
	var rpcErr *unityipc.RPCError
	if errors.As(err, &rpcErr) {
		return statusReport{}, err
	}
	var connectionErr *unityipc.ConnectionAttemptError
	if errors.As(err, &connectionErr) || clierrors.IsTransportDisconnectError(err) {
		return reportFromUnreachableServer(ctx, projectRoot, err, deps), nil
	}
	// Covers both the deadline before "accepted" (os.ErrDeadlineExceeded) and after it
	// (context.DeadlineExceeded), since both report Timeout().
	if clierrors.IsFinalResponseTimeoutError(err) {
		report := newStatusReport(statusStateNotResponding, fmt.Sprintf(
			"Connected to Unity, but it did not answer within %ss.",
			strconv.FormatFloat(deps.probeTimeout.Seconds(), 'f', -1, 64)))
		report.ConnectionError = err.Error()
		return report, nil
	}
	return statusReport{}, err
}

// reportFromUnreachableServer reports a server that refused or dropped the connection. Whether
// this project's Unity process runs decides between a server that is coming back and a closed
// Editor; the connection error is shown as is because every refusal reaches here alike.
func reportFromUnreachableServer(
	ctx context.Context,
	projectRoot string,
	probeErr error,
	deps statusCommandDeps,
) statusReport {
	var report statusReport
	running, lookupErr := deps.isUnityProcessRunning(ctx, projectRoot)
	switch {
	case lookupErr != nil:
		report = newStatusReport(statusStateUnreachable, fmt.Sprintf(
			"The uloop server is not accepting connections, and the Unity process list could not be read: %s.",
			lookupErr.Error()))
	case running:
		report = newStatusReport(statusStateServerUnavailable,
			"Unity is running for this project, but its uloop server is not accepting connections. "+
				"Unity is starting, reloading scripts, or was started in Safe Mode because of compile errors "+
				"(uloop cannot run in Safe Mode).")
		report.UnityProcessRunning = &running
	default:
		report = newStatusReport(statusStateNotRunning, "No Unity Editor is running for this project.")
		report.UnityProcessRunning = &running
	}
	report.ConnectionError = probeErr.Error()
	return report
}

func reportFromEditorResponse(response editorStatusResponse) statusReport {
	state := classifyEditorState(response)
	report := newStatusReport(state, editorStateMessage(state, response))
	report.SecondsSinceLastMainThreadTick = &response.SecondsSinceLastMainThreadTick
	if response.HasEditorState {
		report.IsPlaying = &response.IsPlaying
		report.IsPaused = &response.IsPaused
		report.IsCompiling = &response.IsCompiling
		report.IsUpdating = &response.IsUpdating
	}
	if state == statusStateBusy {
		report.RunningToolName = response.RunningToolName
		report.RunningToolElapsedSeconds = response.RunningToolElapsedSeconds
		report.RunningToolPhase = response.RunningToolPhase
	}
	return report
}

// classifyEditorState picks the state of an Editor that answered. The order is the contract: a
// running command explains a busy main thread, and the compile values are refreshed by the main
// thread, so they are stale while it is blocked.
func classifyEditorState(response editorStatusResponse) string {
	if response.IsBusy {
		return statusStateBusy
	}
	if !response.HasEditorState {
		return statusStateStarting
	}
	if response.SecondsSinceLastMainThreadTick >= editorStatusMainThreadStallThresholdSeconds {
		return statusStateMainThreadBlocked
	}
	if response.IsCompiling {
		return statusStateCompiling
	}
	if response.IsUpdating {
		return statusStateImportingAssets
	}
	return statusStateReady
}

func editorStateMessage(state string, response editorStatusResponse) string {
	switch state {
	case statusStateBusy:
		return fmt.Sprintf("uloop %s has been running for %ds (%s).",
			response.RunningToolName, *response.RunningToolElapsedSeconds, response.RunningToolPhase)
	case statusStateStarting:
		return "The uloop server answered, but Unity has not reported its Editor state yet."
	case statusStateMainThreadBlocked:
		return fmt.Sprintf(
			"Unity's main thread has not run for %.1fs; a dialog, a long import, or running code is blocking it.",
			response.SecondsSinceLastMainThreadTick)
	case statusStateCompiling:
		return "Unity is compiling scripts; a domain reload follows."
	case statusStateImportingAssets:
		return "Unity is importing assets."
	default:
		return "Unity is idle and can take a command now."
	}
}

func newStatusReport(state string, message string) statusReport {
	return statusReport{
		State:       state,
		Ready:       state == statusStateReady,
		Message:     message,
		NextActions: statusNextActions(state),
	}
}

// statusNextActions returns an empty, non-nil list for Ready so the output prints [] there.
func statusNextActions(state string) []string {
	const runAgainShortly = "Run uloop status again in a few seconds."
	switch state {
	case statusStateBusy:
		return []string{
			"Wait for the running command to finish, then run uloop status again.",
			"Another command sent now is rejected as busy.",
		}
	case statusStateStarting, statusStateCompiling, statusStateImportingAssets:
		return []string{runAgainShortly}
	case statusStateMainThreadBlocked:
		return []string{
			"Look at the Unity Editor for a modal dialog.",
			runAgainShortly,
			"If it stays blocked for minutes, restart the Editor with uloop launch -r.",
		}
	case statusStateNotResponding:
		return []string{
			"Run uloop status again.",
			"If this repeats, look at the Unity Editor or restart it with uloop launch -r.",
		}
	case statusStateServerUnavailable:
		return []string{
			"Run uloop status again in 5-10 seconds.",
			"If it stays ServerUnavailable, look at the Unity Editor: a Safe Mode window means the compile errors must be fixed first.",
		}
	case statusStateNotRunning:
		return []string{"Start it with uloop launch."}
	case statusStateUnreachable:
		return []string{
			"Run uloop status again.",
			"Check whether the Unity Editor for this project is open.",
		}
	default:
		return []string{}
	}
}
