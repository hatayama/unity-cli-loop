package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"path/filepath"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/clitest"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies the CLI-only flag is removed before generic schema parsing while unrelated flags remain.
func TestExtractRunTestsSkipCompileFlagRemovesOnlyItsFlag(t *testing.T) {
	remaining, skipCompile := extractRunTestsSkipCompileFlag([]string{"--skip-compile", "--test-mode", "EditMode"})
	if !skipCompile {
		t.Fatal("expected --skip-compile to be extracted")
	}
	if len(remaining) != 2 || remaining[0] != "--test-mode" || remaining[1] != "EditMode" {
		t.Fatalf("remaining arguments mismatch: %#v", remaining)
	}
}

// Verifies run-tests compiles first, resolves the live catalog after that compile, and adds its
// native-only CompileNote to the one final test response.
func TestRunTestsWithImplicitCompileRunsCompileThenUsesLiveCatalog(t *testing.T) {
	projectRoot := t.TempDir()
	listener := newLoopbackIpcListener(t)
	requests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(listener, clicore.RunTestsCommandName, requests, serverErr, `{"Success":true,"Status":"Passed","Message":"Test execution completed with status: Passed"}`)

	original := runTestsImplicitCompile
	compileCalls := 0
	runTestsImplicitCompile = func(context.Context, unityipc.Connection, io.Writer) compileExecutionResult {
		compileCalls++
		writeRunTestsCompileToolCache(t, projectRoot)
		return compileExecutionResult{result: json.RawMessage(`{"Success":true}`), exitCode: 0}
	}
	t.Cleanup(func() {
		runTestsImplicitCompile = original
	})

	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: listener.Addr().Network(), Address: listener.Addr().String()},
		ProjectRoot: projectRoot,
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runTestsWithImplicitCompile(context.Background(), connection, nil, projectRoot, &stdout, &stderr)
	if code != 0 {
		t.Fatalf("run-tests failed: code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if compileCalls != 1 {
		t.Fatalf("compile call count mismatch: %d", compileCalls)
	}
	readIPCRequest(t, requests)
	assertSingleRunTestsJSON(t, stdout.Bytes(), true)
	assertServerDidNotFail(t, serverErr)
}

