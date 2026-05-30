package cli

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"strings"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/unityipc"
)

const (
	compileCommandName        = "compile"
	compileStatusCommandName  = "get-compile-status"
	compileRequestIDParam     = "RequestId"
	compileWaitParam          = domainReloadWaitParam
	compileForceParam         = "ForceRecompile"
	compileWaitTimeout        = toolReadinessTimeout
	compileWaitPollInterval   = toolReadinessPoll
	compileStatusProbeTimeout = toolReadinessProbeTimeout
	compileWaitLogInterval    = 5 * time.Second
	compileResponseTimeout    = 2 * time.Second
)

type compileCompletionOptions struct {
	connection     unityipc.Connection
	requestID      string
	forceRecompile bool
	timeout        time.Duration
	pollInterval   time.Duration
}

type compileStatusResponse struct {
	Ready                    bool            `json:"Ready"`
	HasResult                bool            `json:"HasResult"`
	IsCompiling              bool            `json:"IsCompiling"`
	IsUpdating               bool            `json:"IsUpdating"`
	IsDomainReloadInProgress bool            `json:"IsDomainReloadInProgress"`
	Result                   json.RawMessage `json:"Result"`
	Message                  string          `json:"Message"`
}

var queryCompileStatus = queryCompileStatusFromUnity

func shouldWaitForCompileDomainReload(command string, params map[string]any) bool {
	if command != compileCommandName {
		return false
	}
	return domainReloadWaitEnabled(params)
}

func prepareCompileWaitParams(params map[string]any) (string, error) {
	requestID, err := ensureCompileRequestID(params)
	if err != nil {
		return "", err
	}
	params[compileWaitParam] = true
	return requestID, nil
}

func ensureCompileRequestID(params map[string]any) (string, error) {
	if value, ok := params[compileRequestIDParam].(string); ok && value != "" {
		if isSafeCompileRequestID(value) {
			return value, nil
		}
	}

	requestID, err := createCompileRequestID()
	if err != nil {
		return "", err
	}
	params[compileRequestIDParam] = requestID
	return requestID, nil
}

func createCompileRequestID() (string, error) {
	var token [4]byte
	if _, err := rand.Read(token[:]); err != nil {
		return "", err
	}
	return fmt.Sprintf("compile_%d_%s", time.Now().UnixMilli(), hex.EncodeToString(token[:])), nil
}

func isSafeCompileRequestID(requestID string) bool {
	for _, r := range requestID {
		if r >= 'a' && r <= 'z' {
			continue
		}
		if r >= 'A' && r <= 'Z' {
			continue
		}
		if r >= '0' && r <= '9' {
			continue
		}
		if r == '_' || r == '-' {
			continue
		}
		return false
	}
	return true
}

func waitForCompileCompletion(ctx context.Context, options compileCompletionOptions) (json.RawMessage, bool, error) {
	startedAt := time.Now()
	deadline := startedAt.Add(options.timeout)
	nextProgressLogAt := startedAt.Add(compileWaitLogInterval)

	logCompileWaitStarted(options, startedAt)

	for {
		now := time.Now()
		if !now.Before(deadline) {
			break
		}

		status, err := queryCompileStatus(ctx, options.connection, options.requestID)
		if err == nil && status.Ready && status.HasResult && len(status.Result) > 0 {
			logCompileStatusResultAvailable(options, startedAt, status)
			return status.Result, true, nil
		}

		if !now.Before(nextProgressLogAt) {
			logCompileWaitPolling(options, startedAt, deadline, status, err)
			nextProgressLogAt = now.Add(compileWaitLogInterval)
		}

		select {
		case <-ctx.Done():
			logCompileWaitCancelled(options, startedAt, ctx.Err())
			return nil, false, ctx.Err()
		case <-time.After(options.pollInterval):
		}
	}

	logCompileWaitTimedOut(options, startedAt)
	return nil, false, nil
}

func compileForceRecompileEnabled(params map[string]any) bool {
	value, ok := params[compileForceParam].(bool)
	return ok && value
}

func queryCompileStatusFromUnity(ctx context.Context, connection unityipc.Connection, requestID string) (compileStatusResponse, error) {
	probeContext, cancel := context.WithTimeout(ctx, compileStatusProbeTimeout)
	defer cancel()

	response, err := unityipc.NewClient(connection, version).Send(
		probeContext,
		compileStatusCommandName,
		map[string]any{compileRequestIDParam: requestID},
	)
	if err != nil {
		return compileStatusResponse{}, err
	}

	var status compileStatusResponse
	if err := json.Unmarshal(response, &status); err != nil {
		return compileStatusResponse{}, err
	}
	return status, nil
}

func shouldWaitForCompileStatus(err error, outcome unityipc.UnitySendOutcome) bool {
	if err == nil {
		return true
	}
	if !outcome.RequestDispatched {
		return false
	}
	if isTransportDisconnectError(err) {
		return true
	}
	return outcome.RequestAccepted && isFinalResponseTimeoutError(err)
}

