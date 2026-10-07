package projectrunner

// CLI vibe log writers for the plain tool path: the request sent, and either the response received
// or the failure, joined through a shared correlation ID. They record parameter keys and sizes,
// never a parameter value or a response body, which can name files of the project or carry code.

import (
	"errors"
	"sort"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/vibelog"
)

const (
	plainToolRequestSentOperation      = "cli_tool_request_sent"
	plainToolResponseReceivedOperation = "cli_tool_response_received"
	plainToolRequestFailedOperation    = "cli_tool_request_failed"
)

// writePlainToolVibeLog mirrors writeCompileVibeLog: the entry is built only when the log is on,
// so a command run without ULOOP_DEBUG pays nothing for it.
func writePlainToolVibeLog(projectRoot string, buildEntry func() vibelog.CLIVibeLogEntry) {
	if !vibelog.IsCLIVibeLogEnabled() {
		return
	}

	_ = vibelog.WriteCLIVibeLog(projectRoot, buildEntry())
}

func logPlainToolRequestSent(connection unityipc.Connection, command string, params map[string]any, correlationID string) {
	writePlainToolVibeLog(connection.ProjectRoot, func() vibelog.CLIVibeLogEntry {
		return vibelog.CLIVibeLogEntry{
			Level:     "INFO",
			Operation: plainToolRequestSentOperation,
			Message:   "Sent the tool request to Unity.",
			Context: map[string]any{
				"command":          command,
				"correlation_id":   correlationID,
				"project_identity": vibelog.ProjectIdentity(connection.ProjectRoot),
				"cli_version":      clicontract.ProjectRunnerVersion(),
				"param_keys":       sortedParamKeys(params),
				"array_lengths":    paramArrayLengths(params),
			},
			CorrelationID: correlationID,
		}
	})
}

func logPlainToolResponseReceived(
	connection unityipc.Connection,
	command string,
	correlationID string,
	elapsed time.Duration,
	outcome unityipc.UnitySendOutcome,
	result []byte,
	exitCode int,
) {
	writePlainToolVibeLog(connection.ProjectRoot, func() vibelog.CLIVibeLogEntry {
		// Why INFO even for exit code 1: a tool that reports a failure in its response was still
		// reached and answered, and the response, not this log, says what failed.
		return vibelog.CLIVibeLogEntry{
			Level:     "INFO",
			Operation: plainToolResponseReceivedOperation,
			Message:   "Received the tool response from Unity.",
			Context: map[string]any{
				"command":          command,
				"correlation_id":   correlationID,
				"elapsed_ms":       elapsed.Milliseconds(),
				"request_accepted": outcome.RequestAccepted,
				"result_bytes":     len(result),
				"exit_code":        exitCode,
			},
			CorrelationID: correlationID,
		}
	})
}

func logPlainToolRequestFailed(
	connection unityipc.Connection,
	command string,
	correlationID string,
	elapsed time.Duration,
	outcome unityipc.UnitySendOutcome,
	err error,
) {
	writePlainToolVibeLog(connection.ProjectRoot, func() vibelog.CLIVibeLogEntry {
		// Why no err.Error(): a Unity parameter-validation RPC error quotes the raw parameter values
		// in its message, and this log must never carry parameter values.
		return vibelog.CLIVibeLogEntry{
			Level:     "ERROR",
			Operation: plainToolRequestFailedOperation,
			Message:   "The tool request to Unity failed.",
			Context: map[string]any{
				"command":          command,
				"correlation_id":   correlationID,
				"elapsed_ms":       elapsed.Milliseconds(),
				"request_accepted": outcome.RequestAccepted,
				"error_kind":       classifyPlainToolError(err),
			},
			CorrelationID: correlationID,
		}
	})
}

// classifyPlainToolError names what failed without any of the error's text: "rpc:" followed by the
// type Unity attached to its error (empty when it attached none), "final_response_timeout", or
// "other".
func classifyPlainToolError(err error) string {
	var rpcErr *unityipc.RPCError
	if errors.As(err, &rpcErr) {
		return "rpc:" + clierrors.RPCDataType(rpcErr.Data)
	}
	if clierrors.IsFinalResponseTimeoutError(err) {
		return "final_response_timeout"
	}
	return "other"
}

func sortedParamKeys(params map[string]any) []string {
	keys := make([]string, 0, len(params))
	for key := range params {
		keys = append(keys, key)
	}
	sort.Strings(keys)
	return keys
}

// Only the two shapes the CLI parses an array option into: a JSON array and a comma-separated list.
func paramArrayLengths(params map[string]any) map[string]int {
	lengths := map[string]int{}
	for key, value := range params {
		switch array := value.(type) {
		case []any:
			lengths[key] = len(array)
		case []string:
			lengths[key] = len(array)
		}
	}
	return lengths
}
