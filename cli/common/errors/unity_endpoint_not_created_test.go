package clierrors

import "testing"

// Verifies a missing private endpoint directory uses the existing not-running recovery guidance.
func TestUnityEndpointNotCreatedErrorClassifiesAsUnityNotReachable(t *testing.T) {
	err := UnityEndpointNotCreatedError{EndpointDirectory: "/tmp/uloop-501"}
	cliErr := ClassifyError(err, ErrorContext{Command: "status"})

	if cliErr.ErrorCode != ErrorCodeUnityNotReachable || cliErr.Phase != ErrorPhaseConnection {
		t.Fatalf("unexpected classification: %#v", cliErr)
	}
	if len(cliErr.NextActions) == 0 || cliErr.NextActions[0] != "If Unity is closed, run `uloop launch`." {
		t.Fatalf("missing launch guidance: %#v", cliErr.NextActions)
	}
}
