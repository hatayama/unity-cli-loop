package unityipc

import (
	"bufio"
	"context"
	"encoding/json"
	"fmt"
	"net"
	"time"
)

const (
	requestTimeout       = 180 * time.Second
	finalResponseTimeout = 30 * time.Minute
)

const rpcResponsePhaseAccepted = "accepted"

type Client struct {
	connection      Connection
	requestID       int
	clientVersion   string
	acceptTimeout   time.Duration
	responseTimeout time.Duration
}

type ProgressFunc = func(message string)

type rpcRequest struct {
	JSONRPC string            `json:"jsonrpc"`
	Method  string            `json:"method"`
	Params  map[string]any    `json:"params"`
	ULoop   rpcClientMetadata `json:"uloop"`
	ID      int               `json:"id"`
}

type rpcClientMetadata struct {
	CLIVersion         string `json:"cliVersion"`
	AcceptsDispatchAck bool   `json:"acceptsDispatchAck"`
}

type rpcResponse struct {
	JSONRPC string          `json:"jsonrpc"`
	Result  json.RawMessage `json:"result,omitempty"`
	Error   *rpcError       `json:"error,omitempty"`
	ULoop   rpcResponseMeta `json:"uloop,omitempty"`
	ID      int             `json:"id"`
}

type rpcResponseMeta struct {
	Phase string `json:"phase,omitempty"`
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

// WithResponseTimeout sets the deadline for the final response after Unity accepts a request.
func (client *Client) WithResponseTimeout(timeout time.Duration) *Client {
	client.responseTimeout = timeout
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
		progress("connected")
	}

	client.requestID++
	request := rpcRequest{
		JSONRPC: "2.0",
		Method:  method,
		Params:  params,
		ULoop: rpcClientMetadata{
			CLIVersion:         client.clientVersion,
			AcceptsDispatchAck: true,
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
			progress("accepted")
		}

		cancelAccept()
		if err := setConnectionDeadlineFromNow(conn, client.getResponseTimeout()); err != nil {
			timing.Total = time.Since(startedAt)
			outcome.Timing = timing
			return outcome, err
		}
		stopCancelWatcher := watchConnectionCancellation(ctx, conn)
		defer stopCancelWatcher()

		response, err = readRPCResponse(reader, &timing)
		if err != nil {
			timing.Total = time.Since(startedAt)
			outcome.Timing = timing
			if ctx.Err() != nil {
				return outcome, ctx.Err()
			}
			return outcome, err
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

func setConnectionDeadlineFromNow(conn net.Conn, timeout time.Duration) error {
	return conn.SetDeadline(time.Now().Add(timeout))
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
