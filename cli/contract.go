package clicontract

import (
	"embed"
	"encoding/json"
	"fmt"
)

const (
	contractFileName           = "contract.json"
	dispatcherContractFileName = "dispatcher-contract.json"
	schemaVersion              = 1
)

//go:embed contract.json dispatcher-contract.json
var contractFiles embed.FS

var (
	Current           = mustLoadContract()
	DispatcherCurrent = mustLoadDispatcherContract()
)

type Contract struct {
	SchemaVersion int `json:"schemaVersion"`
	// ProtocolVersion is the C# IPC contract generation this binary speaks. It moves only
	// when the Unity package and the CLI can no longer interoperate, never per release.
	ProtocolVersion int    `json:"protocolVersion"`
	CliVersion      string `json:"cliVersion"`
}

type DispatcherContract struct {
	SchemaVersion int `json:"schemaVersion"`
	// DispatcherVersion is the launcher release version. It moves only when the dispatcher
	// itself is released, not when project-local CLI releases move.
	DispatcherVersion string `json:"dispatcherVersion"`
	// DispatcherContractVersion is the launcher capability generation this binary provides.
	// It moves only when project pins need a newer dispatcher contract.
	DispatcherContractVersion int `json:"dispatcherContractVersion"`
}

func mustLoadContract() Contract {
	content, err := contractFiles.ReadFile(contractFileName)
	if err != nil {
		panic(fmt.Sprintf("CLI contract is not embedded: %v", err))
	}

	var contract Contract
	if err := json.Unmarshal(content, &contract); err != nil {
		panic(fmt.Sprintf("CLI contract is invalid JSON: %v", err))
	}
	if contract.SchemaVersion != schemaVersion {
		panic(fmt.Sprintf("CLI contract schema version mismatch: %d", contract.SchemaVersion))
	}
	requireString(contract.CliVersion, "cliVersion")
	if contract.ProtocolVersion < 1 {
		panic(fmt.Sprintf("CLI contract protocolVersion must be at least 1, got %d", contract.ProtocolVersion))
	}
	return contract
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
