package cli

import (
	"strings"
	"testing"

	corecontract "github.com/hatayama/unity-cli-loop/Packages/src/Cli/Core"
	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/adapters/installer"
)

func TestUpdateCommandForDarwinUsesDirectInstaller(t *testing.T) {
	// Verifies core update downloads the shared installer before running it with the required dispatcher version.
	commandName, args, err := updateCommandForOS("darwin")
	if err != nil {
		t.Fatalf("updateCommandForOS failed: %v", err)
	}

	if commandName != "sh" {
		t.Fatalf("command mismatch: %s", commandName)
	}
	joinedArgs := strings.Join(args, " ")
	expectedScriptURL := installer.ScriptURL(corecontract.Current.MinimumRequiredDispatcherVersion, installer.PosixScriptName)
	expectedReleaseTag := installer.ReleaseTag(corecontract.Current.MinimumRequiredDispatcherVersion)
	if !strings.Contains(joinedArgs, expectedScriptURL) {
		t.Fatalf("installer URL missing: %s", joinedArgs)
	}
	if !strings.Contains(joinedArgs, "ULOOP_VERSION='"+expectedReleaseTag+"'") {
		t.Fatalf("installer version missing: %s", joinedArgs)
	}
	if !strings.Contains(joinedArgs, "curl -fSL") || !strings.Contains(joinedArgs, "-o \"$tmp\"") {
		t.Fatalf("update command should download before executing: %s", joinedArgs)
	}
	if strings.Contains(joinedArgs, "npm") {
		t.Fatalf("update command still references npm: %s", joinedArgs)
	}
}

func TestUpdateCommandForWindowsUsesPowerShellInstaller(t *testing.T) {
	// Verifies core update calls the same Windows installer script with the required dispatcher version.
	commandName, args, err := updateCommandForOS("windows")
	if err != nil {
		t.Fatalf("updateCommandForOS failed: %v", err)
	}

	if commandName != windowsPowerShellCommand {
		t.Fatalf("command mismatch: %s", commandName)
	}
	joinedArgs := strings.Join(args, " ")
	expectedScriptURL := installer.ScriptURL(corecontract.Current.MinimumRequiredDispatcherVersion, installer.WindowsScriptName)
	expectedReleaseTag := installer.ReleaseTag(corecontract.Current.MinimumRequiredDispatcherVersion)
	if !strings.Contains(joinedArgs, expectedScriptURL) {
		t.Fatalf("installer URL missing: %s", joinedArgs)
	}
	if !strings.Contains(joinedArgs, "$env:ULOOP_VERSION='"+expectedReleaseTag+"'") {
		t.Fatalf("installer version missing: %s", joinedArgs)
	}
	if strings.Contains(joinedArgs, "npm") {
		t.Fatalf("update command still references npm: %s", joinedArgs)
	}
}

func TestUpdateCommandForLinuxIsUnsupported(t *testing.T) {
	// Verifies Linux update fails before trying to run a platform-specific installer.
	_, _, err := updateCommandForOS("linux")
	if err == nil {
		t.Fatal("expected unsupported OS error")
	}
	if !strings.Contains(err.Error(), "macOS and Windows") {
		t.Fatalf("unexpected linux error: %v", err)
	}
}

func TestUpdateCommandRejectsUnsupportedOS(t *testing.T) {
	// Verifies unknown OS values are rejected.
	_, _, err := updateCommandForOS("plan9")
	if err == nil {
		t.Fatal("expected unsupported OS error")
	}
}
