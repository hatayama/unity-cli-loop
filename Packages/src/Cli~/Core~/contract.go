package corecontract

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
	SchemaVersion                    int    `json:"schemaVersion"`
	CoreVersion                      string `json:"coreVersion"`
	MinimumRequiredDispatcherVersion string `json:"minimumRequiredDispatcherVersion"`
	DispatcherVersionEnv             string `json:"dispatcherVersionEnv"`
	RequiredDispatcherVersionFlag    string `json:"requiredDispatcherVersionFlag"`
}

func mustLoad() Contract {
	content, err := contractFiles.ReadFile(contractFileName)
	if err != nil {
		panic(fmt.Sprintf("core contract is not embedded: %v", err))
	}

	var contract Contract
	if err := json.Unmarshal(content, &contract); err != nil {
		panic(fmt.Sprintf("core contract is invalid JSON: %v", err))
	}
	if contract.SchemaVersion != schemaVersion {
		panic(fmt.Sprintf("core contract schema version mismatch: %d", contract.SchemaVersion))
	}
	requireString(contract.CoreVersion, "coreVersion")
	requireString(contract.MinimumRequiredDispatcherVersion, "minimumRequiredDispatcherVersion")
	requireString(contract.DispatcherVersionEnv, "dispatcherVersionEnv")
	requireString(contract.RequiredDispatcherVersionFlag, "requiredDispatcherVersionFlag")
	return contract
}

func requireString(value string, key string) {
	if value == "" {
		panic(fmt.Sprintf("core contract field %s must not be empty", key))
	}
}
