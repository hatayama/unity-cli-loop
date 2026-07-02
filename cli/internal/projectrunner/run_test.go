package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

func TestRunProjectLocalVersionJSONIncludesProtocolVersion(t *testing.T) {
	// Verifies Unity setup can inspect protocol compatibility without parsing human help text.
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"--version", "--json"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("version json command failed with code %d: %s", code, stderr.String())
	}
	var payload map[string]any
	if err := json.Unmarshal(stdout.Bytes(), &payload); err != nil {
		t.Fatalf("version json output is not JSON: %v\n%s", err, stdout.String())
	}
	if payload["ProjectRunnerVersion"] != clicore.Version {
		t.Fatalf("projectRunnerVersion mismatch: %#v", payload)
	}
	if payload["ProtocolVersion"] != float64(clicore.ProtocolVersion) {
		t.Fatalf("protocolVersion mismatch: %#v", payload)
	}
}

// Tests that unknown leading options are reported as global option errors.
func TestRunProjectLocalRejectsUnknownGlobalOption(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"--project-pathology"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("exit code mismatch: code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if !strings.Contains(stderr.String(), "Unknown global option: --project-pathology") {
		t.Fatalf("stderr missing unknown option error:\n%s", stderr.String())
	}
}
