package projectrunner

import (
	"bytes"
	"context"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies list --names uses the native command registry and the live Unity catalog, while
// excluding a Unity tool that has the same name as a native command.
func TestRunResolvedProjectCommandListNamesWritesNativeAndLiveToolNames(t *testing.T) {
	listener := newLoopbackIpcListener(t)
	requests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(
		listener,
		"get-tool-details",
		requests,
		serverErr,
		`{"tools":[{"name":"focus-window"},{"name":"enable-watch"},{"name":"clear-watch"},{"name":"get-watch-values"}]}`,
	)
	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runResolvedProjectCommand(
		context.Background(),
		connection,
		"list",
		[]string{"--names"},
		connection.ProjectRoot,
		&stdout,
		&stderr,
	)
	if code != 0 {
		t.Fatalf("list --names failed: code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}

	const expected = "launch\nlist\nsync\nfocus-window\nawait-pause-point\npause-point-status\nset-code-optimization\nskills\npackage\ninstall\nupdate\nuninstall\nversion\nenable-watch\nclear-watch\nget-watch-values\n"
	if stdout.String() != expected {
		t.Fatalf("list --names output mismatch:\n got:\n%s\nwant:\n%s", stdout.String(), expected)
	}
	request := readIPCRequest(t, requests)
	if len(request) != 0 {
		t.Fatalf("get-tool-details parameters mismatch: %#v", request)
	}
	assertServerDidNotFail(t, serverErr)
}

// Verifies list accepts repeated names selectors but rejects value assignment and positional
// arguments with the complete stable INVALID_ARGUMENT envelope.
func TestRunListOptionBoundaries(t *testing.T) {
	const namesOutput = "launch\nlist\nsync\nfocus-window\nawait-pause-point\npause-point-status\nset-code-optimization\nskills\npackage\ninstall\nupdate\nuninstall\nversion\n"
	const namesAssignmentError = "{\n" +
		"  \"Success\": false,\n" +
		"  \"Error\": {\n" +
		"    \"ErrorCode\": \"INVALID_ARGUMENT\",\n" +
		"    \"Phase\": \"argument_parsing\",\n" +
		"    \"Message\": \"Unknown option for list: --names=true\",\n" +
		"    \"Retryable\": false,\n" +
		"    \"SafeToRetry\": false,\n" +
		"    \"ProjectRoot\": \"/project\",\n" +
		"    \"Command\": \"list\",\n" +
		"    \"NextActions\": [\n" +
		"      \"Run \x60uloop list --help\x60 to inspect supported options.\"\n" +
		"    ],\n" +
		"    \"Details\": {\n" +
		"      \"Option\": \"--names=true\"\n" +
		"    }\n" +
		"  }\n" +
		"}\n"
	const positionalArgumentError = "{\n" +
		"  \"Success\": false,\n" +
		"  \"Error\": {\n" +
		"    \"ErrorCode\": \"INVALID_ARGUMENT\",\n" +
		"    \"Phase\": \"argument_parsing\",\n" +
		"    \"Message\": \"Unknown option for list: marker\",\n" +
		"    \"Retryable\": false,\n" +
		"    \"SafeToRetry\": false,\n" +
		"    \"ProjectRoot\": \"/project\",\n" +
		"    \"Command\": \"list\",\n" +
		"    \"NextActions\": [\n" +
		"      \"Run \x60uloop list --help\x60 to inspect supported options.\"\n" +
		"    ],\n" +
		"    \"Details\": {\n" +
		"      \"Option\": \"marker\"\n" +
		"    }\n" +
		"  }\n" +
		"}\n"
	tests := []struct {
		name           string
		args           []string
		expectedStdout string
		expectedStderr string
	}{
		{
			name:           "names assignment",
			args:           []string{"--names=true"},
			expectedStderr: namesAssignmentError,
		},
		{
			name:           "repeated names",
			args:           []string{"--names", "--names"},
			expectedStdout: namesOutput,
		},
		{
			name:           "positional argument",
			args:           []string{"marker"},
			expectedStderr: positionalArgumentError,
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			connection := unityipc.Connection{ProjectRoot: "/project"}
			var listenerErr chan error
			var requests chan map[string]any
			if test.expectedStdout != "" {
				listener := newLoopbackIpcListener(t)
				requests = make(chan map[string]any, 1)
				listenerErr = make(chan error, 1)
				go serveSingleIPCResponse(listener, "get-tool-details", requests, listenerErr, "{\"tools\":[]}")
				connection.Endpoint = unityipc.Endpoint{
					Network: listener.Addr().Network(),
					Address: listener.Addr().String(),
				}
			}
			var stdout bytes.Buffer
			var stderr bytes.Buffer

			code := runList(context.Background(), connection, test.args, &stdout, &stderr)
			if test.expectedStdout != "" {
				if code != 0 {
					t.Fatalf("list --names failed: code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
				}
				if stdout.String() != test.expectedStdout {
					t.Fatalf("list --names output mismatch:\n got:\n%s\nwant:\n%s", stdout.String(), test.expectedStdout)
				}
				readIPCRequest(t, requests)
				assertServerDidNotFail(t, listenerErr)
				return
			}
			if code != 1 {
				t.Fatalf("unexpected exit code: %d", code)
			}
			if stdout.Len() != 0 {
				t.Fatalf("unknown list option must not write stdout: %s", stdout.String())
			}
			if stderr.String() != test.expectedStderr {
				t.Fatalf("unknown list option error mismatch:\n got:\n%s\nwant:\n%s", stderr.String(), test.expectedStderr)
			}
		})
	}
}
