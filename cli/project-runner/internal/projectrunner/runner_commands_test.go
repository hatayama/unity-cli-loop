package projectrunner

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const testUnityRPCFailureResponse = `{"jsonrpc":"2.0","id":1,"error":{"code":-32603,"message":"tool exploded in Unity"}}`

// serveRawIPCResponse answers one request with a complete JSON-RPC response so tests can
// return error envelopes as well as results. The received request is forwarded on requests.
func serveRawIPCResponse(
	listener net.Listener,
	expectedMethod string,
	requests chan<- map[string]any,
	serverErr chan<- error,
	response string,
) {
	conn, err := listener.Accept()
	if err != nil {
		serverErr <- err
		return
	}
	defer func() { _ = conn.Close() }()

	payload, err := unityipc.Read(bufio.NewReader(conn))
	if err != nil {
		serverErr <- err
		return
	}
	request := struct {
		Method string         `json:"method"`
		Params map[string]any `json:"params"`
	}{}
	if err := json.Unmarshal(payload, &request); err != nil {
		serverErr <- err
		return
	}
	if request.Method != expectedMethod {
		serverErr <- fmt.Errorf("method mismatch: %s", request.Method)
		return
	}
	requests <- request.Params
	if err := unityipc.Write(conn, []byte(response)); err != nil {
		serverErr <- err
	}
}

// fakeUnityServer is one loopback listener that answers a single request for a test.
type fakeUnityServer struct {
	connection unityipc.Connection
	requests   chan map[string]any
	serverErr  chan error
}

func startFakeUnityServer(t *testing.T, projectRoot string, expectedMethod string, response string) fakeUnityServer {
	t.Helper()
	listener := newLoopbackIpcListener(t)
	server := fakeUnityServer{
		connection: unityipc.Connection{
			Endpoint: unityipc.Endpoint{
				Network: listener.Addr().Network(),
				Address: listener.Addr().String(),
			},
			ProjectRoot: projectRoot,
		},
		requests:  make(chan map[string]any, 1),
		serverErr: make(chan error, 1),
	}
	go serveRawIPCResponse(listener, expectedMethod, server.requests, server.serverErr, response)
	return server
}

func startFakeUnityResultServer(t *testing.T, projectRoot string, expectedMethod string, result string) fakeUnityServer {
	t.Helper()
	return startFakeUnityServer(t, projectRoot, expectedMethod, fmt.Sprintf(`{"jsonrpc":"2.0","result":%s,"id":1}`, result))
}

func (server fakeUnityServer) receivedRequest(t *testing.T) map[string]any {
	t.Helper()
	select {
	case request := <-server.requests:
		return request
	case err := <-server.serverErr:
		t.Fatalf("fake Unity server failed: %v", err)
		return nil
	case <-time.After(5 * time.Second):
		t.Fatal("timed out waiting for the fake Unity server to receive a request")
		return nil
	}
}

// unreachableConnection points at a project whose IPC endpoint is never contacted; tests use
// it for paths that must fail before any request is sent.
func unreachableConnection(projectRoot string) unityipc.Connection {
	return unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: "tcp", Address: "127.0.0.1:1"},
		ProjectRoot: projectRoot,
	}
}

func writeFakeUnityProject(t *testing.T) string {
	t.Helper()
	root := t.TempDir()
	for _, name := range []string{"Assets", "ProjectSettings"} {
		if err := os.Mkdir(filepath.Join(root, name), 0o755); err != nil {
			t.Fatalf("failed to create %s: %v", name, err)
		}
	}
	canonical, err := filepath.EvalSymlinks(root)
	if err != nil {
		t.Fatalf("failed to canonicalize project root: %v", err)
	}
	return canonical
}

