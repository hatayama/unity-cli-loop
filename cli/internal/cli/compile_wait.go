package cli

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"time"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
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
	attempts := 0
	var lastStatus compileStatusResponse
	var lastErr error
	lastObservationKey := ""

	logCompileStatusPollStart(options, startedAt, deadline)

	ticker := time.NewTicker(options.pollInterval)
	defer ticker.Stop()
	for {
		now := time.Now()
		if !now.Before(deadline) {
			break
		}

		attempts++
		status, err := queryCompileStatus(ctx, options.connection, options.requestID)
		lastErr = err
		if err == nil && status.Ready && status.HasResult && len(status.Result) > 0 {
			logCompileStatusPollObservedIfChanged(options, startedAt, attempts, status, nil, &lastObservationKey)
			logCompileStatusPollComplete(options, startedAt, attempts, status)
			return status.Result, true, nil
		}
		if err == nil {
			lastStatus = status
		}
		logCompileStatusPollObservedIfChanged(options, startedAt, attempts, status, err, &lastObservationKey)

		select {
		case <-ctx.Done():
			logCompileWaitCancelled(options, startedAt, attempts, lastStatus, lastErr, ctx.Err())
			return nil, false, ctx.Err()
		case <-ticker.C:
		}
	}

	logCompileWaitTimedOut(options, startedAt, attempts, lastStatus, lastErr)
	return nil, false, nil
}

func compileForceRecompileEnabled(params map[string]any) bool {
	value, ok := params[compileForceParam].(bool)
	return ok && value
}

func compileReloadExternalSceneChangesEnabled(params map[string]any) bool {
	value, ok := params[reloadExternalSceneChangesPropertyName].(bool)
	if !ok {
		return true
	}
	return value
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

func logCliDebugModeResolved(connection unityipc.Connection, command string) {
	if !isCliVibeLogEnabled() {
		return
	}

	_ = writeCliVibeLog(connection.ProjectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_debug_mode_resolved",
		Message:   "Resolved CLI debug mode for the command.",
		Context: map[string]any{
			"command":          command,
			"debug_enabled":    true,
			"debug_source":     "env",
			"project_identity": projectIdentity(connection.ProjectRoot),
			"cli_version":      version,
		},
	})
}

func logCompileRequestPrepared(
	connection unityipc.Connection,
	params map[string]any,
	requestID string,
) {
	if !isCliVibeLogEnabled() {
		return
	}

	reloadExternalSceneChanges := compileReloadExternalSceneChangesEnabled(params)
	_ = writeCliVibeLog(connection.ProjectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_compile_request_prepared",
		Message:   "Prepared compile request parameters before dispatch.",
		Context: map[string]any{
			"command":                        compileCommandName,
			"request_id":                     requestID,
			"wait_for_domain_reload":         true,
			"force_recompile":                compileForceRecompileEnabled(params),
			"reload_external_scene_changes":  reloadExternalSceneChanges,
			"stop_on_external_scene_changes": !reloadExternalSceneChanges,
			"project_identity":               projectIdentity(connection.ProjectRoot),
			"endpoint":                       connection.Endpoint.Address,
			"timeout_ms":                     compileWaitTimeout.Milliseconds(),
			"poll_interval_ms":               compileWaitPollInterval.Milliseconds(),
			"response_timeout_ms":            compileResponseTimeout.Milliseconds(),
		},
		CorrelationID: requestID,
	})
}

func logCompileRequestSendResult(
	connection unityipc.Connection,
	requestID string,
	outcome unityipc.UnitySendOutcome,
	err error,
	startedAt time.Time,
) {
	if !isCliVibeLogEnabled() {
		return
	}

	_ = writeCliVibeLog(connection.ProjectRoot, cliVibeLogEntry{
		Level:     compileRequestSendResultLogLevel(err),
		Operation: "cli_compile_request_send_result",
		Message:   "Recorded compile request dispatch outcome before status polling.",
		Context: map[string]any{
			"command":            compileCommandName,
			"request_id":         requestID,
			"request_dispatched": outcome.RequestDispatched,
			"request_accepted":   outcome.RequestAccepted,
			"response_received":  err == nil && len(outcome.Result) > 0,
			"response_timeout":   err != nil && isFinalResponseTimeoutError(err),
			"transport_error":    errorMessage(err),
			"elapsed_ms":         time.Since(startedAt).Milliseconds(),
			"endpoint":           connection.Endpoint.Address,
			"project_identity":   projectIdentity(connection.ProjectRoot),
			"outcome_total_ms":   outcome.Timing.Total.Milliseconds(),
			"outcome_dial_ms":    outcome.Timing.Dial.Milliseconds(),
			"outcome_write_ms":   outcome.Timing.Write.Milliseconds(),
			"outcome_read_ms":    outcome.Timing.Read.Milliseconds(),
			"outcome_decode_ms":  outcome.Timing.Decode.Milliseconds(),
		},
		CorrelationID: requestID,
	})
}

func compileRequestSendResultLogLevel(err error) string {
	if err == nil {
		return "INFO"
	}
	return "WARNING"
}

