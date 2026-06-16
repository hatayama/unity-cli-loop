package unityipc

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"net"
	"os"
	"time"

	clicontract "github.com/hatayama/unity-cli-loop/cli"
)

const (
	requestTimeout       = 180 * time.Second
	finalResponseTimeout = 30 * time.Minute
)

const (
	rpcResponsePhaseAccepted  = "accepted"
	rpcResponsePhaseHeartbeat = "heartbeat"
)

// Why: heartbeat silence means the server process died without closing the socket or
// its sender stalled; six missed heartbeats is well past scheduling jitter.
const heartbeatSilenceGraceFactor = 6

// Why: editor main-thread work running five minutes without a single update tick
// almost always means a frozen editor. Failing here with a diagnosis beats sitting on
// the 30-minute absolute deadline while the editor needs a restart.
const defaultMainThreadStallLimit = 5 * time.Minute

// Stall reports below this are normal editor busyness and not worth surfacing.
const mainThreadStallProgressThresholdSeconds = 30

type Client struct {
	connection      Connection
	requestID       int
	clientVersion   string
	acceptTimeout   time.Duration
	responseTimeout time.Duration
	// Test seams: zero means "use the derived/default values".
	heartbeatSilenceOverride time.Duration
	mainThreadStallLimit     time.Duration
	mainThreadStallHandler   func(float64)
}

type ProgressFunc = func(message string)

// Connection-stage progress events. Consumers map these tokens to their own
// contextual message; any other progress payload is display-ready text such
// as the main-thread stall notice.
const (
	ProgressEventConnected = "connected"
	ProgressEventAccepted  = "accepted"
)

type rpcRequest struct {
	JSONRPC string            `json:"jsonrpc"`
	Method  string            `json:"method"`
	Params  map[string]any    `json:"params"`
	ULoop   rpcClientMetadata `json:"uloop"`
	ID      int               `json:"id"`
}

type rpcClientMetadata struct {
	CLIVersion         string `json:"cliVersion"`
	ProtocolVersion    int    `json:"protocolVersion"`
	AcceptsDispatchAck bool   `json:"acceptsDispatchAck"`
	AcceptsHeartbeat   bool   `json:"acceptsHeartbeat"`
}

type rpcResponse struct {
	JSONRPC string          `json:"jsonrpc"`
	Result  json.RawMessage `json:"result,omitempty"`
	Error   *rpcError       `json:"error,omitempty"`
	ULoop   rpcResponseMeta `json:"uloop,omitempty"`
	ID      int             `json:"id"`
}

type rpcResponseMeta struct {
	Phase                    string  `json:"phase,omitempty"`
	HeartbeatIntervalSeconds int     `json:"heartbeatIntervalSeconds,omitempty"`
	MainThreadStallSeconds   float64 `json:"mainThreadStallSeconds,omitempty"`
}

// Reports that Unity's editor main thread stopped pumping while the IPC connection
// stayed alive — the freeze case heartbeats exist to expose.
type EditorUnresponsiveError struct {
	StallSeconds float64
}

func (err *EditorUnresponsiveError) Error() string {
	return fmt.Sprintf(
		"unity editor main thread has been unresponsive for %.0f seconds; the editor is likely frozen. Restart it with 'uloop launch -r'",
		err.StallSeconds)
}

type rpcError struct {
	Code    int             `json:"code"`
	Message string          `json:"message"`
	Data    json.RawMessage `json:"data,omitempty"`
}

type ConnectionAttemptError struct {
	ProjectRoot string
	Endpoint    string
	Cause       error
}

func (err *ConnectionAttemptError) Error() string {
	return fmt.Sprintf("the Unity CLI Loop server is not reachable for this project: %s", err.Cause)
}

func (err *ConnectionAttemptError) Unwrap() error {
	return err.Cause
}

type RPCError struct {
	Code    int
	Message string
	Data    json.RawMessage
}

func (err *RPCError) Error() string {
	return fmt.Sprintf("unity error: %s", err.Message)
}

func NewClient(connection Connection, clientVersion string) *Client {
	return &Client{connection: connection, clientVersion: clientVersion}
}

func (client *Client) WithResponseTimeout(timeout time.Duration) *Client {
	client.responseTimeout = timeout
	return client
}

func (client *Client) WithMainThreadStallHandler(handler func(float64)) *Client {
	client.mainThreadStallHandler = handler
	return client
}

func (client *Client) Send(ctx context.Context, method string, params map[string]any) (json.RawMessage, error) {
	return client.SendWithProgress(ctx, method, params, nil)
}

