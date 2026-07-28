package clierrors

const unityServerBusyResponsivenessStallThresholdSeconds = 5.0

func unityServerBusyNextActions(data map[string]any) []string {
	actions := []string{
		"Wait for the running Unity command to complete.",
		"Retry the command after Unity reports it is no longer busy.",
	}

	if rpcBoolData(data, "isCompiling") {
		actions = append(
			[]string{"Unity is compiling scripts; wait for compilation to finish before retrying."},
			actions...)
	} else if rpcBoolData(data, "isUpdating") {
		actions = append(
			[]string{"Unity is importing assets; wait for the asset database update to finish before retrying."},
			actions...)
	}

	// Why only runningToolName == "compile": attach recovery requires a local pending
	// record from this client's own COMPILE_WAIT_TIMEOUT. Editor-state "unity-compile"
	// busy and other clients' compiles must not promise reattach.
	if rpcStringData(data, "runningToolName") == "compile" {
		actions = append(
			actions,
			"A compile can take several minutes on large projects. Wait for it to finish, then retry. If your own `uloop compile` previously failed with COMPILE_WAIT_TIMEOUT, re-running `uloop compile` reattaches to that compile instead of starting a new one.",
		)
	}

	if stallSeconds, ok := rpcFloatData(data, "secondsSinceLastMainThreadTick"); ok &&
		stallSeconds >= unityServerBusyResponsivenessStallThresholdSeconds {
		actions = append(
			actions,
			"Run a light command such as `uloop get-logs --max-count 1` to check whether Unity is still responsive before treating this as a freeze.")
	}

	return actions
}

func unityServerBusyEditorActivitySummary(data map[string]any) map[string]any {
	if data == nil {
		return nil
	}

	summary := map[string]any{}
	copyOptionalBoolField(summary, data, "isCompiling")
	copyOptionalBoolField(summary, data, "isUpdating")
	copyOptionalBoolField(summary, data, "isPlaying")
	copyOptionalBoolField(summary, data, "isPaused")
	if stallSeconds, ok := rpcFloatData(data, "secondsSinceLastMainThreadTick"); ok {
		summary["secondsSinceLastMainThreadTick"] = stallSeconds
	}
	if len(summary) == 0 {
		return nil
	}
	return summary
}

func rpcBoolData(data map[string]any, key string) bool {
	value, ok := data[key].(bool)
	return ok && value
}

func rpcFloatData(data map[string]any, key string) (float64, bool) {
	value, ok := data[key].(float64)
	return value, ok
}

func copyOptionalBoolField(destination map[string]any, source map[string]any, key string) {
	value, ok := source[key].(bool)
	if !ok || !value {
		return
	}
	destination[key] = value
}