func logCompileStatusPollStart(
	options compileCompletionOptions,
	startedAt time.Time,
	deadline time.Time,
) {
	if !isCliVibeLogEnabled() {
		return
	}

	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_compile_status_poll_start",
		Message:   "Started polling Unity compile status.",
		Context: compileWaitLogContext(options, startedAt, map[string]any{
			"started_at":       startedAt.UTC().Format(time.RFC3339Nano),
			"deadline_at":      deadline.UTC().Format(time.RFC3339Nano),
			"project_identity": projectIdentity(options.connection.ProjectRoot),
		}),
		CorrelationID: options.requestID,
	})
}

func logCompileStatusPollObservedIfChanged(
	options compileCompletionOptions,
	startedAt time.Time,
	attempt int,
	status compileStatusResponse,
	err error,
	lastObservationKey *string,
) {
	if !isCliVibeLogEnabled() {
		return
	}

	observationKey := compileStatusObservationKey(status, err)
	if observationKey == *lastObservationKey {
		return
	}

	*lastObservationKey = observationKey
	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:     compileStatusPollObservedLogLevel(err),
		Operation: "cli_compile_status_poll_observed",
		Message:   "Observed Unity compile status while polling.",
		Context: compileWaitLogContext(options, startedAt, map[string]any{
			"attempt":                      attempt,
			"ready":                        status.Ready,
			"has_result":                   status.HasResult,
			"is_compiling":                 status.IsCompiling,
			"is_updating":                  status.IsUpdating,
			"is_domain_reload_in_progress": status.IsDomainReloadInProgress,
			"message":                      status.Message,
			"transport_error":              errorMessage(err),
		}),
		CorrelationID: options.requestID,
	})
}

func compileStatusPollObservedLogLevel(err error) string {
	if err == nil {
		return "INFO"
	}
	return "WARNING"
}

func compileStatusObservationKey(status compileStatusResponse, err error) string {
	return fmt.Sprintf(
		"%t|%t|%t|%t|%t|%s|%s",
		status.Ready,
		status.HasResult,
		status.IsCompiling,
		status.IsUpdating,
		status.IsDomainReloadInProgress,
		status.Message,
		errorMessage(err),
	)
}

func logCompileStatusPollComplete(
	options compileCompletionOptions,
	startedAt time.Time,
	attempts int,
	status compileStatusResponse,
) {
	if !isCliVibeLogEnabled() {
		return
	}

	summary := compileResultLogSummary(status.Result)
	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_compile_status_poll_complete",
		Message:   "Unity compile status polling returned the stored result.",
		Context: compileWaitLogContext(options, startedAt, map[string]any{
			"poll_attempts": attempts,
			"success":       summary.success,
			"error_count":   summary.errorCount,
			"warning_count": summary.warningCount,
		}),
		CorrelationID: options.requestID,
	})
}

func logCompileWaitTimedOut(
	options compileCompletionOptions,
	startedAt time.Time,
	attempts int,
	lastStatus compileStatusResponse,
	lastErr error,
) {
	if !isCliVibeLogEnabled() {
		return
	}

	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:     "WARNING",
		Operation: "cli_compile_status_poll_timeout",
		Message:   "Timed out while polling Unity compile status.",
		Context: compileWaitLogContext(options, startedAt, map[string]any{
			"poll_attempts":        attempts,
			"last_status":          compileStatusLogContext(lastStatus),
			"last_transport_error": errorMessage(lastErr),
			"project_identity":     projectIdentity(options.connection.ProjectRoot),
		}),
		CorrelationID: options.requestID,
	})
}

func logCompileWaitCancelled(
	options compileCompletionOptions,
	startedAt time.Time,
	attempts int,
	lastStatus compileStatusResponse,
	lastErr error,
	cancelErr error,
) {
	if !isCliVibeLogEnabled() {
		return
	}

	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:     "WARNING",
		Operation: "cli_compile_status_poll_cancelled",
		Message:   "Compile status polling was cancelled.",
		Context: compileWaitLogContext(options, startedAt, map[string]any{
			"poll_attempts":        attempts,
			"last_status":          compileStatusLogContext(lastStatus),
			"last_transport_error": errorMessage(lastErr),
			"cancel_error":         errorMessage(cancelErr),
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

type compileResultSummary struct {
	success      any
	errorCount   any
	warningCount any
}

func compileResultLogSummary(result json.RawMessage) compileResultSummary {
	var payload map[string]any
	if err := json.Unmarshal(result, &payload); err != nil {
		return compileResultSummary{}
	}
	return compileResultSummary{
		success:      payload["Success"],
		errorCount:   payload["ErrorCount"],
		warningCount: payload["WarningCount"],
	}
}

func compileStatusLogContext(status compileStatusResponse) map[string]any {
	return map[string]any{
		"ready":                        status.Ready,
		"has_result":                   status.HasResult,
		"is_compiling":                 status.IsCompiling,
		"is_updating":                  status.IsUpdating,
		"is_domain_reload_in_progress": status.IsDomainReloadInProgress,
		"message":                      status.Message,
	}
}
