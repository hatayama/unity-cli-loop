package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// pausePointStatusExpectPayload decodes only the expectation fields the CLI adds on top of the
// Unity status response, so these tests assert the wire names --expect callers actually read.
type pausePointStatusExpectPayload struct {
	Status                string                        `json:"Status"`
	CapturedVariables     []pausePointCapturedVariable  `json:"CapturedVariables"`
	Expectations          []pausePointExpectationResult `json:"Expectations"`
	AllExpectationsPassed *bool                         `json:"AllExpectationsPassed"`
}

func stubPausePointStatusHitWithSpeed(t *testing.T, speed string) {
	t.Helper()

	originalQuery := queryPausePointStatus
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
	})

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Success:   true,
			Id:        id,
			Status:    pausePointStatusHit,
			IsEnabled: true,
			IsHit:     true,
			HitCount:  1,
			CapturedVariables: []pausePointCapturedVariable{
				{Name: "speed", Scope: "Local", TypeName: "System.Int32", Value: pausePointVariableValue(speed)},
			},
		}, nil
	}
}

func runPausePointStatusForExpect(t *testing.T, args []string) (int, string) {
	t.Helper()

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		args,
		&stdout,
		&stderr)
	if stderr.Len() > 0 {
		t.Logf("stderr: %s", stderr.String())
	}
	return code, stdout.String()
}

func decodePausePointStatusExpectPayload(t *testing.T, output string) pausePointStatusExpectPayload {
	t.Helper()

	var payload pausePointStatusExpectPayload
	if err := json.Unmarshal([]byte(output), &payload); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, output)
	}
	return payload
}

// Verifies pause-point-status --expect reports each expectation and the aggregate verdict using the
// same field names await-pause-point already emits, so one query shape works for both commands.
func TestRunPausePointStatusEvaluatesExpectations(t *testing.T) {
	stubPausePointStatusHitWithSpeed(t, "5")

	code, output := runPausePointStatusForExpect(t, []string{"--id", "jump", "--expect", "speed=5"})

	if code != 0 {
		t.Fatalf("expected success, got %d: %s", code, output)
	}
	payload := decodePausePointStatusExpectPayload(t, output)
	if len(payload.Expectations) != 1 {
		t.Fatalf("expected 1 expectation, got %#v", payload.Expectations)
	}
	expectation := payload.Expectations[0]
	if expectation.Name != "speed" || expectation.Expected != "5" || expectation.Actual != "5" ||
		!expectation.Found || !expectation.Passed {
		t.Fatalf("expectation mismatch: %#v", expectation)
	}
	if payload.AllExpectationsPassed == nil || !*payload.AllExpectationsPassed {
		t.Fatalf("AllExpectationsPassed mismatch: %#v", payload.AllExpectationsPassed)
	}
}

// Verifies a failed expectation still exits 0: querying the hit succeeded, and the expectation
// verdict is reported in the payload rather than through the process exit code.
func TestRunPausePointStatusFailedExpectationKeepsExitCodeZero(t *testing.T) {
	stubPausePointStatusHitWithSpeed(t, "5")

	code, output := runPausePointStatusForExpect(t, []string{"--id", "jump", "--expect", "speed=9"})

	if code != 0 {
		t.Fatalf("expected exit code 0 for a failed expectation, got %d: %s", code, output)
	}
	payload := decodePausePointStatusExpectPayload(t, output)
	if len(payload.Expectations) != 1 || payload.Expectations[0].Passed {
		t.Fatalf("expected a failing expectation, got %#v", payload.Expectations)
	}
	if payload.AllExpectationsPassed == nil || *payload.AllExpectationsPassed {
		t.Fatalf("AllExpectationsPassed must be present and false: %#v", payload.AllExpectationsPassed)
	}
}