// Verifies that native commands with their own parsers reject an unknown flag themselves,
// naming the command, instead of falling through to the dynamic tool catalog.
func TestRunResolvedProjectCommandRoutesNativeCommandsToTheirOwnParsers(t *testing.T) {
	for _, command := range []string{
		clicore.PausePointAwaitCommandName,
		clicore.PausePointStatusUserCommandName,
		pausePointEnableCommandName,
	} {
		t.Run(command, func(t *testing.T) {
			var stdout, stderr bytes.Buffer

			code := runResolvedProjectCommand(
				context.Background(),
				unreachableConnection(t.TempDir()),
				command,
				[]string{"--bogus-flag"},
				t.TempDir(),
				&stdout,
				&stderr,
			)

			if code != 1 {
				t.Fatalf("exit code = %d, want 1; stderr=%s", code, stderr.String())
			}
			if stdout.Len() != 0 {
				t.Fatalf("stdout must stay empty on argument errors: %s", stdout.String())
			}
			if !strings.Contains(stderr.String(), "--bogus-flag") {
				t.Fatalf("stderr must name the rejected flag:\n%s", stderr.String())
			}
			if strings.Contains(stderr.String(), "Unknown command") {
				t.Fatalf("native command must not be looked up in the tool catalog:\n%s", stderr.String())
			}
		})
	}
}

// Verifies that sync fetches the live tool catalog and stores it verbatim as the project tool cache.
func TestRunResolvedProjectCommandSyncWritesToolCache(t *testing.T) {
	projectRoot := t.TempDir()
	catalog := `{"tools":[{"name":"get-logs"}]}`
	server := startFakeUnityResultServer(t, projectRoot, "get-tool-details", catalog)
	var stdout, stderr bytes.Buffer

	code := runResolvedProjectCommand(context.Background(), server.connection, "sync", nil, projectRoot, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("sync failed with %d: %s", code, stderr.String())
	}
	server.receivedRequest(t)
	cachePath := filepath.Join(projectRoot, clicore.CacheDirectoryName, clicore.CacheFileName)
	content, err := os.ReadFile(cachePath)
	if err != nil {
		t.Fatalf("tool cache was not written: %v", err)
	}
	if string(content) != catalog {
		t.Fatalf("cache content mismatch:\nwant: %s\ngot:  %s", catalog, content)
	}
	if stdout.String() != "Tools synced to "+cachePath+"\n" {
		t.Fatalf("stdout mismatch: %q", stdout.String())
	}
}

// Verifies that a Unity-side failure during sync is reported and leaves no cache file behind.
func TestRunSyncReportsUnityFailureWithoutWritingCache(t *testing.T) {
	projectRoot := t.TempDir()
	server := startFakeUnityServer(t, projectRoot, "get-tool-details", testUnityRPCFailureResponse)
	var stdout, stderr bytes.Buffer

	code := runSync(context.Background(), server.connection, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("exit code = %d, want 1", code)
	}
	if !strings.Contains(stderr.String(), "tool exploded in Unity") {
		t.Fatalf("stderr must carry the Unity error message:\n%s", stderr.String())
	}
	if _, err := os.Stat(filepath.Join(projectRoot, clicore.CacheDirectoryName)); !os.IsNotExist(err) {
		t.Fatalf("cache directory must not be created on failure: %v", err)
	}
	if stdout.Len() != 0 {
		t.Fatalf("stdout must stay empty: %s", stdout.String())
	}
}

// Verifies that sync fails without claiming success when the cache directory or file cannot be written.
func TestRunSyncFailsWhenCacheCannotBeWritten(t *testing.T) {
	cases := []struct {
		name    string
		prepare func(t *testing.T, projectRoot string)
	}{
		{
			name: "cache directory path is a file",
			prepare: func(t *testing.T, projectRoot string) {
				writeTestFile(t, filepath.Join(projectRoot, clicore.CacheDirectoryName), "not a directory")
			},
		},
		{
			name: "cache file path is a directory",
			prepare: func(t *testing.T, projectRoot string) {
				if err := os.MkdirAll(filepath.Join(projectRoot, clicore.CacheDirectoryName, clicore.CacheFileName), 0o755); err != nil {
					t.Fatalf("failed to create blocking directory: %v", err)
				}
			},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			projectRoot := t.TempDir()
			testCase.prepare(t, projectRoot)
			server := startFakeUnityResultServer(t, projectRoot, "get-tool-details", `{"tools":[]}`)
			var stdout, stderr bytes.Buffer

			code := runSync(context.Background(), server.connection, &stdout, &stderr)

			if code != 1 {
				t.Fatalf("exit code = %d, want 1", code)
			}
			if stdout.Len() != 0 {
				t.Fatalf("stdout must not report a sync: %s", stdout.String())
			}
			if stderr.Len() == 0 {
				t.Fatal("stderr must explain the write failure")
			}
		})
	}
}

