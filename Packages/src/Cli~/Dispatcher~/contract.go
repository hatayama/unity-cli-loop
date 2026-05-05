package dispatchercontract

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
	SchemaVersion        int    `json:"schemaVersion"`
	DispatcherVersion    string `json:"dispatcherVersion"`
	DispatcherVersionEnv string `json:"dispatcherVersionEnv"`
}

func mustLoad() Contract {
	content, err := contractFiles.ReadFile(contractFileName)
	if err != nil {
		panic(fmt.Sprintf("dispatcher contract is not embedded: %v", err))
	}

	var contract Contract
	if err := json.Unmarshal(content, &contract); err != nil {
		panic(fmt.Sprintf("dispatcher contract is invalid JSON: %v", err))
	}
	if contract.SchemaVersion != schemaVersion {
		panic(fmt.Sprintf("dispatcher contract schema version mismatch: %d", contract.SchemaVersion))
	}
	requireString(contract.DispatcherVersion, "dispatcherVersion")
	requireString(contract.DispatcherVersionEnv, "dispatcherVersionEnv")
	return contract
}

func requireString(value string, key string) {
	if value == "" {
		panic(fmt.Sprintf("dispatcher contract field %s must not be empty", key))
	}
}
