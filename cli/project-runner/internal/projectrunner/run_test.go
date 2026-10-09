package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/clitest"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
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

// Verifies that a malformed global --project-path is rejected before any command runs.
func TestRunProjectLocalRejectsProjectPathWithoutValue(t *testing.T) {
	var stdout, stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"--project-path"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("exit code = %d, want 1; stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stderr.String(), "--project-path requires a value") {
		t.Fatalf("stderr must report the missing value:\n%s", stderr.String())
	}
}

// Verifies that compile routed through runTool takes the domain-reload wait path, which
// validates --timeout-seconds before contacting Unity.
func TestRunToolRoutesCompileToDomainReloadWait(t *testing.T) {
	var stdout, stderr bytes.Buffer

	code := runTool(
		context.Background(),
		unreachableConnection(t.TempDir()),
		clicore.CompileCommandName,
		map[string]any{compileWaitTimeoutParam: -1},
		&stdout,
		&stderr,
	)

	if code != 1 {
		t.Fatalf("exit code = %d, want 1", code)
	}
	if !strings.Contains(stderr.String(), "Invalid positive integer value for --timeout-seconds") {
		t.Fatalf("stderr must reject the timeout:\n%s", stderr.String())
	}
}

// Verifies that control-play-mode routed through runTool waits for the requested state
// by polling Unity after the action request.
func TestRunToolRoutesControlPlayModeToStateWait(t *testing.T) {
	listener := newLoopbackIpcListener(t)
	requests := make(chan map[string]any, 2)
	serverErr := make(chan error, 1)
	go serveControlPlayModeResponses(listener, requests, serverErr, []string{
		`{"IsPlaying":false,"IsPaused":false,"Message":"Play mode started"}`,
		`{"IsPlaying":true,"IsPaused":false,"Message":"Play mode status"}`,
	})
	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: listener.Addr().Network(), Address: listener.Addr().String()},
		ProjectRoot: t.TempDir(),
	}
	var stdout, stderr bytes.Buffer

	code := runTool(
		context.Background(),
		connection,
		controlPlayModeCommandName,
		map[string]any{controlPlayModeActionParam: "Play", controlPlayModeTimeoutParam: 1},
		&stdout,
		&stderr,
	)

	if code != 0 {
		t.Fatalf("control-play-mode failed with %d: %s", code, stderr.String())
	}
	readControlPlayModeRequest(t, requests)
	readControlPlayModeRequest(t, requests)
	if !strings.Contains(stdout.String(), `"IsPlaying": true`) {
		t.Fatalf("stdout must report the reached play state:\n%s", stdout.String())
	}
}

// Verifies that PlayMode run-tests honoring Enter Play Mode settings goes through the
// domain-reload wait path and still returns Unity's result.
func TestRunToolExecutionRoutesPlayModeRunTestsToDomainReloadWait(t *testing.T) {
	projectRoot := t.TempDir()
	server := startFakeUnityResultServer(t, projectRoot, clicore.RunTestsCommandName, `{"Success":true,"TestCount":1}`)

	result := runToolExecution(
		context.Background(),
		server.connection,
		clicore.RunTestsCommandName,
		map[string]any{"RespectEnterPlayModeSettings": true, "TestMode": "PlayMode"},
		io.Discard,
	)

	if result.exitCode != 0 {
		t.Fatalf("exit code = %d, want 0", result.exitCode)
	}
	request := server.receivedRequest(t)
	if request["RequestId"] == nil || request["RequestId"] == "" {
		t.Fatalf("domain-reload wait path must attach a request ID: %#v", request)
	}
	if !strings.Contains(string(result.result), `"TestCount":1`) {
		t.Fatalf("result must carry Unity's payload: %s", result.result)
	}
}