// Verifies that a catalog tool is sent to Unity with its CLI flags converted to schema params
// and that Unity's result is written to stdout.
func TestRunResolvedProjectCommandSendsDynamicToolToUnity(t *testing.T) {
	projectRoot := t.TempDir()
	server := startFakeUnityResultServer(t, projectRoot, "get-logs", `{"Success":true,"TotalCount":0}`)
	var stdout, stderr bytes.Buffer

	code := runResolvedProjectCommand(
		context.Background(),
		server.connection,
		"get-logs",
		[]string{"--max-count", "5", "--include-stack-trace"},
		projectRoot,
		&stdout,
		&stderr,
	)

	if code != 0 {
		t.Fatalf("get-logs failed with %d: %s", code, stderr.String())
	}
	request := server.receivedRequest(t)
	if request["MaxCount"] != float64(5) || request["IncludeStackTrace"] != true {
		t.Fatalf("unexpected params sent to Unity: %#v", request)
	}
	if !strings.Contains(stdout.String(), `"TotalCount": 0`) {
		t.Fatalf("stdout must carry Unity's result:\n%s", stdout.String())
	}
}

// Verifies that run-tests with --skip-compile goes straight to Unity without the implicit compile.
func TestRunResolvedProjectCommandRunTestsSkipCompileSendsRunTestsOnly(t *testing.T) {
	original := runTestsImplicitCompile
	runTestsImplicitCompile = func(context.Context, unityipc.Connection, io.Writer) compileExecutionResult {
		t.Fatal("implicit compile must not run with --skip-compile")
		return compileExecutionResult{}
	}
	t.Cleanup(func() { runTestsImplicitCompile = original })

	projectRoot := t.TempDir()
	server := startFakeUnityResultServer(t, projectRoot, clicore.RunTestsCommandName, `{"Success":true,"TestCount":2}`)
	var stdout, stderr bytes.Buffer

	code := runResolvedProjectCommand(
		context.Background(),
		server.connection,
		clicore.RunTestsCommandName,
		[]string{"--skip-compile", "--filter-value", "Sample"},
		projectRoot,
		&stdout,
		&stderr,
	)

	if code != 0 {
		t.Fatalf("run-tests failed with %d: %s", code, stderr.String())
	}
	if request := server.receivedRequest(t); request["FilterValue"] != "Sample" {
		t.Fatalf("unexpected params sent to Unity: %#v", request)
	}
	if strings.Contains(stdout.String(), runTestsCompileNote) {
		t.Fatalf("stdout must not carry the compile note when compile was skipped:\n%s", stdout.String())
	}
}

// Verifies that a command missing from the tool catalog is reported as unknown without contacting Unity.
func TestRunDynamicProjectToolRejectsUnknownCommand(t *testing.T) {
	var stdout, stderr bytes.Buffer

	code := runDynamicProjectTool(
		context.Background(),
		unreachableConnection(t.TempDir()),
		"no-such-tool",
		nil,
		t.TempDir(),
		&stdout,
		&stderr,
	)

	if code != 1 {
		t.Fatalf("exit code = %d, want 1", code)
	}
	if !strings.Contains(stderr.String(), "no-such-tool") {
		t.Fatalf("stderr must name the unknown command:\n%s", stderr.String())
	}
}

