package projectrunner

import (
	"bytes"
	"context"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/clitest"
)

func TestRunProjectLocalVersionJSONIncludesProtocolVersion(t *testing.T) {
	// Verifies Unity setup can inspect protocol compatibility without parsing human help text.
	payload := clitest.RunVersionJSON(t, RunProjectLocal)

	if payload["ProjectRunnerVersion"] != clicontract.ProjectRunnerVersion() {
		t.Fatalf("projectRunnerVersion mismatch: %#v", payload)
	}
	if payload["ProtocolVersion"] != float64(clicontract.ProtocolVersion()) {
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
