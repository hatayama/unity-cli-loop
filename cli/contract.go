package clicontract

import (
	"embed"
	"encoding/json"
	"fmt"
)

const (
	contractFileName = "contract.json"
	schemaVersion    = 1
)

//go:embed contract.json
var contractFiles embed.FS

var Current = mustLoad()

type Contract struct {
	SchemaVersion int `json:"schemaVersion"`
	// ProtocolVersion is the C# IPC contract generation this binary speaks. It moves only
	// when the Unity package and the CLI can no longer interoperate, never per release.
	ProtocolVersion int    `json:"protocolVersion"`
	CliVersion      string `json:"cliVersion"`
}

func mustLoad() Contract {
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

func requireString(value string, key string) {
	if value == "" {
		panic(fmt.Sprintf("CLI contract field %s must not be empty", key))
	}
}
