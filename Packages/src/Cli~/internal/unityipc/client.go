package unityipc

import (
	"bufio"
	"context"
	"encoding/json"
	"fmt"
	"time"
)

const requestTimeout = 180 * time.Second

type Client struct {
	connection    Connection
	requestID     int
	clientVersion string
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
	CLIVersion string `json:"cliVersion"`
}

type rpcResponse struct {
	JSONRPC string          `json:"jsonrpc"`
	Result  json.RawMessage `json:"result,omitempty"`
	Error   *rpcError       `json:"error,omitempty"`
	ID      int             `json:"id"`
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

func (client *Client) Send(ctx context.Context, method string, params map[string]any) (json.RawMessage, error) {
	return client.SendWithProgress(ctx, method, params, nil)
}

func (client *Client) SendWithProgress(ctx context.Context, method string, params map[string]any, progress ProgressFunc) (json.RawMessage, error) {
	outcome, err := client.SendWithProgressOutcome(ctx, method, params, progress)
	return outcome.Result, err
}

func (client *Client) SendWithProgressOutcome(ctx context.Context, method string, params map[string]any, progress ProgressFunc) (UnitySendOutcome, error) {
	ctx, cancel := context.WithTimeout(ctx, requestTimeout)
	defer cancel()

	startedAt := time.Now()
	timing := UnitySendTiming{}

	dialStartedAt := time.Now()
	conn, err := dialEndpoint(ctx, client.connection.Endpoint)
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
			CLIVersion: client.clientVersion,
		},
		ID: client.requestID,
	}

	payload, err := json.Marshal(request)
	if err != nil {
		return UnitySendOutcome{}, err
	}

	if deadline, ok := ctx.Deadline(); ok {
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

	readStartedAt := time.Now()
	responsePayload, err := Read(bufio.NewReader(conn))
	timing.Read = time.Since(readStartedAt)
	if err != nil {
		timing.Total = time.Since(startedAt)
		outcome.Timing = timing
		return outcome, err
	}

	decodeStartedAt := time.Now()
	var response rpcResponse
	if err := json.Unmarshal(responsePayload, &response); err != nil {
		timing.Decode = time.Since(decodeStartedAt)
		timing.Total = time.Since(startedAt)
		outcome.Timing = timing
		return outcome, err
	}
	timing.Decode = time.Since(decodeStartedAt)
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

func formatConnectionAttemptError(connection Connection, err error) error {
	return &ConnectionAttemptError{
		ProjectRoot: connection.ProjectRoot,
		Endpoint:    connection.Endpoint.Address,
		Cause:       err,
	}
}
