package cli

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"strings"
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

type compileStatusPollState struct {
	attempts             int
	hasLastStatus        bool
	lastStatus           compileStatusResponse
	lastTransportError   string
	lastLogSignature     string
	bridgeRecoveryLogged bool
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
	pollState := compileStatusPollState{}
	logCompileStatusPollStart(options, startedAt, deadline)

	for {
		now := time.Now()
		if !now.Before(deadline) {
			break
		}

		pollState.attempts++
		status, err := queryCompileStatus(ctx, options.connection, options.requestID)
		updateCompileStatusPollState(options, startedAt, &pollState, status, err)
		if err == nil && status.Ready && status.HasResult && len(status.Result) > 0 {
			logCompileStatusPollComplete(options, startedAt, pollState, status)
			return status.Result, true, nil
		}

		select {
		case <-ctx.Done():
			logCompileStatusPollCancelled(options, startedAt, pollState, ctx.Err())
			return nil, false, ctx.Err()
		case <-time.After(options.pollInterval):
		}
	}

	logCompileStatusPollObserved(options, startedAt, pollState, true)
	logCompileStatusPollTimedOut(options, startedAt, pollState)
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

func logCliDebugModeResolved(connection unityipc.Connection, command string) {
	debugMode := resolveCliVibeDebugMode(connection.ProjectRoot)
	_ = writeCliVibeLog(connection.ProjectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_debug_mode_resolved",
		Message:   "Resolved CLI debug logging mode for this command.",
		Context: map[string]any{
			"command":          command,
			"debug_enabled":    debugMode.enabled,
			"debug_source":     string(debugMode.source),
			"project_identity": projectIdentity(connection.ProjectRoot),
			"cli_version":      version,
		},
	})
}

func logCompileRequestPrepared(connection unityipc.Connection, requestID string, params map[string]any) {
	_ = writeCliVibeLog(connection.ProjectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_compile_request_prepared",
		Message:   "Prepared Unity compile request for domain reload polling.",
		Context: map[string]any{
			"request_id":                     requestID,
			"command":                        compileCommandName,
			"wait_for_domain_reload":         true,
			"force_recompile":                compileForceRecompileEnabled(params),
			"stop_on_external_scene_changes": compileStopOnExternalSceneChangesEnabled(params),
			"project_identity":               projectIdentity(connection.ProjectRoot),
			"endpoint":                       connection.Endpoint.Address,
			"timeout_ms":                     compileWaitTimeout.Milliseconds(),
			"poll_interval_ms":               compileWaitPollInterval.Milliseconds(),
		},
		CorrelationID: requestID,
	})
}

func logCompileRequestSendResult(
	connection unityipc.Connection,
	requestID string,
	outcome unityipc.UnitySendOutcome,
	err error,
	elapsed time.Duration,
) {
	responseTimeout := err != nil && isFinalResponseTimeoutError(err)
	_ = writeCliVibeLog(connection.ProjectRoot, cliVibeLogEntry{
		Level:     compileRequestSendResultLevel(err),
		Operation: "cli_compile_request_send_result",
		Message:   "Recorded Unity compile request send result.",
		Context: map[string]any{
			"request_id":         requestID,
			"request_dispatched": outcome.RequestDispatched,
			"request_accepted":   outcome.RequestAccepted,
			"response_received":  err == nil && len(outcome.Result) > 0,
			"response_timeout":   responseTimeout,
			"transport_error":    errorMessage(err),
			"elapsed_ms":         elapsed.Milliseconds(),
		},
		CorrelationID: requestID,
	})
}

func compileRequestSendResultLevel(err error) string {
	if err == nil {
		return "INFO"
	}
	return "WARNING"
}

func compileStopOnExternalSceneChangesEnabled(params map[string]any) bool {
	value, ok := params[reloadExternalSceneChangesPropertyName].(bool)
	return ok && !value
}

func logCompileStatusPollStart(options compileCompletionOptions, startedAt time.Time, deadline time.Time) {
	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_compile_status_poll_start",
		Message:   "Started polling Unity compile status.",
		Context: map[string]any{
			"command":          compileCommandName,
			"request_id":       options.requestID,
			"started_at":       startedAt.UTC().Format(time.RFC3339Nano),
			"deadline_at":      deadline.UTC().Format(time.RFC3339Nano),
			"endpoint":         options.connection.Endpoint.Address,
			"timeout_ms":       options.timeout.Milliseconds(),
			"poll_interval_ms": options.pollInterval.Milliseconds(),
			"project_identity": projectIdentity(options.connection.ProjectRoot),
		},
		CorrelationID: options.requestID,
	})
}

func updateCompileStatusPollState(
	options compileCompletionOptions,
	startedAt time.Time,
	pollState *compileStatusPollState,
	status compileStatusResponse,
	err error,
) {
	if err == nil {
		if pollState.lastTransportError != "" && !pollState.bridgeRecoveryLogged {
			logCompileBridgeRecoveryObserved(options, startedAt)
			pollState.bridgeRecoveryLogged = true
		}
		pollState.hasLastStatus = true
		pollState.lastStatus = status
		pollState.lastTransportError = ""
	} else {
		pollState.lastTransportError = errorMessage(err)
	}

	signature := compileStatusPollSignature(*pollState)
	if signature == pollState.lastLogSignature {
		return
	}
	pollState.lastLogSignature = signature
	logCompileStatusPollObserved(options, startedAt, *pollState, false)
}

