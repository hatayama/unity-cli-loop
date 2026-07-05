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
)

//go:embed dispatcher-contract.json
var contractFiles embed.FS

var DispatcherCurrent = mustLoadDispatcherContract()

type DispatcherContract struct {
	// DispatcherVersion is the launcher release version. It moves only when the dispatcher
	// itself is released, not when project-local CLI releases move.
	DispatcherVersion string `json:"dispatcherVersion"`
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
	requireString(contract.DispatcherVersion, "dispatcherVersion")
	return contract
}

func requireString(value string, key string) {
	if value == "" {
		panic(fmt.Sprintf("contract field %s must not be empty", key))
	}
}
