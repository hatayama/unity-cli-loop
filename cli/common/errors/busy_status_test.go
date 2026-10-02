package clierrors

import "testing"

// Verifies the busy message falls back to the server text when either tool name is unknown.
func TestUnityServerBusyMessageFallsBackWithoutToolNames(t *testing.T) {
	cases := []struct {
		name             string
		data             serverBusyErrorData
		requestedCommand string
	}{
		{name: "running tool unknown", data: serverBusyErrorData{RequestedToolName: "compile"}},
		{name: "requested tool unknown", data: serverBusyErrorData{RunningToolName: "run-tests"}},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			got := unityServerBusyMessage("server busy", testCase.data, testCase.requestedCommand)
			if got != "server busy" {
				t.Fatalf("expected fallback message, got %q", got)
			}
		})
	}
}
