package cli

import (
	"encoding/json"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

const (
	domainReloadWaitParam                    = "WaitForDomainReload"
	dynamicCodeCompileOnlyParam              = "CompileOnly"
	dynamicCodeDomainReloadWaitRequiredField = "DomainReloadWaitRequired"
)

func shouldWaitForExecuteDynamicCodeDomainReload(command string, params map[string]any) bool {
	if command != executeDynamicCodeCommandName {
		return false
	}
	if compileOnly, ok := params[dynamicCodeCompileOnlyParam].(bool); ok && compileOnly {
		return false
	}
	return domainReloadWaitEnabled(params)
}

func domainReloadWaitEnabled(params map[string]any) bool {
	value, ok := params[domainReloadWaitParam].(bool)
	if ok {
		return value
	}

	// Why: user-facing Unity mutation commands are checkpoints; returning only
	// after reload recovery avoids handing the next tool a half-reset editor.
	return true
}

func executeDynamicCodeDomainReloadWaitRequired(result []byte) bool {
	var payload struct {
		DomainReloadWaitRequired bool `json:"DomainReloadWaitRequired"`
	}
	if err := json.Unmarshal(result, &payload); err != nil {
		return false
	}
	return payload.DomainReloadWaitRequired
}

func shouldWaitForExecuteDynamicCodeDisconnect(err error, outcome unityipc.UnitySendOutcome) bool {
	if err == nil {
		return false
	}
	if !outcome.RequestDispatched {
		return false
	}
	return isTransportDisconnectError(err)
}

func stripExecuteDynamicCodeControlResult(result []byte) []byte {
	var payload map[string]any
	if err := json.Unmarshal(result, &payload); err != nil {
		return result
	}

	delete(payload, dynamicCodeDomainReloadWaitRequiredField)
	sanitized, err := json.Marshal(payload)
	if err != nil {
		return result
	}
	return sanitized
}
