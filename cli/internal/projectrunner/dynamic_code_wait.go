package projectrunner

import (
	"encoding/json"

	"github.com/hatayama/unity-cli-loop/cli/internal/clicore"
	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

const (
	dynamicCodeCompileOnlyParam                    = "CompileOnly"
	dynamicCodeDomainReloadWaitRequiredField       = "DomainReloadWaitRequired"
	legacyDynamicCodeDomainReloadWaitRequiredField = "domainReloadWaitRequired"
)

func shouldWaitForExecuteDynamicCodeDomainReload(command string, params map[string]any) bool {
	if command != clicore.ExecuteDynamicCodeCommandName {
		return false
	}
	if compileOnly, ok := params[dynamicCodeCompileOnlyParam].(bool); ok && compileOnly {
		return false
	}
	return domainReloadWaitEnabled(params, false)
}

func domainReloadWaitEnabled(params map[string]any, defaultValue bool) bool {
	value, ok := params[clicore.DomainReloadWaitParam].(bool)
	if ok {
		return value
	}

	return defaultValue
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
	return clicore.IsTransportDisconnectError(err)
}

func stripExecuteDynamicCodeControlResult(result []byte) []byte {
	var payload map[string]any
	if err := json.Unmarshal(result, &payload); err != nil {
		return result
	}

	delete(payload, dynamicCodeDomainReloadWaitRequiredField)
	delete(payload, legacyDynamicCodeDomainReloadWaitRequiredField)
	sanitized, err := json.Marshal(payload)
	if err != nil {
		return result
	}
	return sanitized
}