func isTransportDisconnectError(err error) bool {
	message := err.Error()
	return message == "UNITY_NO_RESPONSE" ||
		strings.Contains(message, "EOF") ||
		strings.Contains(message, "connection reset") ||
		strings.Contains(message, "broken pipe") ||
		strings.Contains(message, "use of closed network connection")
}

func isFinalResponseTimeoutError(err error) bool {
	return strings.Contains(err.Error(), "i/o timeout")
}

func logCompileWaitRequestPrepared(projectRoot string, requestID string) {
	_ = writeCliVibeLog(projectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_compile_wait_request_prepared",
		Message:   "Prepared compile request ID for status polling.",
		Context: map[string]any{
			"command":    compileCommandName,
			"request_id": requestID,
		},
		CorrelationID: requestID,
	})
}

func logCompileSendCompleted(
	connection unityipc.Connection,
	requestID string,
	outcome unityipc.UnitySendOutcome,
	sendErr error,
	elapsed time.Duration,
) {
	_ = writeCliVibeLog(connection.ProjectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_compile_send_completed",
		Message:   "Compile RPC returned to the CLI before status polling.",
		Context: map[string]any{
			"command":            compileCommandName,
			"request_id":         requestID,
			"endpoint":           connection.Endpoint.Address,
			"elapsed_ms":         elapsed.Milliseconds(),
			"request_dispatched": outcome.RequestDispatched,
			"request_accepted":   outcome.RequestAccepted,
			"error":              errorMessage(sendErr),
			"will_poll_status":   shouldWaitForCompileStatus(sendErr, outcome),
		},
		CorrelationID: requestID,
	})
}

func logCompileWaitStarted(options compileCompletionOptions, startedAt time.Time) {
	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:         "INFO",
		Operation:     "cli_compile_status_wait_started",
		Message:       "Started polling Unity compile status.",
		Context:       compileWaitLogContext(options, startedAt, nil),
		CorrelationID: options.requestID,
	})
}

func logCompileWaitPolling(
	options compileCompletionOptions,
	startedAt time.Time,
	deadline time.Time,
	status compileStatusResponse,
	statusErr error,
) {
	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_compile_status_wait_polling",
		Message:   "Still polling Unity compile status.",
		Context: compileWaitLogContext(options, startedAt, map[string]any{
			"ready":                        status.Ready,
			"has_result":                   status.HasResult,
			"is_compiling":                 status.IsCompiling,
			"is_updating":                  status.IsUpdating,
			"is_domain_reload_in_progress": status.IsDomainReloadInProgress,
			"status_error":                 errorMessage(statusErr),
			"remaining_timeout_ms":         remainingMilliseconds(deadline),
		}),
		CorrelationID: options.requestID,
	})
}

func logCompileStatusResultAvailable(
	options compileCompletionOptions,
	startedAt time.Time,
	status compileStatusResponse,
) {
	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_compile_status_result_available",
		Message:   "Unity compile status returned a ready result.",
		Context: compileWaitLogContext(options, startedAt, map[string]any{
			"ready":        status.Ready,
			"has_result":   status.HasResult,
			"result_bytes": len(status.Result),
		}),
		CorrelationID: options.requestID,
	})
}

func logCompileWaitTimedOut(options compileCompletionOptions, startedAt time.Time) {
	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:         "WARNING",
		Operation:     "cli_compile_status_wait_timed_out",
		Message:       "Timed out while polling Unity compile status.",
		Context:       compileWaitLogContext(options, startedAt, nil),
		CorrelationID: options.requestID,
	})
}

func logCompileWaitCancelled(options compileCompletionOptions, startedAt time.Time, err error) {
	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:     "WARNING",
		Operation: "cli_compile_status_wait_cancelled",
		Message:   "Compile status polling was cancelled.",
		Context: compileWaitLogContext(options, startedAt, map[string]any{
			"error": errorMessage(err),
		}),
		CorrelationID: options.requestID,
	})
}

func compileWaitLogContext(
	options compileCompletionOptions,
	startedAt time.Time,
	extra map[string]any,
) map[string]any {
	context := map[string]any{
		"command":          compileCommandName,
		"request_id":       options.requestID,
		"force_recompile":  options.forceRecompile,
		"endpoint":         options.connection.Endpoint.Address,
		"timeout_ms":       options.timeout.Milliseconds(),
		"poll_interval_ms": options.pollInterval.Milliseconds(),
		"elapsed_ms":       time.Since(startedAt).Milliseconds(),
	}
	for key, value := range extra {
		context[key] = value
	}
	return context
}

func remainingMilliseconds(deadline time.Time) int64 {
	remaining := time.Until(deadline).Milliseconds()
	if remaining < 0 {
		return 0
	}
	return remaining
}
