package cli

import (
	corecontract "github.com/hatayama/unity-cli-loop/Packages/src/Cli/Core"
	cliversion "github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/version"
)

func isRequiredDispatcherVersionRequest(args []string) bool {
	return len(args) == 1 && args[0] == corecontract.Current.RequiredDispatcherVersionFlag
}

func isDispatcherCompatible(dispatcherVersion string) bool {
	if dispatcherVersion == "" {
		return false
	}
	comparison, ok := cliversion.Compare(dispatcherVersion, corecontract.Current.MinimumRequiredDispatcherVersion)
	return ok && comparison >= 0
}

func dispatcherUpdateRequiredError(dispatcherVersion string, requiredVersion string, command string) cliError {
	if dispatcherVersion == "" {
		dispatcherVersion = "missing"
	}
	return cliError{
		ErrorCode:   errorCodeDispatcherUpdateRequired,
		Phase:       errorPhaseExecution,
		Message:     "The global uloop dispatcher is too old for this project-local uloop-core.",
		Retryable:   false,
		SafeToRetry: false,
		Command:     command,
		NextActions: []string{
			"Run `uloop update` to install a compatible global dispatcher.",
			"Alternatively, use Window > Unity CLI Loop > Setup Wizard and update the CLI.",
		},
		Details: map[string]any{
			"dispatcherVersion":         dispatcherVersion,
			"requiredDispatcherVersion": requiredVersion,
		},
	}
}
