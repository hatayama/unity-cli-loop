package cli

import (
	"fmt"
	"io"
	"os"
	"strings"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/domain"
)

const debugTimingEnvName = "ULOOP_DEBUG_TIMING"

func writeDebugTiming(writer io.Writer, command string, total time.Duration, outcome domain.UnitySendOutcome) {
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