// Verifies that a Unity-side error on the plain tool path exits 1, prints nothing to stdout,
// and reports the Unity message on stderr.
func TestRunToolReportsUnityErrorOnPlainPath(t *testing.T) {
	projectRoot := t.TempDir()
	server := startFakeUnityServer(t, projectRoot, "get-logs", testUnityRPCFailureResponse)
	var stdout, stderr bytes.Buffer

	code := runTool(context.Background(), server.connection, "get-logs", map[string]any{}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("exit code = %d, want 1", code)
	}
	server.receivedRequest(t)
	if stdout.Len() != 0 {
		t.Fatalf("stdout must stay empty: %s", stdout.String())
	}
	if !strings.Contains(stderr.String(), "tool exploded in Unity") {
		t.Fatalf("stderr must carry the Unity error message:\n%s", stderr.String())
	}
}

// Verifies that execute-dynamic-code with the domain-reload wait strips the internal wait
// control field from the result it prints when Unity reports no reload is pending.
func TestRunToolExecuteDynamicCodeStripsWaitControlField(t *testing.T) {
	projectRoot := t.TempDir()
	server := startFakeUnityResultServer(
		t, projectRoot, clicore.ExecuteDynamicCodeCommandName,
		`{"Success":true,"Result":"42","DomainReloadWaitRequired":false}`)
	var stdout, stderr bytes.Buffer

	code := runTool(
		context.Background(),
		server.connection,
		clicore.ExecuteDynamicCodeCommandName,
		map[string]any{"Code": "return 42;", clicore.DomainReloadWaitParam: true},
		&stdout,
		&stderr,
	)

	if code != 0 {
		t.Fatalf("execute-dynamic-code failed with %d: %s", code, stderr.String())
	}
	server.receivedRequest(t)
	var payload map[string]any
	if err := json.Unmarshal(stdout.Bytes(), &payload); err != nil {
		t.Fatalf("stdout is not JSON: %v\n%s", err, stdout.String())
	}
	if _, exists := payload["DomainReloadWaitRequired"]; exists {
		t.Fatalf("internal wait field must be stripped: %#v", payload)
	}
	if payload["Result"] != "42" {
		t.Fatalf("Unity's result must be kept: %#v", payload)
	}
}

// Verifies that a Unity-side error on the execute-dynamic-code wait path exits 1, reports the
// Unity message, and prints no result instead of continuing with an empty response.
func TestRunExecuteDynamicCodeWithDomainReloadWaitReportsUnityError(t *testing.T) {
	projectRoot := t.TempDir()
	server := startFakeUnityServer(t, projectRoot, clicore.ExecuteDynamicCodeCommandName, testUnityRPCFailureResponse)
	var stdout, stderr bytes.Buffer

	code := runExecuteDynamicCodeWithDomainReloadWait(
		context.Background(),
		server.connection,
		map[string]any{"Code": "return 1;", clicore.DomainReloadWaitParam: true},
		&stdout,
		&stderr,
	)

	if code != 1 {
		t.Fatalf("exit code = %d, want 1", code)
	}
	server.receivedRequest(t)
	if stdout.Len() != 0 {
		t.Fatalf("stdout must stay empty: %s", stdout.String())
	}
	if !strings.Contains(stderr.String(), "tool exploded in Unity") {
		t.Fatalf("stderr must carry the Unity error message:\n%s", stderr.String())
	}
}

// Verifies that the fresh compile path rejects an invalid --timeout-seconds before sending anything.
func TestRunFreshCompileRejectsInvalidTimeout(t *testing.T) {
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		t.Fatal("compile status must not be queried")
		return compileStatusResponse{}, nil
	})
	deps.sendCompile = func(context.Context, unityipc.Connection, string, map[string]any, unityipc.ProgressFunc, time.Duration) (unityipc.UnitySendOutcome, error) {
		t.Fatal("compile must not be sent")
		return unityipc.UnitySendOutcome{}, nil
	}
	var stderr bytes.Buffer

	result := runFreshCompileWithDomainReloadWaitResultWithDeps(
		context.Background(),
		unreachableConnection(t.TempDir()),
		map[string]any{compileWaitTimeoutParam: "soon"},
		&stderr,
		deps,
	)

	if result.exitCode != 1 || len(result.result) != 0 {
		t.Fatalf("unexpected result: %#v", result)
	}
	if !strings.Contains(stderr.String(), "Invalid positive integer value for --timeout-seconds") {
		t.Fatalf("stderr must reject the timeout:\n%s", stderr.String())
	}
}