func (client *Client) SendWithProgress(ctx context.Context, method string, params map[string]any, progress ProgressFunc) (json.RawMessage, error) {
	outcome, err := client.SendWithProgressOutcome(ctx, method, params, progress)
	return outcome.Result, err
}

func (client *Client) SendWithProgressOutcome(ctx context.Context, method string, params map[string]any, progress ProgressFunc) (UnitySendOutcome, error) {
	return client.SendWithProgressOutcomeAcceptContext(ctx, ctx, method, params, progress)
}

func (client *Client) SendWithProgressOutcomeAcceptContext(
	ctx context.Context,
	acceptParentContext context.Context,
	method string,
	params map[string]any,
	progress ProgressFunc,
) (UnitySendOutcome, error) {
	acceptCtx, cancelAccept := context.WithTimeout(acceptParentContext, client.getAcceptTimeout())
	defer cancelAccept()

	startedAt := time.Now()
	timing := UnitySendTiming{}

	dialStartedAt := time.Now()
	conn, err := dialEndpoint(acceptCtx, client.connection.Endpoint)
	timing.Dial = time.Since(dialStartedAt)
	if err != nil {
		timing.Total = time.Since(startedAt)
		return UnitySendOutcome{Timing: timing}, formatConnectionAttemptError(client.connection, err)
	}
	defer func() {
		_ = conn.Close()
	}()

	if progress != nil {
		progress(ProgressEventConnected)
	}

	client.requestID++
	request := rpcRequest{
		JSONRPC: "2.0",
		Method:  method,
		Params:  params,
		ULoop: rpcClientMetadata{
			CLIVersion:         client.clientVersion,
			ProtocolVersion:    clicontract.Current.ProtocolVersion,
			AcceptsDispatchAck: true,
			AcceptsHeartbeat:   true,
		},
		ID: client.requestID,
	}

	payload, err := json.Marshal(request)
	if err != nil {
		return UnitySendOutcome{}, err
	}

	if deadline, ok := acceptCtx.Deadline(); ok {
		_ = conn.SetDeadline(deadline)
	}

	writeStartedAt := time.Now()
	if err := Write(conn, payload); err != nil {
		timing.Write = time.Since(writeStartedAt)
		timing.Total = time.Since(startedAt)
		return UnitySendOutcome{Timing: timing}, err
	}
	timing.Write = time.Since(writeStartedAt)
	outcome := UnitySendOutcome{RequestDispatched: true}

	reader := bufio.NewReader(conn)
	response, err := readRPCResponse(reader, &timing)
	if err != nil {
		timing.Total = time.Since(startedAt)
		outcome.Timing = timing
		return outcome, err
	}

	if response.ULoop.Phase == rpcResponsePhaseAccepted {
		outcome.RequestAccepted = true
		if progress != nil {
			progress(ProgressEventAccepted)
		}

		cancelAccept()
		heartbeatSilence := client.getHeartbeatSilence(response.ULoop.HeartbeatIntervalSeconds)
		absoluteDeadline := time.Now().Add(client.getResponseTimeout())
		if err := applyPostAcceptDeadline(conn, heartbeatSilence, absoluteDeadline); err != nil {
			timing.Total = time.Since(startedAt)
			outcome.Timing = timing
			return outcome, err
		}
		stopCancelWatcher := watchConnectionCancellation(ctx, conn)
		defer stopCancelWatcher()

		for {
			response, err = readRPCResponse(reader, &timing)
			if err != nil {
				timing.Total = time.Since(startedAt)
				outcome.Timing = timing
				if ctx.Err() != nil {
					return outcome, ctx.Err()
				}
				if heartbeatSilence > 0 && isDeadlineExpiry(err) && time.Now().Before(absoluteDeadline) {
					return outcome, fmt.Errorf(
						"no response or heartbeat from Unity for %s; the connection or server stalled: %w",
						heartbeatSilence, err)
				}
				return outcome, err
			}
			if response.ULoop.Phase != rpcResponsePhaseHeartbeat {
				break
			}
			if ctx.Err() != nil {
				timing.Total = time.Since(startedAt)
				outcome.Timing = timing
				return outcome, ctx.Err()
			}

			stallSeconds := response.ULoop.MainThreadStallSeconds
			if stallSeconds >= mainThreadStallProgressThresholdSeconds {
				if client.mainThreadStallHandler != nil {
					client.mainThreadStallHandler(stallSeconds)
				}
				if progress != nil {
					progress(fmt.Sprintf("unity editor main thread busy for %.0fs...", stallSeconds))
				}
			}
			if stallSeconds >= client.getMainThreadStallLimit().Seconds() {
				timing.Total = time.Since(startedAt)
				outcome.Timing = timing
				return outcome, &EditorUnresponsiveError{StallSeconds: stallSeconds}
			}
			if heartbeatSilence > 0 {
				if err := applyPostAcceptDeadline(conn, heartbeatSilence, absoluteDeadline); err != nil {
					timing.Total = time.Since(startedAt)
					outcome.Timing = timing
					return outcome, err
				}
			}
		}
	}

	if response.Error != nil {
		timing.Total = time.Since(startedAt)
		outcome.Timing = timing
		return outcome, &RPCError{
			Code:    response.Error.Code,
			Message: response.Error.Message,
			Data:    response.Error.Data,
		}
	}
	if len(response.Result) == 0 {
		timing.Total = time.Since(startedAt)
		outcome.Timing = timing
		return outcome, fmt.Errorf("UNITY_NO_RESPONSE")
	}

	outcome.Result = response.Result
	timing.Total = time.Since(startedAt)
	outcome.Timing = timing
	return outcome, nil
}

