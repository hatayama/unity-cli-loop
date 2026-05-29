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
)

var compileFinalResponseTimeout = compileWaitTimeout

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
	deadline := time.Now().Add(options.timeout)
	var idleSince time.Time

	for time.Now().Before(deadline) {
		result, err := tryReadCompileResult(options.projectRoot, options.requestID)
		if err != nil {
			return nil, false, err
		}

		if len(result) > 0 {
			if idleSince.IsZero() {
				idleSince = time.Now()
			}
			if time.Since(idleSince) >= options.lockGrace {
				return result, true, nil
			}
		} else {
			idleSince = time.Time{}
		}

		select {
		case <-ctx.Done():
			return nil, false, ctx.Err()
		case <-time.After(options.pollInterval):
		}
	}

	return nil, false, nil
}

func tryReadCompileResult(projectRoot string, requestID string) (json.RawMessage, error) {
	if !isSafeCompileRequestID(requestID) {
		return nil, fmt.Errorf("requestId contains unsafe characters: %s", requestID)
	}

	resultPath := filepath.Join(projectRoot, compileResultRelativeDir, requestID+".json")
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
