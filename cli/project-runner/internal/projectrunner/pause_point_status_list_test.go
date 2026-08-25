package projectrunner

import (
	"bytes"
	"context"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies the dedicated list query sends no marker parameters and decodes the compact Unity response.
func TestQueryPausePointStatusListFromUnitySendsEmptyParametersAndDecodesSummary(t *testing.T) {
	listener := newLoopbackIpcListener(t)
	requests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(
		listener,
		pausePointStatusCommandName,
		requests,
		serverErr,
		`{"Success":true,"Message":"1 pause point(s) registered.","Count":1,"PausePoints":[{"Id":"jump","Status":"Enabled","Mode":"single-shot","HitCount":0,"RemainingMilliseconds":30000}],"NextActions":["Pass --id <marker-id> to inspect one pause point in detail."]}`,
	)

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}
	response, err := queryPausePointStatusListFromUnity(context.Background(), connection)
	if err != nil {
		t.Fatalf("queryPausePointStatusListFromUnity failed: %v", err)
	}
	if response.Count != 1 || len(response.PausePoints) != 1 || response.PausePoints[0].Id != "jump" {
		t.Fatalf("response mismatch: %#v", response)
	}

	params := readIPCRequest(t, requests)
	if len(params) != 0 {
		t.Fatalf("expected empty list parameters, got %#v", params)
	}
}

// Verifies list mode names the individual per-marker option that cannot apply without a target.
func TestParsePausePointStatusOptionsListModeRejectsPerMarkerOptions(t *testing.T) {
	tests := []struct {
		name      string
		args      []string
		wantError string
	}{
		{
			name:      "captured variables",
			args:      []string{"--captured-variables", "full"},
			wantError: "--captured-variables requires --id or --file with --line.",
		},
		{
			name:      "captured variable names",
			args:      []string{"--captured-variable-names", "speed"},
			wantError: "--captured-variable-names requires --id or --file with --line.",
		},
		{
			name:      "expect",
			args:      []string{"--expect", "speed==5"},
			wantError: "--expect requires --id or --file with --line.",
		},
		{
			name:      "captured variables before expect",
			args:      []string{"--captured-variables", "full", "--expect", "speed==5"},
			wantError: "--captured-variables requires --id or --file with --line.",
		},
		{
			name:      "expect before captured variables",
			args:      []string{"--expect", "speed==5", "--captured-variables", "full"},
			wantError: "--expect requires --id or --file with --line.",
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			_, err := parsePausePointStatusOptions(test.args)
			if err == nil || err.Error() != test.wantError {
				t.Fatalf("error = %v, want %q", err, test.wantError)
			}
		})
	}
}

// Verifies an id-less status command uses the dedicated list query and writes its summary JSON unchanged.
func TestRunPausePointStatusCommandWithoutTargetWritesListResponse(t *testing.T) {
	originalQueryPausePointStatus := queryPausePointStatus
	originalQueryPausePointStatusList := queryPausePointStatusList
	t.Cleanup(func() {
		queryPausePointStatus = originalQueryPausePointStatus
		queryPausePointStatusList = originalQueryPausePointStatusList
	})
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		t.Fatal("single-marker status query must not run in list mode")
		return pausePointStatusResponse{}, nil
	}
	queryPausePointStatusList = func(
		ctx context.Context,
		connection unityipc.Connection,
	) (pausePointStatusListResponse, error) {
		return pausePointStatusListResponse{
			Success: true,
			Message: "2 pause point(s) registered.",
			Count:   2,
			PausePoints: []pausePointStatusListItemResponse{
				{
					Id:                    "alpha",
					Status:                pausePointStatusEnabled,
					Mode:                  "single-shot",
					HitCount:              0,
					RemainingMilliseconds: 30000,
				},
				{
					Id:                    "zulu",
					Status:                pausePointStatusExpired,
					Mode:                  "trace",
					HitCount:              4,
					RemainingMilliseconds: 0,
				},
			},
			NextActions: []string{"Pass --id <marker-id> to inspect one pause point in detail."},
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "<PROJECT_ROOT>"},
		[]string{},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	const want = "{\n" +
		"  \"Success\": true,\n" +
		"  \"Message\": \"2 pause point(s) registered.\",\n" +
		"  \"Count\": 2,\n" +
		"  \"PausePoints\": [\n" +
		"    {\n" +
		"      \"Id\": \"alpha\",\n" +
		"      \"Status\": \"Enabled\",\n" +
		"      \"Mode\": \"single-shot\",\n" +
		"      \"HitCount\": 0,\n" +
		"      \"RemainingMilliseconds\": 30000\n" +
		"    },\n" +
		"    {\n" +
		"      \"Id\": \"zulu\",\n" +
		"      \"Status\": \"Expired\",\n" +
		"      \"Mode\": \"trace\",\n" +
		"      \"HitCount\": 4,\n" +
		"      \"RemainingMilliseconds\": 0\n" +
		"    }\n" +
		"  ],\n" +
		"  \"NextActions\": [\n" +
		"    \"Pass --id \\u003cmarker-id\\u003e to inspect one pause point in detail.\"\n" +
		"  ]\n" +
		"}\n"
	if stdout.String() != want {
		t.Fatalf("stdout = %s, want %s", stdout.String(), want)
	}
}