// Verifies CompileNote remains present when the compile succeeds but the test response reports failures.
func TestRunTestsWithImplicitCompileAddsCompileNoteToFailedTestResponse(t *testing.T) {
	projectRoot := t.TempDir()
	writeRunTestsCompileToolCache(t, projectRoot)
	listener := newLoopbackIpcListener(t)
	requests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(listener, clicore.RunTestsCommandName, requests, serverErr, `{"Success":false,"Status":"Failed","Message":"Test execution completed with status: Failed"}`)

	original := runTestsImplicitCompile
	runTestsImplicitCompile = func(context.Context, unityipc.Connection, io.Writer) compileExecutionResult {
		return compileExecutionResult{result: json.RawMessage(`{"Success":true}`), exitCode: 0}
	}
	t.Cleanup(func() {
		runTestsImplicitCompile = original
	})

	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: listener.Addr().Network(), Address: listener.Addr().String()},
		ProjectRoot: projectRoot,
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runTestsWithImplicitCompile(context.Background(), connection, nil, projectRoot, &stdout, &stderr)
	if code != 1 {
		t.Fatalf("failed test response exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	readIPCRequest(t, requests)
	assertSingleRunTestsJSON(t, stdout.Bytes(), true)
	assertServerDidNotFail(t, serverErr)
}

// Verifies --skip-compile bypasses the compile seam and omits CompileNote from the test response.
func TestRunTestsWithImplicitCompileSkipCompileOmitsCompileNote(t *testing.T) {
	projectRoot := t.TempDir()
	writeRunTestsCompileToolCache(t, projectRoot)
	listener := newLoopbackIpcListener(t)
	requests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(listener, clicore.RunTestsCommandName, requests, serverErr, `{"Success":true,"Status":"Passed","Message":"Test execution completed with status: Passed"}`)

	original := runTestsImplicitCompile
	compileCalls := 0
	runTestsImplicitCompile = func(context.Context, unityipc.Connection, io.Writer) compileExecutionResult {
		compileCalls++
		return compileExecutionResult{}
	}
	t.Cleanup(func() {
		runTestsImplicitCompile = original
	})

	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: listener.Addr().Network(), Address: listener.Addr().String()},
		ProjectRoot: projectRoot,
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runTestsWithImplicitCompile(context.Background(), connection, []string{"--skip-compile"}, projectRoot, &stdout, &stderr)
	if code != 0 {
		t.Fatalf("run-tests failed: code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if compileCalls != 0 {
		t.Fatalf("--skip-compile must not invoke compile: %d", compileCalls)
	}
	readIPCRequest(t, requests)
	assertSingleRunTestsJSON(t, stdout.Bytes(), false)
	assertServerDidNotFail(t, serverErr)
}

// Verifies a failed implicit compile is the only output and prevents the test IPC request.
func TestRunTestsWithImplicitCompileFailsFastWithCompileResponse(t *testing.T) {
	projectRoot := t.TempDir()
	compileResponse := json.RawMessage(`{"Success":false,"ErrorCount":1,"WarningCount":0}`)
	compileResult := compileExecutionResult{result: compileResponse, exitCode: 1}
	var compileStdout bytes.Buffer
	if code := writeCompileExecutionResult(&compileStdout, compileResult); code != 1 {
		t.Fatalf("compile presenter exit code mismatch: %d", code)
	}
	original := runTestsImplicitCompile
	runTestsImplicitCompile = func(context.Context, unityipc.Connection, io.Writer) compileExecutionResult {
		return compileResult
	}
	t.Cleanup(func() {
		runTestsImplicitCompile = original
	})

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	connection := unityipc.Connection{ProjectRoot: projectRoot}

	code := runTestsWithImplicitCompile(context.Background(), connection, nil, projectRoot, &stdout, &stderr)
	if code != 1 {
		t.Fatalf("compile failure exit code mismatch: %d", code)
	}
	if stdout.String() != compileStdout.String() {
		t.Fatalf("run-tests compile failure must match compile output:\ncompile: %s\nrun-tests: %s", compileStdout.String(), stdout.String())
	}
	if bytes.Contains(stdout.Bytes(), []byte("CompileNote")) {
		t.Fatalf("compile failure must not include CompileNote: %s", stdout.String())
	}
}

func writeRunTestsCompileToolCache(t *testing.T, projectRoot string) {
	t.Helper()
	clitest.WriteProjectFile(t, projectRoot, filepath.Join(clicore.CacheDirectoryName, clicore.CacheFileName), `{
  "tools": [
    {
      "name": "run-tests",
      "inputSchema": {
        "type": "object",
        "properties": {}
      }
    }
  ]
}`)
}

func assertSingleRunTestsJSON(t *testing.T, output []byte, expectCompileNote bool) {
	t.Helper()
	decoder := json.NewDecoder(bytes.NewReader(output))
	fields := map[string]json.RawMessage{}
	if err := decoder.Decode(&fields); err != nil {
		t.Fatalf("stdout must contain JSON: %v\n%s", err, output)
	}
	var extra any
	if err := decoder.Decode(&extra); err != io.EOF {
		t.Fatalf("stdout must contain one JSON response: %s", output)
	}

	compileNote, present := fields["CompileNote"]
	if present != expectCompileNote {
		t.Fatalf("CompileNote presence mismatch: expect=%t fields=%s", expectCompileNote, output)
	}
	if !expectCompileNote {
		return
	}

	var note string
	if err := json.Unmarshal(compileNote, &note); err != nil {
		t.Fatalf("CompileNote must be a string: %v", err)
	}
	if note != runTestsCompileNote {
		t.Fatalf("CompileNote mismatch: %q", note)
	}
}

func assertServerDidNotFail(t *testing.T, serverErr <-chan error) {
	t.Helper()
	select {
	case err := <-serverErr:
		t.Fatalf("IPC server failed: %v", err)
	default:
	}
}
