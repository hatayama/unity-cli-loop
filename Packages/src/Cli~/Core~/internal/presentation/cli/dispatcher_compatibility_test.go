package cli

import (
	"bytes"
	"context"
	"encoding/json"
	"strings"
	"testing"

	corecontract "github.com/hatayama/unity-cli-loop/Packages/src/Cli/Core"
)

func TestRunProjectLocalRequiredDispatcherVersionPrintsMinimum(t *testing.T) {
	// Verifies that Unity can query the bundled core's dispatcher requirement without a dispatcher env.
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(
		context.Background(),
		[]string{corecontract.Current.RequiredDispatcherVersionFlag},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	if strings.TrimSpace(stdout.String()) != corecontract.Current.MinimumRequiredDispatcherVersion {
		t.Fatalf("required dispatcher version mismatch: %s", stdout.String())
	}
}

func TestRunProjectLocalWhenDispatcherVersionMissingReportsUpdateRequired(t *testing.T) {
	// Verifies that an old dispatcher which does not pass its version is rejected before tool dispatch.
	t.Setenv(corecontract.Current.DispatcherVersionEnv, "")
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"list"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	var envelope cliErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != errorCodeDispatcherUpdateRequired {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Details["requiredDispatcherVersion"] != corecontract.Current.MinimumRequiredDispatcherVersion {
		t.Fatalf("details mismatch: %#v", envelope.Error.Details)
	}
}

func TestDispatcherCompatibilityAcceptsRequiredVersion(t *testing.T) {
	// Verifies that core accepts a dispatcher at the exact minimum compatibility version.
	if !isDispatcherCompatible(corecontract.Current.MinimumRequiredDispatcherVersion) {
		t.Fatal("required dispatcher version should be compatible")
	}
}

func TestDispatcherCompatibilityRejectsOlderVersion(t *testing.T) {
	// Verifies that core rejects a dispatcher below the minimum compatibility version.
	if isDispatcherCompatible("3.0.0-beta.0") {
		t.Fatal("older dispatcher version should not be compatible")
	}
}
