package cli

import (
	"strings"
	"testing"
)

func TestUpdateCommandForDarwinUsesDirectInstaller(t *testing.T) {
	commandName, args, err := updateCommandForOS("darwin")
	if err != nil {
		t.Fatalf("updateCommandForOS failed: %v", err)
	}

	if commandName != "sh" {
		t.Fatalf("command mismatch: %s", commandName)
	}
	joinedArgs := strings.Join(args, " ")
	if !strings.Contains(joinedArgs, installerScriptURL) {
		t.Fatalf("installer URL missing: %s", joinedArgs)
	}
	if strings.Contains(joinedArgs, "npm") {
		t.Fatalf("update command still references npm: %s", joinedArgs)
	}
}

func TestUpdateCommandForWindowsUsesPowerShellInstaller(t *testing.T) {
	commandName, args, err := updateCommandForOS("windows")
	if err != nil {
		t.Fatalf("updateCommandForOS failed: %v", err)
	}

	if commandName != windowsPowerShellCommand {
		t.Fatalf("command mismatch: %s", commandName)
	}
	joinedArgs := strings.Join(args, " ")
	if !strings.Contains(joinedArgs, windowsInstallerScriptURL) {
		t.Fatalf("installer URL missing: %s", joinedArgs)
	}
	if strings.Contains(joinedArgs, "npm") {
		t.Fatalf("update command still references npm: %s", joinedArgs)
	}
}

func TestUpdateCommandForLinuxIsUnsupported(t *testing.T) {
	_, _, err := updateCommandForOS("linux")
	if err == nil {
		t.Fatal("expected unsupported OS error")
	}
	if !strings.Contains(err.Error(), "macOS and Windows") {
		t.Fatalf("unexpected linux error: %v", err)
	}
}

func TestUpdateCommandRejectsUnsupportedOS(t *testing.T) {
	_, _, err := updateCommandForOS("plan9")
	if err == nil {
		t.Fatal("expected unsupported OS error")
	}
}