// Verifies that invalid tool arguments are rejected before any request reaches Unity.
func TestRunDynamicProjectToolRejectsInvalidArgumentsBeforeSending(t *testing.T) {
	missingCodeFile := filepath.Join(t.TempDir(), "missing.cs")
	existingCodeFile := filepath.Join(t.TempDir(), "snippet.cs")
	writeTestFile(t, existingCodeFile, "return 1;")
	notAProject := t.TempDir()

	cases := []struct {
		name       string
		command    string
		args       []string
		wantStderr string
	}{
		{name: "code-file without value", command: clicore.ExecuteDynamicCodeCommandName, args: []string{"--code-file"}, wantStderr: "--code-file"},
		{name: "code-file that does not exist", command: clicore.ExecuteDynamicCodeCommandName, args: []string{"--code-file", missingCodeFile}, wantStderr: "missing.cs"},
		{name: "code and code-file together", command: clicore.ExecuteDynamicCodeCommandName, args: []string{"--code", "return 2;", "--code-file", existingCodeFile}, wantStderr: "cannot be combined"},
		{name: "clear-pause-point file without line", command: pausePointClearCommandName, args: []string{"--file", "Assets/Sample.cs"}, wantStderr: "--line"},
		{name: "unknown tool option", command: "get-logs", args: []string{"--bogus-flag"}, wantStderr: "--bogus-flag"},
		{name: "nested project path that is not a Unity project", command: "get-logs", args: []string{"--project-path", notAProject}, wantStderr: "Unity project"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			var stdout, stderr bytes.Buffer

			code := runDynamicProjectTool(
				context.Background(),
				unreachableConnection(t.TempDir()),
				testCase.command,
				testCase.args,
				t.TempDir(),
				&stdout,
				&stderr,
			)

			if code != 1 {
				t.Fatalf("exit code = %d, want 1; stderr=%s", code, stderr.String())
			}
			if !strings.Contains(stderr.String(), testCase.wantStderr) {
				t.Fatalf("stderr must mention %q:\n%s", testCase.wantStderr, stderr.String())
			}
			if stdout.Len() != 0 {
				t.Fatalf("stdout must stay empty: %s", stdout.String())
			}
		})
	}
}

// Verifies that a nested --project-path naming a different Unity project than the resolved
// connection is rejected instead of silently sending the request to the wrong Editor.
func TestRunDynamicProjectToolRejectsNestedProjectPathForAnotherProject(t *testing.T) {
	otherProject := writeFakeUnityProject(t)
	var stdout, stderr bytes.Buffer

	code := runDynamicProjectTool(
		context.Background(),
		unreachableConnection(writeFakeUnityProject(t)),
		"get-logs",
		[]string{"--project-path", otherProject},
		t.TempDir(),
		&stdout,
		&stderr,
	)

	if code != 1 {
		t.Fatalf("exit code = %d, want 1", code)
	}
	if !strings.Contains(stderr.String(), "--project-path must target the same Unity project") {
		t.Fatalf("stderr must explain the project mismatch:\n%s", stderr.String())
	}
}

// Verifies that a nested --project-path naming the connected project is accepted and stripped
// from the params sent to Unity.
func TestRunDynamicProjectToolAcceptsNestedProjectPathForSameProject(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, "get-logs", `{"Success":true}`)
	var stdout, stderr bytes.Buffer

	code := runDynamicProjectTool(
		context.Background(),
		server.connection,
		"get-logs",
		[]string{"--project-path", projectRoot, "--max-count", "1"},
		t.TempDir(),
		&stdout,
		&stderr,
	)

	if code != 0 {
		t.Fatalf("get-logs failed with %d: %s", code, stderr.String())
	}
	request := server.receivedRequest(t)
	if _, exists := request["ProjectPath"]; exists {
		t.Fatalf("--project-path must not be forwarded as a tool param: %#v", request)
	}
	if request["MaxCount"] != float64(1) {
		t.Fatalf("unexpected params sent to Unity: %#v", request)
	}
}

// Verifies that when run-tests fails in Unity after an implicit compile, the failure is
// reported on stderr and no compile note is written to stdout.
func TestRunDynamicProjectToolWithCompileNoteSkipsNoteWhenUnityFails(t *testing.T) {
	projectRoot := t.TempDir()
	server := startFakeUnityServer(t, projectRoot, clicore.RunTestsCommandName, testUnityRPCFailureResponse)
	var stdout, stderr bytes.Buffer

	code := runDynamicProjectToolWithCompileNote(
		context.Background(),
		server.connection,
		clicore.RunTestsCommandName,
		nil,
		projectRoot,
		&stdout,
		&stderr,
		true,
		"",
	)

	if code != 1 {
		t.Fatalf("exit code = %d, want 1", code)
	}
	server.receivedRequest(t)
	if stdout.Len() != 0 {
		t.Fatalf("stdout must stay empty when Unity fails: %s", stdout.String())
	}
	if !strings.Contains(stderr.String(), "tool exploded in Unity") {
		t.Fatalf("stderr must carry the Unity error message:\n%s", stderr.String())
	}
}

func writeTestFile(t *testing.T, path string, content string) {
	t.Helper()
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write %s: %v", path, err)
	}
}
