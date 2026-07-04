package unityipc

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"net"
	"os"
	"sync"
	"time"

	clicontract "github.com/hatayama/unity-cli-loop/common/clicontract"
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
	connection    Connection
	requestIDMu   sync.Mutex
	requestID     int
	clientVersion string
	options       ClientOptions
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
	ProjectRunnerVersion string `json:"projectRunnerVersion"`
	ProtocolVersion      int    `json:"protocolVersion"`
	AcceptsDispatchAck   bool   `json:"acceptsDispatchAck"`
	AcceptsHeartbeat     bool   `json:"acceptsHeartbeat"`
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

func NewClient(connection Connection, clientVersion string, options ...ClientOption) *Client {
	clientOptions := ClientOptions{}
	for _, option := range options {
		option(&clientOptions)
	}
	return &Client{connection: connection, clientVersion: clientVersion, options: clientOptions}
}

func (client *Client) WithResponseTimeout(timeout time.Duration) *Client {
	return client.withOption(WithResponseTimeout(timeout))
}

func (client *Client) WithMainThreadStallHandler(handler func(float64)) *Client {
	return client.withOption(WithMainThreadStallHandler(handler))
}

func (client *Client) withOption(option ClientOption) *Client {
	clientOptions := client.options
	option(&clientOptions)
	return &Client{
		connection:    client.connection,
		clientVersion: client.clientVersion,
		options:       clientOptions,
	}
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

	requestID := client.nextRequestID()
	request := rpcRequest{
		JSONRPC: "2.0",
		Method:  method,
		Params:  params,
		ULoop: rpcClientMetadata{
			ProjectRunnerVersion: client.clientVersion,
			ProtocolVersion:      clicontract.Current.ProtocolVersion,
			AcceptsDispatchAck:   true,
			AcceptsHeartbeat:     true,
		},
		ID: requestID,
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

		return client.readAcceptedResponse(
			ctx,
			conn,
			reader,
			progress,
			cancelAccept,
			startedAt,
			timing,
			outcome,
			response,
		)
	}

	return finishRPCResponse(response, outcome, timing, startedAt)
}

func (client *Client) readAcceptedResponse(
	ctx context.Context,
	conn net.Conn,
	reader *bufio.Reader,
	progress ProgressFunc,
	cancelAccept context.CancelFunc,
	startedAt time.Time,
	timing UnitySendTiming,
	outcome UnitySendOutcome,
	response rpcResponse,
) (UnitySendOutcome, error) {
	cancelAccept()
	heartbeatSilence := client.getHeartbeatSilence(response.ULoop.HeartbeatIntervalSeconds)
	absoluteDeadline := time.Now().Add(client.getResponseTimeout())
	if err := applyPostAcceptDeadline(conn, heartbeatSilence, absoluteDeadline); err != nil {
		return finishOutcomeWithError(outcome, timing, startedAt, err)
	}
	stopCancelWatcher := watchConnectionCancellation(ctx, conn)
	defer stopCancelWatcher()

	for {
		nextResponse, err := readRPCResponse(reader, &timing)
		if err != nil {
			return client.finishAcceptedReadError(
				ctx,
				outcome,
				timing,
				startedAt,
				heartbeatSilence,
				absoluteDeadline,
				err,
			)
		}
		response = nextResponse
		if response.ULoop.Phase != rpcResponsePhaseHeartbeat {
			break
		}
		if ctx.Err() != nil {
			return finishOutcomeWithError(outcome, timing, startedAt, ctx.Err())
		}
		if err := client.handleHeartbeatResponse(
			conn,
			response,
			progress,
			heartbeatSilence,
			absoluteDeadline,
		); err != nil {
			return finishOutcomeWithError(outcome, timing, startedAt, err)
		}
	}

	return finishRPCResponse(response, outcome, timing, startedAt)
}

func (client *Client) finishAcceptedReadError(
	ctx context.Context,
	outcome UnitySendOutcome,
	timing UnitySendTiming,
	startedAt time.Time,
	heartbeatSilence time.Duration,
	absoluteDeadline time.Time,
	err error,
) (UnitySendOutcome, error) {
	if ctx.Err() != nil {
		return finishOutcomeWithError(outcome, timing, startedAt, ctx.Err())
	}
	if heartbeatSilence > 0 && isDeadlineExpiry(err) && time.Now().Before(absoluteDeadline) {
		return finishOutcomeWithError(
			outcome,
			timing,
			startedAt,
			fmt.Errorf(
				"no response or heartbeat from Unity for %s; the connection or server stalled: %w",
				heartbeatSilence,
				err),
		)
	}
	return finishOutcomeWithError(outcome, timing, startedAt, err)
}

func (client *Client) handleHeartbeatResponse(
	conn net.Conn,
	response rpcResponse,
	progress ProgressFunc,
	heartbeatSilence time.Duration,
	absoluteDeadline time.Time,
) error {
	stallSeconds := response.ULoop.MainThreadStallSeconds
	if stallSeconds >= mainThreadStallProgressThresholdSeconds {
		client.reportMainThreadStall(stallSeconds, progress)
	}
	if stallSeconds >= client.getMainThreadStallLimit().Seconds() {
		return &EditorUnresponsiveError{StallSeconds: stallSeconds}
	}
	if heartbeatSilence > 0 {
		return applyPostAcceptDeadline(conn, heartbeatSilence, absoluteDeadline)
	}
	return nil
}

func (client *Client) reportMainThreadStall(stallSeconds float64, progress ProgressFunc) {
	if client.options.mainThreadStallHandler != nil {
		client.options.mainThreadStallHandler(stallSeconds)
	}
	if progress != nil {
		progress(fmt.Sprintf(
			"Unity main thread stuck %.0fs; check modal/long operation...",
			stallSeconds))
	}
}

func finishRPCResponse(
	response rpcResponse,
	outcome UnitySendOutcome,
	timing UnitySendTiming,
	startedAt time.Time,
) (UnitySendOutcome, error) {
	if response.Error != nil {
		return finishOutcomeWithError(outcome, timing, startedAt, &RPCError{
			Code:    response.Error.Code,
			Message: response.Error.Message,
			Data:    response.Error.Data,
		})
	}
	if len(response.Result) == 0 {
		return finishOutcomeWithError(outcome, timing, startedAt, fmt.Errorf("UNITY_NO_RESPONSE"))
	}

	outcome.Result = response.Result
	timing.Total = time.Since(startedAt)
	outcome.Timing = timing
	return outcome, nil
}

func finishOutcomeWithError(
	outcome UnitySendOutcome,
	timing UnitySendTiming,
	startedAt time.Time,
	err error,
) (UnitySendOutcome, error) {
	timing.Total = time.Since(startedAt)
	outcome.Timing = timing
	return outcome, err
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
