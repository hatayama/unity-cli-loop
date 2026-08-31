package clicontract

import (
	"embed"
	"encoding/json"
	"fmt"
	"sync"
)

const (
	contractFileName = "contract.json"
)

//go:embed contract.json
var contractFiles embed.FS

var (
	contractLoadOnce sync.Once
	loadedContract   Contract
	loadContractErr  error
)

type Contract struct {
	// ProtocolVersion is the C# IPC contract generation this binary speaks. It moves only
	// when the Unity package and the CLI can no longer interoperate, never per release.
	ProtocolVersion      int    `json:"protocolVersion"`
	ProjectRunnerVersion string `json:"projectRunnerVersion"`
}

// Load reads and validates the embedded CLI contract.
func Load() (Contract, error) {
	contractLoadOnce.Do(func() {
		loadedContract, loadContractErr = loadEmbeddedContract()
	})
	return loadedContract, loadContractErr
}

func loadEmbeddedContract() (Contract, error) {
	content, err := contractFiles.ReadFile(contractFileName)
	if err != nil {
		return Contract{}, fmt.Errorf("CLI contract is not embedded: %w", err)
	}

	return parseContract(content)
}

// ProjectRunnerVersion returns the project runner release version from the CLI contract.
func ProjectRunnerVersion() string {
	return mustLoadContract().ProjectRunnerVersion
}

// ProtocolVersion returns the IPC protocol generation from the CLI contract.
func ProtocolVersion() int {
	return mustLoadContract().ProtocolVersion
}

func parseContract(content []byte) (Contract, error) {
	var contract Contract
	if err := json.Unmarshal(content, &contract); err != nil {
		return Contract{}, fmt.Errorf("CLI contract is invalid JSON: %w", err)
	}
	if err := requireString(contract.ProjectRunnerVersion, "projectRunnerVersion"); err != nil {
		return Contract{}, err
	}
	if contract.ProtocolVersion < 1 {
		return Contract{}, fmt.Errorf("CLI contract protocolVersion must be at least 1, got %d", contract.ProtocolVersion)
	}
	return contract, nil
}

func mustLoadContract() Contract {
	contract, err := Load()
	if err != nil {
		panic(err)
	}
	return contract
}

func requireString(value string, key string) error {
	if value == "" {
		return fmt.Errorf("contract field %s must not be empty", key)
	}
	return nil
}
