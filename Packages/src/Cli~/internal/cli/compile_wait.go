package cli

import (
	"bytes"
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/unityipc"
)

const (
	compileCommandName       = "compile"
	compileRequestIDParam    = "RequestId"
	compileWaitParam         = domainReloadWaitParam
	compileResultRelativeDir = "Temp/UnityCliLoop/compile-results"
	compileWaitTimeout       = 90 * time.Second
	compileWaitPollInterval  = 50 * time.Millisecond
	compileLockGracePeriod   = 500 * time.Millisecond
	compileWaitLogInterval   = 5 * time.Second
)

type compileCompletionOptions struct {
	projectRoot  string
	requestID    string
	timeout      time.Duration
	pollInterval time.Duration
	lockGrace    time.Duration
}

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
	var idleSince time.Time

	logCompileWaitStarted(options, startedAt)

	for {
		now := time.Now()
		if !now.Before(deadline) {
			break
		}

		result, err := tryReadCompileResult(options.projectRoot, options.requestID)
		if err != nil {
			logCompileWaitReadFailed(options, startedAt, err)
			return nil, false, err
		}

		if len(result) > 0 {
			if idleSince.IsZero() {
				idleSince = now
				logCompileResultFileDetected(options, startedAt, len(result))
			}
			if time.Since(idleSince) >= options.lockGrace {
				logCompileResultFileStable(options, startedAt, len(result))
				return result, true, nil
			}
		} else {
			idleSince = time.Time{}
		}

		if !now.Before(nextProgressLogAt) {
			logCompileWaitPolling(options, startedAt, deadline, !idleSince.IsZero())
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

func tryReadCompileResult(projectRoot string, requestID string) (json.RawMessage, error) {
	if !isSafeCompileRequestID(requestID) {
		return nil, fmt.Errorf("requestId contains unsafe characters: %s", requestID)
	}

	resultPath := compileResultPath(projectRoot, requestID)
	content, err := os.ReadFile(resultPath)
	if err != nil {
		if os.IsNotExist(err) {
			return nil, nil
		}
		return nil, err
	}

	content = bytes.TrimPrefix(content, []byte{0xef, 0xbb, 0xbf})
	if !json.Valid(content) {
		return nil, nil
	}
	return json.RawMessage(content), nil
}

func compileResultPath(projectRoot string, requestID string) string {
	return filepath.Join(projectRoot, compileResultRelativeDir, requestID+".json")
}

func shouldWaitForCompileResult(err error, outcome unityipc.UnitySendOutcome) bool {
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
		Message:   "Prepared compile request ID for domain reload result polling.",
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
		Message:   "Compile RPC returned to the CLI before result-file polling.",
		Context: map[string]any{
			"command":            compileCommandName,
			"request_id":         requestID,
			"endpoint":           connection.Endpoint.Address,
			"elapsed_ms":         elapsed.Milliseconds(),
			"request_dispatched": outcome.RequestDispatched,
			"request_accepted":   outcome.RequestAccepted,
			"error":              errorMessage(sendErr),
			"will_poll_result":   shouldWaitForCompileResult(sendErr, outcome),
		},
		CorrelationID: requestID,
	})
}

func logCompileWaitStarted(options compileCompletionOptions, startedAt time.Time) {
	_ = writeCliVibeLog(options.projectRoot, cliVibeLogEntry{
		Level:         "INFO",
		Operation:     "cli_compile_result_wait_started",
		Message:       "Started polling for the Unity compile result file.",
		Context:       compileWaitLogContext(options, startedAt, nil),
		CorrelationID: options.requestID,
	})
}

func logCompileWaitPolling(
	options compileCompletionOptions,
	startedAt time.Time,
	deadline time.Time,
	resultVisible bool,
) {
	_ = writeCliVibeLog(options.projectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_compile_result_wait_polling",
		Message:   "Still polling for the Unity compile result file.",
		Context: compileWaitLogContext(options, startedAt, map[string]any{
			"result_visible":       resultVisible,
			"remaining_timeout_ms": remainingMilliseconds(deadline),
		}),
		CorrelationID: options.requestID,
	})
}

func logCompileResultFileDetected(options compileCompletionOptions, startedAt time.Time, resultBytes int) {
	_ = writeCliVibeLog(options.projectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_compile_result_file_detected",
		Message:   "Detected a valid Unity compile result file and started lock-grace verification.",
		Context: compileWaitLogContext(options, startedAt, map[string]any{
			"result_bytes": resultBytes,
		}),
		CorrelationID: options.requestID,
	})
}

func logCompileResultFileStable(options compileCompletionOptions, startedAt time.Time, resultBytes int) {
	_ = writeCliVibeLog(options.projectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_compile_result_file_stable",
		Message:   "Compile result file stayed readable through the lock-grace window.",
		Context: compileWaitLogContext(options, startedAt, map[string]any{
			"result_bytes": resultBytes,
		}),
		CorrelationID: options.requestID,
	})
}

func logCompileWaitTimedOut(options compileCompletionOptions, startedAt time.Time) {
	_ = writeCliVibeLog(options.projectRoot, cliVibeLogEntry{
		Level:         "WARNING",
		Operation:     "cli_compile_result_wait_timed_out",
		Message:       "Timed out while polling for the Unity compile result file.",
		Context:       compileWaitLogContext(options, startedAt, nil),
		CorrelationID: options.requestID,
	})
}

func logCompileWaitCancelled(options compileCompletionOptions, startedAt time.Time, err error) {
	_ = writeCliVibeLog(options.projectRoot, cliVibeLogEntry{
		Level:     "WARNING",
		Operation: "cli_compile_result_wait_cancelled",
		Message:   "Compile result-file polling was cancelled.",
		Context: compileWaitLogContext(options, startedAt, map[string]any{
			"error": errorMessage(err),
		}),
		CorrelationID: options.requestID,
	})
}

func logCompileWaitReadFailed(options compileCompletionOptions, startedAt time.Time, err error) {
	_ = writeCliVibeLog(options.projectRoot, cliVibeLogEntry{
		Level:     "WARNING",
		Operation: "cli_compile_result_wait_read_failed",
		Message:   "Failed while reading the Unity compile result file.",
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
		"result_path":      compileResultPath(options.projectRoot, options.requestID),
		"timeout_ms":       options.timeout.Milliseconds(),
		"poll_interval_ms": options.pollInterval.Milliseconds(),
		"lock_grace_ms":    options.lockGrace.Milliseconds(),
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
