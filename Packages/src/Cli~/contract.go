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
	SchemaVersion int    `json:"schemaVersion"`
	CliVersion    string `json:"cliVersion"`
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
	return contract
}

func requireString(value string, key string) {
	if value == "" {
		panic(fmt.Sprintf("CLI contract field %s must not be empty", key))
	}
}
