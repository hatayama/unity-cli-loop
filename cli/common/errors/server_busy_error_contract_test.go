package clierrors

import (
	"encoding/json"
	"os"
	"testing"
)

const serverBusyErrorContractPath = "tests/contracts/server_busy_error_contract.json"

type serverBusyErrorContractFile struct {
	Comment   string          `json:"comment"`
	ErrorData json.RawMessage `json:"errorData"`
}

// Verifies the shared server_busy error.data fixture decodes into every typed struct field.
func TestDecodeServerBusyErrorData_WhenContractFixture_ReadsEveryField(t *testing.T) {
	contract := readServerBusyErrorContract(t)

	decoded := decodeServerBusyErrorData(contract.ErrorData)
	if decoded.Type != "server_busy" {
		t.Fatalf("type mismatch: %#v", decoded)
	}
	if decoded.Message != "Unity is busy running 'compile'. Retry 'get-logs' after the running tool completes." {
		t.Fatalf("message mismatch: %#v", decoded)
	}
	if decoded.RunningToolName != "compile" {
		t.Fatalf("runningToolName mismatch: %#v", decoded)
	}
	if decoded.RequestedToolName != "get-logs" {
		t.Fatalf("requestedToolName mismatch: %#v", decoded)
	}
	if decoded.IsPlaying == nil || !*decoded.IsPlaying {
		t.Fatalf("isPlaying mismatch: %#v", decoded)
	}
	if decoded.IsPaused == nil || *decoded.IsPaused {
		t.Fatalf("isPaused mismatch: %#v", decoded)
	}
	if decoded.IsCompiling == nil || !*decoded.IsCompiling {
		t.Fatalf("isCompiling mismatch: %#v", decoded)
	}
	if decoded.IsUpdating == nil || *decoded.IsUpdating {
		t.Fatalf("isUpdating mismatch: %#v", decoded)
	}
	if decoded.SecondsSinceLastMainThreadTick == nil || *decoded.SecondsSinceLastMainThreadTick != 1.5 {
		t.Fatalf("secondsSinceLastMainThreadTick mismatch: %#v", decoded)
	}
	if decoded.RunningToolElapsedSeconds == nil || *decoded.RunningToolElapsedSeconds != 12 {
		t.Fatalf("runningToolElapsedSeconds mismatch: %#v", decoded)
	}
}

func readServerBusyErrorContract(t *testing.T) serverBusyErrorContractFile {
	t.Helper()

	path := findRepoRelativeFile(t, serverBusyErrorContractPath)
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read server_busy error contract: %v", err)
	}

	var contract serverBusyErrorContractFile
	if err := json.Unmarshal(data, &contract); err != nil {
		t.Fatalf("failed to unmarshal server_busy error contract: %v", err)
	}
	if len(contract.ErrorData) == 0 {
		t.Fatal("server_busy error contract must include errorData")
	}
	return contract
}
