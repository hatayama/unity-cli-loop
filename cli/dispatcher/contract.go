// Package dispatchercontract holds the dispatcher-side release contract. The runner-side
// contract lives separately in common/clicontract; this package owns the dispatcher release
// contract at the dispatcher module root.
package dispatchercontract

import (
	"embed"
	"encoding/json"
	"fmt"
)

const (
	dispatcherContractFileName = "dispatcher-contract.json"
	schemaVersion              = 1
)

//go:embed dispatcher-contract.json
var contractFiles embed.FS

var DispatcherCurrent = mustLoadDispatcherContract()

type DispatcherContract struct {
	SchemaVersion int `json:"schemaVersion"`
	// DispatcherVersion is the launcher release version. It moves only when the dispatcher
	// itself is released, not when project-local CLI releases move.
	DispatcherVersion string `json:"dispatcherVersion"`
	// DispatcherContractVersion is the launcher capability generation this binary provides.
	// It moves only when project pins need a newer dispatcher contract.
	DispatcherContractVersion int `json:"dispatcherContractVersion"`
}

func mustLoadDispatcherContract() DispatcherContract {
	content, err := contractFiles.ReadFile(dispatcherContractFileName)
	if err != nil {
		panic(fmt.Sprintf("Dispatcher contract is not embedded: %v", err))
	}

	var contract DispatcherContract
	if err := json.Unmarshal(content, &contract); err != nil {
		panic(fmt.Sprintf("Dispatcher contract is invalid JSON: %v", err))
	}
	if contract.SchemaVersion != schemaVersion {
		panic(fmt.Sprintf("Dispatcher contract schema version mismatch: %d", contract.SchemaVersion))
	}
	requireString(contract.DispatcherVersion, "dispatcherVersion")
	if contract.DispatcherContractVersion < 1 {
		panic(fmt.Sprintf("Dispatcher contract dispatcherContractVersion must be at least 1, got %d", contract.DispatcherContractVersion))
	}
	return contract
}

func requireString(value string, key string) {
	if value == "" {
		panic(fmt.Sprintf("contract field %s must not be empty", key))
	}
}
