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

	const expected = "launch\nlist\nsync\nfocus-window\nawait-pause-point\npause-point-status\nskills\npackage\ninstall\nupdate\nuninstall\nversion\nenable-watch\nclear-watch\nget-watch-values\n"
	if stdout.String() != expected {
		t.Fatalf("list --names output mismatch:\n got:\n%s\nwant:\n%s", stdout.String(), expected)
	}
	request := readIPCRequest(t, requests)
	if len(request) != 0 {
		t.Fatalf("get-tool-details parameters mismatch: %#v", request)
	}
	assertServerDidNotFail(t, serverErr)
}

// Verifies list rejects an option other than its names-only selector before it contacts Unity and
// gives callers the established unknown-option recovery action.
func TestRunListRejectsUnknownOption(t *testing.T) {
	connection := unityipc.Connection{ProjectRoot: "/project"}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runList(context.Background(), connection, []string{"--unexpected"}, &stdout, &stderr)
	if code != 1 {
		t.Fatalf("unexpected exit code: %d", code)
	}
	if stdout.Len() != 0 {
		t.Fatalf("unknown list option must not write stdout: %s", stdout.String())
	}

	const expected = "{\n" +
		"  \"Success\": false,\n" +
		"  \"Error\": {\n" +
		"    \"ErrorCode\": \"INVALID_ARGUMENT\",\n" +
		"    \"Phase\": \"argument_parsing\",\n" +
		"    \"Message\": \"Unknown option for list: --unexpected\",\n" +
		"    \"Retryable\": false,\n" +
		"    \"SafeToRetry\": false,\n" +
		"    \"ProjectRoot\": \"/project\",\n" +
		"    \"Command\": \"list\",\n" +
		"    \"NextActions\": [\n" +
		"      \"Run \x60uloop list --help\x60 to inspect supported options.\"\n" +
		"    ],\n" +
		"    \"Details\": {\n" +
		"      \"Option\": \"--unexpected\"\n" +
		"    }\n" +
		"  }\n" +
		"}\n"
	if stderr.String() != expected {
		t.Fatalf("unknown list option error mismatch:\n got:\n%s\nwant:\n%s", stderr.String(), expected)
	}
}