func compileStatusPollSignature(pollState compileStatusPollState) string {
	payload, err := json.Marshal(map[string]any{
		"has_last_status":      pollState.hasLastStatus,
		"last_status":          compileStatusLogContext(pollState.lastStatus),
		"last_transport_error": pollState.lastTransportError,
	})
	if err != nil {
		return pollState.lastTransportError
	}
	return string(payload)
}

func logCompileStatusPollObserved(
	options compileCompletionOptions,
	startedAt time.Time,
	pollState compileStatusPollState,
	finalBeforeTimeout bool,
) {
	context := compileStatusLogContext(pollState.lastStatus)
	context["command"] = compileCommandName
	context["request_id"] = options.requestID
	context["attempt"] = pollState.attempts
	context["transport_error"] = pollState.lastTransportError
	context["elapsed_ms"] = time.Since(startedAt).Milliseconds()
	if finalBeforeTimeout {
		context["final_before_timeout"] = true
	}
	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:         compileStatusObservationLevel(pollState),
		Operation:     "cli_compile_status_poll_observed",
		Message:       "Observed Unity compile status while polling.",
		Context:       context,
		CorrelationID: options.requestID,
	})
}

func compileStatusObservationLevel(pollState compileStatusPollState) string {
	if pollState.lastTransportError == "" {
		return "INFO"
	}
	return "WARNING"
}

func logCompileStatusPollComplete(
	options compileCompletionOptions,
	startedAt time.Time,
	pollState compileStatusPollState,
	status compileStatusResponse,
) {
	context := compileResultLogSummary(status.Result)
	context["command"] = compileCommandName
	context["request_id"] = options.requestID
	context["elapsed_ms"] = time.Since(startedAt).Milliseconds()
	context["poll_attempts"] = pollState.attempts
	context["last_status"] = compileStatusLogContext(status)
	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:         "INFO",
		Operation:     "cli_compile_status_poll_complete",
		Message:       "Unity compile result became available.",
		Context:       context,
		CorrelationID: options.requestID,
	})
}

func logCompileStatusPollTimedOut(
	options compileCompletionOptions,
	startedAt time.Time,
	pollState compileStatusPollState,
) {
	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:         "WARNING",
		Operation:     "cli_compile_status_poll_timeout",
		Message:       "Timed out while polling Unity compile status.",
		Context:       compileWaitLogContext(options, startedAt, pollState, nil),
		CorrelationID: options.requestID,
	})
}

func logCompileStatusPollCancelled(
	options compileCompletionOptions,
	startedAt time.Time,
	pollState compileStatusPollState,
	err error,
) {
	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:     "WARNING",
		Operation: "cli_compile_status_poll_cancelled",
		Message:   "Compile status polling was cancelled.",
		Context: compileWaitLogContext(options, startedAt, pollState, map[string]any{
			"cancel_error": errorMessage(err),
		}),
		CorrelationID: options.requestID,
	})
}

func logCompileBridgeRecoveryObserved(options compileCompletionOptions, startedAt time.Time) {
	_ = writeCliVibeLog(options.connection.ProjectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_compile_bridge_rebind_observed",
		Message:   "Unity compile status polling recovered after a transport error.",
		Context: map[string]any{
			"command":      compileCommandName,
			"request_id":   options.requestID,
			"old_endpoint": options.connection.Endpoint.Address,
			"new_endpoint": options.connection.Endpoint.Address,
			"elapsed_ms":   time.Since(startedAt).Milliseconds(),
		},
		CorrelationID: options.requestID,
	})
}

func compileWaitLogContext(
	options compileCompletionOptions,
	startedAt time.Time,
	pollState compileStatusPollState,
	extra map[string]any,
) map[string]any {
	context := map[string]any{
		"command":              compileCommandName,
		"request_id":           options.requestID,
		"force_recompile":      options.forceRecompile,
		"endpoint":             options.connection.Endpoint.Address,
		"timeout_ms":           options.timeout.Milliseconds(),
		"poll_interval_ms":     options.pollInterval.Milliseconds(),
		"elapsed_ms":           time.Since(startedAt).Milliseconds(),
		"poll_attempts":        pollState.attempts,
		"last_status":          compileStatusLogContext(pollState.lastStatus),
		"last_transport_error": pollState.lastTransportError,
		"project_identity":     projectIdentity(options.connection.ProjectRoot),
	}
	for key, value := range extra {
		context[key] = value
	}
	return context
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

func compileResultLogSummary(result json.RawMessage) map[string]any {
	context := map[string]any{
		"success":       nil,
		"error_count":   nil,
		"warning_count": nil,
	}
	if len(result) == 0 {
		return context
	}

	var payload map[string]any
	if err := json.Unmarshal(result, &payload); err != nil {
		return context
	}
	context["success"] = payload["Success"]
	context["error_count"] = payload["ErrorCount"]
	context["warning_count"] = payload["WarningCount"]
	return context
}
