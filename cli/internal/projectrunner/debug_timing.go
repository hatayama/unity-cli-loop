package projectrunner

import (
	"encoding/json"
	"fmt"
	"io"
	"os"
	"strings"
	"time"

	"github.com/hatayama/unity-cli-loop/cli/internal/clicore"
	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

const (
	debugTimingEnvName                 = "ULOOP_DEBUG_TIMING"
	dynamicCodeIncludeTimingsParamName = "IncludeTimings"
)

func writeDebugTiming(writer io.Writer, command string, total time.Duration, outcome unityipc.UnitySendOutcome) {
	if !isDebugTimingEnabled() {
		return
	}

	timing := outcome.Timing
	_, _ = fmt.Fprintf(
		writer,
		"[uloop timing] command=%s total=%s rpc_total=%s dial=%s write=%s read=%s decode=%s\n",
		command,
		formatDebugDuration(total),
		formatDebugDuration(timing.Total),
		formatDebugDuration(timing.Dial),
		formatDebugDuration(timing.Write),
		formatDebugDuration(timing.Read),
		formatDebugDuration(timing.Decode),
	)
	for _, unityTiming := range extractUnityDebugTimings(command, outcome.Result) {
		_, _ = fmt.Fprintf(writer, "[uloop timing] unity %s\n", unityTiming)
	}
}

func applyDebugTimingParams(command string, params map[string]any) {
	if command != clicore.ExecuteDynamicCodeCommandName || !isDebugTimingEnabled() {
		return
	}

	params[dynamicCodeIncludeTimingsParamName] = true
}

func stripDebugTimingResult(command string, result json.RawMessage) json.RawMessage {
	if command != clicore.ExecuteDynamicCodeCommandName || !isDebugTimingEnabled() {
		return result
	}

	var payload map[string]any
	if err := json.Unmarshal(result, &payload); err != nil {
		return result
	}

	delete(payload, "timings")
	delete(payload, "Timings")
	sanitized, err := json.Marshal(payload)
	if err != nil {
		return result
	}
	return sanitized
}

func extractUnityDebugTimings(command string, result json.RawMessage) []string {
	if command != clicore.ExecuteDynamicCodeCommandName {
		return nil
	}

	var payload struct {
		Timings []string `json:"Timings"`
	}
	if err := json.Unmarshal(result, &payload); err != nil {
		return nil
	}
	return payload.Timings
}

func isDebugTimingEnabled() bool {
	value := strings.TrimSpace(os.Getenv(debugTimingEnvName))
	if value == "" || value == "0" {
		return false
	}
	return !strings.EqualFold(value, "false")
}

func formatDebugDuration(duration time.Duration) string {
	return duration.Round(time.Microsecond).String()
}
