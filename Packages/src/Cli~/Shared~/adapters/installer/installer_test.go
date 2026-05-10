package installer

import "testing"

func TestScriptURLForBetaVersionUsesV3BetaInstaller(t *testing.T) {
	// Verifies beta dispatcher installs use the beta branch installer script.
	url := ScriptURL("3.0.0-beta.3", PosixScriptName)

	expected := "https://raw.githubusercontent.com/hatayama/unity-cli-loop/v3-beta/scripts/install.sh"
	if url != expected {
		t.Fatalf("script URL mismatch: got %q want %q", url, expected)
	}
}

func TestScriptURLForStableVersionUsesMainInstaller(t *testing.T) {
	// Verifies stable dispatcher installs use the stable branch installer script.
	url := ScriptURL("3.0.0", WindowsScriptName)

	expected := "https://raw.githubusercontent.com/hatayama/unity-cli-loop/main/scripts/install.ps1"
	if url != expected {
		t.Fatalf("script URL mismatch: got %q want %q", url, expected)
	}
}

func TestReleaseTagAddsMissingPrefix(t *testing.T) {
	// Verifies installer commands pass the GitHub release tag format.
	tag := ReleaseTag("3.0.0-beta.3")

	if tag != "v3.0.0-beta.3" {
		t.Fatalf("release tag mismatch: %s", tag)
	}
}

func TestUpdateSelectorForBetaVersionUsesLatestBeta(t *testing.T) {
	// Verifies dispatcher self-update advances within the beta release channel.
	selector := UpdateSelectorForVersion("3.0.0-beta.3")

	if selector != LatestBeta {
		t.Fatalf("selector mismatch: %s", selector)
	}
}

func TestUpdateSelectorForStableVersionUsesLatestStable(t *testing.T) {
	// Verifies stable dispatcher self-update advances within the stable release channel.
	selector := UpdateSelectorForVersion("3.0.0")

	if selector != LatestStable {
		t.Fatalf("selector mismatch: %s", selector)
	}
}
