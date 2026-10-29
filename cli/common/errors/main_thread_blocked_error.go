package clierrors

import (
	"fmt"
	"strconv"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// mainThreadBlockedError reports that Unity stopped waiting for its blocked main thread before
// the tool's code ran. Why safe to retry: the tool never started, so nothing happened twice.
func mainThreadBlockedError(
	rpcErr *unityipc.RPCError,
	details map[string]any,
	data mainThreadBlockedErrorData,
	context ErrorContext,
) CLIError {
	toolName := firstNonEmpty(data.RunningToolName, context.Command)
	message := fmt.Sprintf(
		"'%s' was not run: Unity's main thread has not run for %s, so uloop stopped waiting and released the command slot. A modal dialog, a long import, or running code is blocking it.",
		toolName,
		stallText(data.StallSeconds))
	details["RunningToolName"] = toolName
	if data.StallSeconds != nil {
		details["StallSeconds"] = *data.StallSeconds
	}

	return CLIError{
		ErrorCode:   errorCodeMainThreadBlocked,
		Phase:       ErrorPhaseResponseWaiting,
		Message:     message,
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: []string{
			"Check the Unity window for a modal dialog or a long operation that is blocking the Editor main thread.",
			ApiUpdateConsentModalNextAction,
			"Run `uloop focus-window` if Unity is hidden behind another window.",
			"Run `uloop status`; when it reports Ready, run the command once more. Do not retry in a loop while it reports MainThreadBlocked.",
			"If no dialog is shown and the Editor stays blocked, restart it with `uloop launch -r`.",
		},
		Details: details,
	}
}

// stallText names the stall Unity reported, or the threshold it stops at when an older payload
// carried no value.
func stallText(stallSeconds *float64) string {
	if stallSeconds == nil {
		return "30s or more"
	}
	return strconv.FormatFloat(*stallSeconds, 'f', 0, 64) + "s"
}
