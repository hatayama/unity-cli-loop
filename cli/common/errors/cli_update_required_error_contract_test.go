package clierrors

import (
	"encoding/json"
	"os"
	"path/filepath"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const cliUpdateRequiredErrorContractPath = "tests/contracts/cli_update_required_error_contract.json"

type cliUpdateRequiredErrorContractFile struct {
	// This frame must remain backward compatible forever — old CLIs parse it to learn they must update.
	Comment   string          `json:"comment"`
	ErrorData json.RawMessage `json:"errorData"`
}

// Verifies the frozen cli_update_required error.data frame still classifies as CLI_UPDATE_REQUIRED.
// This frame must remain backward compatible forever — old CLIs parse it to learn they must update.
func TestClassifyError_WhenCliUpdateRequiredContractFixture_ClassifiesAsCLIUpdateRequired(t *testing.T) {
	contract := readCliUpdateRequiredErrorContract(t)

	err := &unityipc.RPCError{
		Code:    -32603,
		Message: "The installed uloop CLI uses an IPC protocol that does not match this Unity package.",
		Data:    contract.ErrorData,
	}

	cliErr := ClassifyError(err, ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "compile"})
	if cliErr.ErrorCode != ErrorCodeCLIUpdateRequired {
		t.Fatalf("expected %s, got %#v", ErrorCodeCLIUpdateRequired, cliErr)
	}
}

func readCliUpdateRequiredErrorContract(t *testing.T) cliUpdateRequiredErrorContractFile {
	t.Helper()

	path := findRepoRelativeFile(t, cliUpdateRequiredErrorContractPath)
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read cli_update_required error contract: %v", err)
	}

	var contract cliUpdateRequiredErrorContractFile
	if err := json.Unmarshal(data, &contract); err != nil {
		t.Fatalf("failed to unmarshal cli_update_required error contract: %v", err)
	}
	if len(contract.ErrorData) == 0 {
		t.Fatal("cli_update_required error contract must include errorData")
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
