package projectrunner

import (
	"encoding/json"
	"os"
	"path/filepath"
	"reflect"
	"testing"
)

const getLogsResponseContractPath = "tests/contracts/get_logs_response_contract.json"

// Verifies the Go pause-point get-logs DTO preserves every field in the shared Unity response contract.
func TestPausePointGetLogsResponseMatchesSharedContract(t *testing.T) {
	fixture := readGetLogsResponseContract(t)

	var response pausePointGetLogsResponse
	if err := json.Unmarshal(fixture, &response); err != nil {
		t.Fatalf("failed to unmarshal shared get-logs response contract: %v", err)
	}

	roundTripped, err := json.Marshal(response)
	if err != nil {
		t.Fatalf("failed to marshal Go get-logs response: %v", err)
	}

	expected := readJSONFieldShape(t, fixture)
	actual := readJSONFieldShape(t, roundTripped)
	if !reflect.DeepEqual(actual, expected) {
		t.Fatalf("get-logs response field shape drifted\nexpected: %#v\nactual:   %#v", expected, actual)
	}
}

func readGetLogsResponseContract(t *testing.T) []byte {
	t.Helper()

	path := findRepoRelativeFile(t, getLogsResponseContractPath)
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read shared get-logs response contract: %v", err)
	}
	return data
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

func readJSONFieldShape(t *testing.T, data []byte) map[string]any {
	t.Helper()

	var value any
	if err := json.Unmarshal(data, &value); err != nil {
		t.Fatalf("failed to parse JSON field shape: %v", err)
	}
	return normalizeJSONFieldShape(value).(map[string]any)
}

func normalizeJSONFieldShape(value any) any {
	switch typedValue := value.(type) {
	case map[string]any:
		shape := make(map[string]any, len(typedValue))
		for key, child := range typedValue {
			shape[key] = normalizeJSONFieldShape(child)
		}
		return shape
	case []any:
		if len(typedValue) == 0 {
			return []any{}
		}
		return []any{normalizeJSONFieldShape(typedValue[0])}
	case nil:
		return "null"
	case bool:
		return "boolean"
	case float64:
		return "number"
	case string:
		return "string"
	default:
		return "unknown"
	}
}