// Verifies that when the compile connection drops after dispatch, the fresh compile path
// keeps waiting on compile status, and a cancellation during that wait is reported as a failure.
func TestRunFreshCompileReportsCancellationWhileWaitingAfterDisconnect(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	queried := 0
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		queried++
		cancel()
		return compileStatusResponse{IsCompiling: true}, nil
	})
	deps.sendCompile = func(context.Context, unityipc.Connection, string, map[string]any, unityipc.ProgressFunc, time.Duration) (unityipc.UnitySendOutcome, error) {
		return unityipc.UnitySendOutcome{RequestDispatched: true}, io.EOF
	}
	var stderr bytes.Buffer

	result := runFreshCompileWithDomainReloadWaitResultWithDeps(
		ctx,
		unreachableConnection(t.TempDir()),
		map[string]any{},
		&stderr,
		deps,
	)

	if result.exitCode != 1 || len(result.result) != 0 {
		t.Fatalf("unexpected result: %#v", result)
	}
	if queried != 1 {
		t.Fatalf("compile status queries = %d, want 1", queried)
	}
	if !strings.Contains(stderr.String(), context.Canceled.Error()) {
		t.Fatalf("stderr must report the cancellation:\n%s", stderr.String())
	}
}

// Verifies that list reports a Unity-side failure instead of printing an empty catalog.
func TestRunListReportsUnityFailure(t *testing.T) {
	projectRoot := t.TempDir()
	server := startFakeUnityServer(t, projectRoot, "get-tool-details", testUnityRPCFailureResponse)
	var stdout, stderr bytes.Buffer

	code := runList(context.Background(), server.connection, nil, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("exit code = %d, want 1", code)
	}
	if stdout.Len() != 0 {
		t.Fatalf("stdout must stay empty: %s", stdout.String())
	}
	if !strings.Contains(stderr.String(), "tool exploded in Unity") {
		t.Fatalf("stderr must carry the Unity error message:\n%s", stderr.String())
	}
}

// Verifies that list --names fails when Unity returns a catalog that cannot be decoded.
func TestRunListNamesRejectsMalformedCatalog(t *testing.T) {
	projectRoot := t.TempDir()
	server := startFakeUnityResultServer(t, projectRoot, "get-tool-details", `{"tools":"not-a-list"}`)
	var stdout, stderr bytes.Buffer

	code := runList(context.Background(), server.connection, []string{"--names"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("exit code = %d, want 1", code)
	}
	if stdout.Len() != 0 {
		t.Fatalf("stdout must stay empty: %s", stdout.String())
	}
	if !strings.Contains(stderr.String(), "cannot unmarshal") {
		t.Fatalf("stderr must explain the decode failure:\n%s", stderr.String())
	}
}

// Verifies that plain list prints the live Unity catalog as JSON.
func TestRunListPrintsLiveCatalog(t *testing.T) {
	projectRoot := t.TempDir()
	server := startFakeUnityResultServer(t, projectRoot, "get-tool-details", `{"tools":[{"name":"sample-live-tool","description":"Live tool"}]}`)
	var stdout, stderr bytes.Buffer

	code := runList(context.Background(), server.connection, nil, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("list failed with %d: %s", code, stderr.String())
	}
	if !json.Valid(stdout.Bytes()) {
		t.Fatalf("stdout must be JSON:\n%s", stdout.String())
	}
	if !strings.Contains(stdout.String(), "sample-live-tool") {
		t.Fatalf("stdout must list the live tool:\n%s", stdout.String())
	}
}