func (client *Client) getAcceptTimeout() time.Duration {
	if client.acceptTimeout > 0 {
		return client.acceptTimeout
	}
	return requestTimeout
}

func (client *Client) getResponseTimeout() time.Duration {
	if client.responseTimeout > 0 {
		return client.responseTimeout
	}
	return finalResponseTimeout
}

// Derives the sliding silence window for a negotiated heartbeat connection.
// Zero disables sliding: an explicit response timeout (e.g. compile's quick fallback
// to status polling) must stay an absolute deadline, and servers that did not
// negotiate heartbeats keep the legacy absolute deadline too.
func (client *Client) getHeartbeatSilence(heartbeatIntervalSeconds int) time.Duration {
	if client.responseTimeout > 0 {
		return 0
	}
	if heartbeatIntervalSeconds <= 0 {
		return 0
	}
	if client.heartbeatSilenceOverride > 0 {
		return client.heartbeatSilenceOverride
	}
	return time.Duration(heartbeatIntervalSeconds) * time.Second * heartbeatSilenceGraceFactor
}

func (client *Client) getMainThreadStallLimit() time.Duration {
	if client.mainThreadStallLimit > 0 {
		return client.mainThreadStallLimit
	}
	return defaultMainThreadStallLimit
}

// Reports whether the error is a connection deadline expiry. go-winio's named pipe
// deadline error is not os.ErrDeadlineExceeded, so a Timeout() probe through the
// unwrap chain is required for Windows.
func isDeadlineExpiry(err error) bool {
	if errors.Is(err, os.ErrDeadlineExceeded) || os.IsTimeout(err) {
		return true
	}
	var timeoutCause interface{ Timeout() bool }
	return errors.As(err, &timeoutCause) && timeoutCause.Timeout()
}

// Sets the connection deadline to the sliding heartbeat-silence window, capped by the
// absolute response deadline so heartbeats can never extend the total wait past it.
func applyPostAcceptDeadline(conn net.Conn, heartbeatSilence time.Duration, absoluteDeadline time.Time) error {
	deadline := absoluteDeadline
	if heartbeatSilence > 0 {
		slidingDeadline := time.Now().Add(heartbeatSilence)
		if slidingDeadline.Before(deadline) {
			deadline = slidingDeadline
		}
	}
	return conn.SetDeadline(deadline)
}

func watchConnectionCancellation(ctx context.Context, conn net.Conn) func() {
	done := make(chan struct{})
	go func() {
		select {
		case <-ctx.Done():
			_ = conn.SetDeadline(time.Now())
		case <-done:
		}
	}()
	return func() {
		close(done)
	}
}

func readRPCResponse(reader *bufio.Reader, timing *UnitySendTiming) (rpcResponse, error) {
	readStartedAt := time.Now()
	responsePayload, err := Read(reader)
	timing.Read += time.Since(readStartedAt)
	if err != nil {
		return rpcResponse{}, err
	}

	decodeStartedAt := time.Now()
	var response rpcResponse
	if err := json.Unmarshal(responsePayload, &response); err != nil {
		timing.Decode += time.Since(decodeStartedAt)
		return rpcResponse{}, err
	}
	timing.Decode += time.Since(decodeStartedAt)
	return response, nil
}

func formatConnectionAttemptError(connection Connection, err error) error {
	return &ConnectionAttemptError{
		ProjectRoot: connection.ProjectRoot,
		Endpoint:    connection.Endpoint.Address,
		Cause:       err,
	}
}
