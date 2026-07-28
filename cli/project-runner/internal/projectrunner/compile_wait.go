package projectrunner

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"math"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	compileStatusCommandName = "get-compile-status"
	compileRequestIDParam    = "RequestId"
	compileWaitParam         = clicore.DomainReloadWaitParam
	compileForceParam        = "ForceRecompile"
	compileWaitTimeoutParam  = "CompileWaitTimeoutSeconds"
	// Why separate from ToolReadinessTimeout (180s): launch readiness stays short.
	// Why 10m: worst-case blind block beats headroom. Why ≤ C# CompileResultLifetime (20m):
	// timed-out clients can still retrieve results by retrying uloop compile ~10m more.
	compileWaitTimeout = 10 * time.Minute
	// Why warn at 20m: Unity stores compile results for CompileResultLifetime (20m).
	// Waiting longer does not fail, but a timed-out retry may miss the retained result.
	compileWaitTimeoutRetentionWarningSeconds = 1200
	// Why: time.Duration is int64 nanoseconds. Values above this overflow to a negative
	// duration when multiplied by time.Second, which would look like an immediate timeout.
	// This is an overflow guard, not a product-imposed maximum wait.
	compileWaitTimeoutMaxSeconds = int64(math.MaxInt64 / int64(time.Second))
	compileWaitPollInterval      = clicore.ToolReadinessPoll
	compileStatusProbeTimeout    = clicore.ToolReadinessProbeTimeout
	compileResponseTimeout       = 2 * time.Second
)

type compileCompletionOptions struct {
	connection     unityipc.Connection
	requestID      string
	forceRecompile bool
	timeout        time.Duration
	pollInterval   time.Duration
}

type compileStatusResponse struct {
	Success                  bool            `json:"Success"`
	Ready                    bool            `json:"Ready"`
	HasResult                bool            `json:"HasResult"`
	IsCompiling              bool            `json:"IsCompiling"`
	IsUpdating               bool            `json:"IsUpdating"`
	IsDomainReloadInProgress bool            `json:"IsDomainReloadInProgress"`
	Result                   json.RawMessage `json:"Result"`
	Message                  string          `json:"Message"`
}

func shouldWaitForCompileDomainReload(command string, params map[string]any) bool {
	if command != clicore.CompileCommandName {
		return false
	}
	return domainReloadWaitEnabled(params, true)
}

func prepareCompileWaitParams(params map[string]any) (string, error) {
	requestID, err := ensureCompileRequestID(params)
	if err != nil {
		return "", err
	}
	params[compileWaitParam] = true
	return requestID, nil
}

// compileWaitTimeoutFromParams reads CompileWaitTimeoutSeconds from tool params.
// Missing values keep the default compileWaitTimeout (10m). Non-positive or non-integer
// values are rejected before a compile request is sent.
func compileWaitTimeoutFromParams(params map[string]any) (time.Duration, error) {
	value, exists := params[compileWaitTimeoutParam]
	if !exists || value == nil {
		return compileWaitTimeout, nil
	}

	seconds, ok := positiveInt64FromAny(value)
	if !ok || seconds > compileWaitTimeoutMaxSeconds {
		return 0, clierrors.InvalidValueArgumentError(
			"--compile-wait-timeout-seconds",
			fmt.Sprint(value),
			"positive integer",
		)
	}
	return time.Duration(seconds) * time.Second, nil
}

func positiveInt64FromAny(value any) (int64, bool) {
	switch typed := value.(type) {
	case int:
		if typed <= 0 {
			return 0, false
		}
		return int64(typed), true
	case int32:
		if typed <= 0 {
			return 0, false
		}
		return int64(typed), true
	case int64:
		if typed <= 0 {
			return 0, false
		}
		return typed, true
	case float64:
		if typed <= 0 || typed != math.Trunc(typed) || typed > float64(compileWaitTimeoutMaxSeconds) {
			return 0, false
		}
		return int64(typed), true
	case json.Number:
		parsed, err := typed.Int64()
		if err != nil || parsed <= 0 {
			return 0, false
		}
		return parsed, true
	default:
		return 0, false
	}
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

func waitForCompileCompletionWithDeps(
	ctx context.Context,
	options compileCompletionOptions,
	deps compileWaitDeps,
) (json.RawMessage, bool, *compileStatusResponse, error) {
	startedAt := time.Now()
	deadline := startedAt.Add(options.timeout)
	attempts := 0
	var lastStatus compileStatusResponse
	observedStatus := false
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
		status, err := deps.queryCompileStatus(ctx, options.connection, options.requestID)
		lastErr = err
		if err == nil && status.Ready && status.HasResult && len(status.Result) > 0 {
			logCompileStatusPollObservedIfChanged(options, startedAt, attempts, status, nil, &lastObservationKey)
			logCompileStatusPollComplete(options, startedAt, attempts, status)
			return status.Result, true, nil, nil
		}
		if err == nil {
			lastStatus = status
			observedStatus = true
		}
		logCompileStatusPollObservedIfChanged(options, startedAt, attempts, status, err, &lastObservationKey)

		select {
		case <-ctx.Done():
			logCompileWaitCancelled(options, startedAt, attempts, lastStatus, lastErr, ctx.Err())
			return nil, false, lastObservedCompileStatus(lastStatus, observedStatus), ctx.Err()
		case <-ticker.C:
		}
	}

	logCompileWaitTimedOut(options, startedAt, attempts, lastStatus, lastErr)
	return nil, false, lastObservedCompileStatus(lastStatus, observedStatus), nil
}

func lastObservedCompileStatus(status compileStatusResponse, observed bool) *compileStatusResponse {
	if !observed {
		return nil
	}
	copied := status
	return &copied
}

func compileForceRecompileEnabled(params map[string]any) bool {
	value, ok := params[compileForceParam].(bool)
	return ok && value
}

func compileReloadExternalSceneChangesEnabled(params map[string]any) bool {
	value, ok := params[tooldocs.ReloadExternalSceneChangesPropertyName].(bool)
	if !ok {
		return true
	}
	return value
}

func queryCompileStatusFromUnity(ctx context.Context, connection unityipc.Connection, requestID string) (compileStatusResponse, error) {
	probeContext, cancel := context.WithTimeout(ctx, compileStatusProbeTimeout)
	defer cancel()

	response, err := unityipc.NewClient(connection, clicontract.ProjectRunnerVersion()).Send(
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
	if clierrors.IsTransportDisconnectError(err) {
		return true
	}
	return outcome.RequestAccepted && clierrors.IsFinalResponseTimeoutError(err)
}