// Verifies expectations are evaluated before --captured-variables names strips values, so the
// requested value is still compared even though the response itself reports names only.
func TestRunPausePointStatusEvaluatesExpectationsBeforeNamesModeStripsValues(t *testing.T) {
	stubPausePointStatusHitWithSpeed(t, "5")

	code, output := runPausePointStatusForExpect(
		t, []string{"--id", "jump", "--captured-variables", "names", "--expect", "speed=5"})

	if code != 0 {
		t.Fatalf("expected success, got %d: %s", code, output)
	}
	payload := decodePausePointStatusExpectPayload(t, output)
	if len(payload.Expectations) != 1 || !payload.Expectations[0].Passed ||
		payload.Expectations[0].Actual != "5" {
		t.Fatalf("expectation must be evaluated against the unfiltered value: %#v", payload.Expectations)
	}
	if len(payload.CapturedVariables) != 1 || payload.CapturedVariables[0].Value != nil {
		t.Fatalf("names mode must still strip Value: %#v", payload.CapturedVariables)
	}
}

// Verifies expectations are evaluated before the --captured-variable-names filter narrows the
// response, so an --expect target that was not also requested by name is not reported as missing.
func TestRunPausePointStatusEvaluatesExpectationsBeforeNameFilter(t *testing.T) {
	stubPausePointStatusHitWithSpeed(t, "5")

	code, output := runPausePointStatusForExpect(
		t, []string{"--id", "jump", "--captured-variable-names", "health", "--expect", "speed=5"})

	if code != 0 {
		t.Fatalf("expected success, got %d: %s", code, output)
	}
	payload := decodePausePointStatusExpectPayload(t, output)
	if len(payload.Expectations) != 1 || !payload.Expectations[0].Found ||
		!payload.Expectations[0].Passed {
		t.Fatalf("expectation must survive the name filter: %#v", payload.Expectations)
	}
}

// Verifies a status query without --expect emits neither expectation field, so callers that never
// asked for expectations see no schema change.
func TestRunPausePointStatusOmitsExpectationFieldsWithoutExpectFlag(t *testing.T) {
	stubPausePointStatusHitWithSpeed(t, "5")

	code, output := runPausePointStatusForExpect(t, []string{"--id", "jump"})

	if code != 0 {
		t.Fatalf("expected success, got %d: %s", code, output)
	}
	if strings.Contains(output, "Expectations") || strings.Contains(output, "AllExpectationsPassed") {
		t.Fatalf("expectation fields must be omitted without --expect: %s", output)
	}
}

// Verifies a marker that is armed but not yet hit reports its expectations as not found rather than
// omitting them, and that Status stays the field distinguishing "not hit yet" from "hit and wrong".
func TestRunPausePointStatusReportsExpectationsAsNotFoundBeforeAHit(t *testing.T) {
	originalQuery := queryPausePointStatus
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
	})
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Success: true, Id: id, Status: pausePointStatusEnabled, IsEnabled: true}, nil
	}

	code, output := runPausePointStatusForExpect(t, []string{"--id", "jump", "--expect", "speed=5"})

	if code != 0 {
		t.Fatalf("expected success, got %d: %s", code, output)
	}
	payload := decodePausePointStatusExpectPayload(t, output)
	if payload.Status != pausePointStatusEnabled {
		t.Fatalf("Status must still report the marker is only armed: %q", payload.Status)
	}
	if len(payload.Expectations) != 1 || payload.Expectations[0].Found || payload.Expectations[0].Passed {
		t.Fatalf("an unhit marker captured nothing, so the expectation is not found: %#v", payload.Expectations)
	}
}

// Verifies an invalid --expect value is rejected by pause-point-status the same way
// await-pause-point rejects it, instead of being reported as an unknown option.
func TestRunPausePointStatusRejectsInvalidExpectValue(t *testing.T) {
	stubPausePointStatusHitWithSpeed(t, "5")

	code, output := runPausePointStatusForExpect(t, []string{"--id", "jump", "--expect", "speed"})

	if code == 0 {
		t.Fatalf("expected failure for an --expect value without '=': %s", output)
	}
}

// Verifies pause-point-status --help advertises --expect, so the flag is discoverable from the
// command that accepts it.
func TestPausePointStatusHelpAdvertisesExpect(t *testing.T) {
	var stdout bytes.Buffer
	printNativeCommandHelp(clicore.PausePointStatusUserCommandName, &stdout)

	if !strings.Contains(stdout.String(), "--expect") {
		t.Fatalf("pause-point-status help must list --expect: %s", stdout.String())
	}
}
