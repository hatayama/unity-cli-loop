package project

import (
	"encoding/json"
	"os"
	"path/filepath"
	"runtime"
	"testing"
)

const endpointContractPath = "tests/contracts/endpoint_contract.json"

type endpointContractFile struct {
	Comment string                 `json:"comment"`
	Cases   []endpointContractCase `json:"cases"`
}

type endpointContractCase struct {
	ID                      string   `json:"id"`
	CanonicalProjectRoot    string   `json:"canonicalProjectRoot"`
	TrimOnlyEquivalentRoots []string `json:"trimOnlyEquivalentRoots"`
	UnixSocketPath          string   `json:"unixSocketPath"`
	WindowsPipePath         string   `json:"windowsPipePath"`
}

// Verifies Go CreateEndpoint matches the shared endpoint contract for already-canonicalized roots.
func TestCreateEndpointMatchesSharedContract(t *testing.T) {
	contract := readEndpointContract(t)
	for _, contractCase := range contract.Cases {
		contractCase := contractCase
		t.Run(contractCase.ID, func(t *testing.T) {
			assertEndpointMatchesContract(t, contractCase.CanonicalProjectRoot, contractCase)
			for _, equivalentRoot := range contractCase.TrimOnlyEquivalentRoots {
				trimmed := trimTrailingSeparators(equivalentRoot)
				if trimmed != contractCase.CanonicalProjectRoot {
					t.Fatalf(
						"trimOnlyEquivalentRoot %q should trim to canonical %q, got %q",
						equivalentRoot,
						contractCase.CanonicalProjectRoot,
						trimmed)
				}
				assertEndpointMatchesContract(t, trimmed, contractCase)
			}
		})
	}
}

func assertEndpointMatchesContract(t *testing.T, projectRoot string, contractCase endpointContractCase) {
	t.Helper()

	endpoint := CreateEndpoint(projectRoot)
	expected := contractCase.UnixSocketPath
	if runtime.GOOS == "windows" {
		expected = contractCase.WindowsPipePath
	}
	if endpoint.Address != expected {
		t.Fatalf("endpoint mismatch for %q\nexpected: %s\nactual:   %s", projectRoot, expected, endpoint.Address)
	}
}

func readEndpointContract(t *testing.T) endpointContractFile {
	t.Helper()

	path := findRepoRelativeFile(t, endpointContractPath)
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read shared endpoint contract: %v", err)
	}

	var contract endpointContractFile
	if err := json.Unmarshal(data, &contract); err != nil {
		t.Fatalf("failed to unmarshal shared endpoint contract: %v", err)
	}
	if len(contract.Cases) == 0 {
		t.Fatal("endpoint contract must contain at least one case")
	}
	return contract
}

func findRepoRelativeFile(t *testing.T, relativePath string) string {
	t.Helper()

	directory, err := os.Getwd()
	if err != nil {
		t.Fatalf("failed to resolve current directory: %v", err)
	}

	for {
		candidate := filepath.Join(directory, relativePath)
		if _, err := os.Stat(candidate); err == nil {
			return candidate
		}

		parent := filepath.Dir(directory)
		if parent == directory {
			t.Fatalf("failed to find %s from %s", relativePath, directory)
		}
		directory = parent
	}
}
