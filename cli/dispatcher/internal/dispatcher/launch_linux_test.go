//go:build linux

package dispatcher

import (
	"reflect"
	"testing"
)

// Verifies launch on Linux looks for the Editor under the Unity Hub default location in the home directory.
func TestUnityExecutableCandidatesOnLinuxUsesHubPathUnderHome(t *testing.T) {
	t.Setenv("HOME", "/home/uloop-test")

	candidates := unityExecutableCandidates("6000.0.1f1")

	expected := []string{"/home/uloop-test/Unity/Hub/Editor/6000.0.1f1/Editor/Unity"}
	if !reflect.DeepEqual(candidates, expected) {
		t.Fatalf("expected %v, got %v", expected, candidates)
	}
}
