package update

import "testing"

func TestScriptURLForBetaVersionUsesReleaseInstaller(t *testing.T) {
	// Verifies beta dispatcher installs use the installer script shipped with the selected release.
	url := ScriptURL("3.0.0-beta.3", PosixScriptName)

	expected := "https://raw.githubusercontent.com/hatayama/unity-cli-loop/dispatcher-v3.0.0-beta.3/scripts/install.sh"
	if url != expected {
		t.Fatalf("script URL mismatch: got %q want %q", url, expected)
	}
}

func TestScriptURLForStableVersionUsesReleaseInstaller(t *testing.T) {
	// Verifies stable dispatcher installs use the installer script shipped with the selected release.
	url := ScriptURL("3.0.0", WindowsScriptName)

	expected := "https://raw.githubusercontent.com/hatayama/unity-cli-loop/dispatcher-v3.0.0/scripts/install.ps1"
	if url != expected {
		t.Fatalf("script URL mismatch: got %q want %q", url, expected)
	}
}

func TestProjectRunnerReleaseTagAddsMissingPrefix(t *testing.T) {
	// Verifies project runner downloads use the GitHub release tag format.
	tag := ProjectRunnerReleaseTag("3.0.0-beta.3")

	if tag != "uloop-project-runner-v3.0.0-beta.3" {
		t.Fatalf("release tag mismatch: %s", tag)
	}
}

func TestProjectRunnerReleaseTagKeepsProjectRunnerPrefix(t *testing.T) {
	// Verifies exact project runner release tags are not rewritten.
	tag := ProjectRunnerReleaseTag("uloop-project-runner-v3.0.0-beta.3")

	if tag != "uloop-project-runner-v3.0.0-beta.3" {
		t.Fatalf("release tag mismatch: %s", tag)
	}
}

func TestDispatcherReleaseTagAddsMissingPrefix(t *testing.T) {
	// Verifies dispatcher installs use the GitHub release tag format.
	tag := DispatcherReleaseTag("1.0.0")

	if tag != "dispatcher-v1.0.0" {
		t.Fatalf("release tag mismatch: %s", tag)
	}
}

func TestDispatcherReleaseTagKeepsPrefix(t *testing.T) {
	// Verifies exact dispatcher release tags are not rewritten.
	tag := DispatcherReleaseTag("dispatcher-v1.0.0")

	if tag != "dispatcher-v1.0.0" {
		t.Fatalf("release tag mismatch: %s", tag)
	}
}

func TestUpdateSelectorForBetaVersionUsesLatestBeta(t *testing.T) {
	// Verifies CLI self-update advances within the beta release channel.
	selector := UpdateSelectorForVersion("3.0.0-beta.3")

	if selector != LatestBeta {
		t.Fatalf("selector mismatch: %s", selector)
	}
}

func TestUpdateSelectorForStableVersionUsesLatestStable(t *testing.T) {
	// Verifies stable CLI self-update advances within the stable release channel.
	selector := UpdateSelectorForVersion("3.0.0")

	if selector != LatestStable {
		t.Fatalf("selector mismatch: %s", selector)
	}
}
