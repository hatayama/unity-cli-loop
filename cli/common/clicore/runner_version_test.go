package clicore

import (
	"bytes"
	"encoding/json"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
)

// Verifies that the version JSON reports the embedded project runner and protocol versions.
func TestWriteVersionJSONReportsContractVersions(t *testing.T) {
	var stdout bytes.Buffer

	WriteVersionJSON(&stdout)

	var payload struct {
		ProjectRunnerVersion string
		ProtocolVersion      int
	}
	if err := json.Unmarshal(stdout.Bytes(), &payload); err != nil {
		t.Fatalf("version output is not JSON: %v (%q)", err, stdout.String())
	}
	if payload.ProjectRunnerVersion != clicontract.ProjectRunnerVersion() || payload.ProtocolVersion != clicontract.ProtocolVersion() {
		t.Fatalf("unexpected version payload: %#v", payload)
	}
}
