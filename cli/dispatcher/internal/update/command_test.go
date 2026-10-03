package update

import (
	"reflect"
	"testing"
)

// Verifies each supported OS gets its own shell and installer script, pinned to the current
// version's release assets and the matching update channel.
func TestCommandForOSBuildsTheInstallerCommandPerOS(t *testing.T) {
	cases := []struct {
		goos       string
		wantName   string
		wantScript string
	}{
		{goos: "darwin", wantName: "sh", wantScript: PosixScriptName},
		{goos: "linux", wantName: "sh", wantScript: PosixScriptName},
		{goos: "windows", wantName: "powershell", wantScript: WindowsScriptName},
	}
	for _, testCase := range cases {
		t.Run(testCase.goos, func(t *testing.T) {
			command, err := CommandForOS(testCase.goos, Options{CurrentVersion: "3.1.0"})
			if err != nil {
				t.Fatalf("CommandForOS failed: %v", err)
			}

			assetURL := "https://github.com/hatayama/unity-cli-loop/releases/download/dispatcher-v3.1.0/" + testCase.wantScript
			want := Command{
				Name:                 testCase.wantName,
				Env:                  []string{"ULOOP_VERSION=" + LatestStable},
				InstallerName:        testCase.wantScript,
				InstallerURL:         assetURL,
				InstallerChecksumURL: assetURL + ".sha256",
				ReleaseTag:           "dispatcher-v3.1.0",
			}
			if !reflect.DeepEqual(command, want) {
				t.Fatalf("command = %#v, want %#v", command, want)
			}
		})
	}
}

// Verifies an OS without a native installer is rejected with the unsupported-OS message.
func TestCommandForOSRejectsUnsupportedOS(t *testing.T) {
	_, err := CommandForOS("plan9", Options{CurrentVersion: "3.1.0"})

	if err == nil || err.Error() != UnsupportedOSMessage {
		t.Fatalf("err = %v, want %q", err, UnsupportedOSMessage)
	}
}

// Verifies an explicit target version replaces the current version for both the installer assets
// and the update selector, which then names that exact dispatcher release.
func TestCommandForOSUsesTheTargetVersion(t *testing.T) {
	command, err := CommandForOS("linux", Options{CurrentVersion: "3.0.0-beta.2", TargetVersion: "v3.2.0"})
	if err != nil {
		t.Fatalf("CommandForOS failed: %v", err)
	}

	if command.ReleaseTag != "dispatcher-v3.2.0" {
		t.Fatalf("ReleaseTag = %q, want dispatcher-v3.2.0", command.ReleaseTag)
	}
	if !reflect.DeepEqual(command.Env, []string{"ULOOP_VERSION=dispatcher-v3.2.0"}) {
		t.Fatalf("Env = %#v", command.Env)
	}
}

// Verifies a beta current version selects the beta channel when no target version is given.
func TestSelectorUsesTheBetaChannelForBetaVersions(t *testing.T) {
	if selector := Selector(Options{CurrentVersion: "3.0.0-beta.2"}); selector != LatestBeta {
		t.Fatalf("Selector = %q, want %q", selector, LatestBeta)
	}
}

// Verifies target version normalization strips each accepted release-tag prefix, case-insensitively,
// and leaves surrounding whitespace out.
func TestNormalizeTargetVersionStripsReleasePrefixes(t *testing.T) {
	cases := map[string]string{
		" 3.2.0 ":                     "3.2.0",
		"v3.2.0":                      "3.2.0",
		"V3.2.0":                      "3.2.0",
		"dispatcher-v3.2.0":           "3.2.0",
		"Dispatcher-V3.2.0":           "3.2.0",
		"uloop-project-runner-v3.2.0": "3.2.0",
		"ULOOP-PROJECT-RUNNER-V3.2.0": "3.2.0",
	}
	for input, want := range cases {
		if got := NormalizeTargetVersion(input); got != want {
			t.Fatalf("NormalizeTargetVersion(%q) = %q, want %q", input, got, want)
		}
	}
}

// Verifies the target version check accepts semantic versions and rejects anything else.
func TestIsValidTargetVersion(t *testing.T) {
	if !IsValidTargetVersion("3.2.0-beta.1") {
		t.Fatal("a semantic version must be valid")
	}
	if IsValidTargetVersion("latest") {
		t.Fatal("a channel name must not be a valid target version")
	}
}
