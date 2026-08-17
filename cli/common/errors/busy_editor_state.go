package clierrors

const unityServerBusyResponsivenessStallThresholdSeconds = 5.0

func unityServerBusyNextActions(data serverBusyErrorData) []string {
	actions := []string{
		"Wait for the running Unity command to complete.",
		"Retry the command after Unity reports it is no longer busy.",
	}

	if optionalTrueBool(data.IsCompiling) {
		actions = append(
			[]string{"Unity is compiling scripts; wait for compilation to finish before retrying."},
			actions...)
	} else if optionalTrueBool(data.IsUpdating) {
		actions = append(
			[]string{"Unity is importing assets; wait for the asset database update to finish before retrying."},
			actions...)
	}

	// Why only runningToolName == "compile": attach recovery requires a local pending
	// record from this client's own COMPILE_WAIT_TIMEOUT. Editor-state "unity-compile"
	// busy and other clients' compiles must not promise reattach.
	if data.RunningToolName == "compile" {
		actions = append(
			actions,
			"A compile can take several minutes on large projects. Wait for it to finish, then retry. If your own `uloop compile` previously failed with COMPILE_WAIT_TIMEOUT, re-running `uloop compile` reattaches to that compile instead of starting a new one.",
		)
	}

	if data.SecondsSinceLastMainThreadTick != nil &&
		*data.SecondsSinceLastMainThreadTick >= unityServerBusyResponsivenessStallThresholdSeconds {
		actions = append(
			actions,
			"Run a light command such as `uloop get-logs --max-count 1` to check whether Unity is still responsive before treating this as a freeze.")
	}

	return actions
}

func unityServerBusyEditorActivitySummary(data serverBusyErrorData) map[string]any {
	summary := map[string]any{}
	copyOptionalTrueBool(summary, "isCompiling", data.IsCompiling)
	copyOptionalTrueBool(summary, "isUpdating", data.IsUpdating)
	copyOptionalTrueBool(summary, "isPlaying", data.IsPlaying)
	copyOptionalTrueBool(summary, "isPaused", data.IsPaused)
	if data.SecondsSinceLastMainThreadTick != nil {
		summary["secondsSinceLastMainThreadTick"] = *data.SecondsSinceLastMainThreadTick
	}
	if len(summary) == 0 {
		return nil
	}
	return summary
}

func optionalTrueBool(value *bool) bool {
	return value != nil && *value
}

func copyOptionalTrueBool(destination map[string]any, key string, value *bool) {
	if value == nil || !*value {
		return
	}
	destination[key] = true
}
