package cli

import (
	"strings"
	"testing"

	clicontract "github.com/hatayama/unity-cli-loop/cli"
	"github.com/hatayama/unity-cli-loop/cli/internal/update"
)

func TestUpdateCommandForDarwinUsesDirectInstaller(t *testing.T) {
	// Verifies dispatcher update downloads the shared installer before running it on the matching channel.
	commandName, args, err := updateCommandForOS("darwin")
	if err != nil {
		t.Fatalf("updateCommandForOS failed: %v", err)
	}

	if commandName != "sh" {
		t.Fatalf("command mismatch: %s", commandName)
	}
	joinedArgs := strings.Join(args, " ")
	expectedScriptURL := update.ScriptURL(clicontract.DispatcherCurrent.DispatcherVersion, update.PosixScriptName)
	expectedReleaseTag := update.UpdateSelectorForVersion(clicontract.DispatcherCurrent.DispatcherVersion)
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
	// Verifies dispatcher update calls the same Windows installer script on the matching channel.
	commandName, args, err := updateCommandForOS("windows")
	if err != nil {
		t.Fatalf("updateCommandForOS failed: %v", err)
	}

	if commandName != windowsPowerShellCommand {
		t.Fatalf("command mismatch: %s", commandName)
	}
	joinedArgs := strings.Join(args, " ")
	expectedScriptURL := update.ScriptURL(clicontract.DispatcherCurrent.DispatcherVersion, update.WindowsScriptName)
	expectedReleaseTag := update.UpdateSelectorForVersion(clicontract.DispatcherCurrent.DispatcherVersion)
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

func TestUpdateCommandForDarwinUsesRequestedVersion(t *testing.T) {
	// Verifies dispatcher update can target the minimum release version requested by Unity.
	commandName, args, err := updateCommandForOSWithOptions("darwin", updateOptions{
		targetVersion: "3.0.0-beta.6",
	})
	if err != nil {
		t.Fatalf("updateCommandForOSWithOptions failed: %v", err)
	}

	if commandName != "sh" {
		t.Fatalf("command mismatch: %s", commandName)
	}
	joinedArgs := strings.Join(args, " ")
	if !strings.Contains(joinedArgs, "dispatcher-v3.0.0-beta.6/scripts/install.sh") {
		t.Fatalf("installer URL mismatch: %s", joinedArgs)
	}
	if !strings.Contains(joinedArgs, "ULOOP_VERSION='dispatcher-v3.0.0-beta.6'") {
		t.Fatalf("installer version missing: %s", joinedArgs)
	}
}

func TestUpdateCommandForDarwinNormalizesRequestedVersionPrefix(t *testing.T) {
	// Verifies accepted v-prefixed semantic versions still resolve to valid dispatcher release tags.
	commandName, args, err := updateCommandForOSWithOptions("darwin", updateOptions{
		targetVersion: "v3.0.0-beta.6",
	})
	if err != nil {
		t.Fatalf("updateCommandForOSWithOptions failed: %v", err)
	}

	if commandName != "sh" {
		t.Fatalf("command mismatch: %s", commandName)
	}
	joinedArgs := strings.Join(args, " ")
	if !strings.Contains(joinedArgs, "ULOOP_VERSION='dispatcher-v3.0.0-beta.6'") {
		t.Fatalf("installer version should not contain a doubled v prefix: %s", joinedArgs)
	}
	if strings.Contains(joinedArgs, "dispatcher-vv3.0.0-beta.6") {
		t.Fatalf("installer version contains doubled v prefix: %s", joinedArgs)
	}
}

func TestUpdateCommandForWindowsUsesRequestedVersion(t *testing.T) {
	// Verifies Windows dispatcher update can target the minimum release version requested by Unity.
	commandName, args, err := updateCommandForOSWithOptions("windows", updateOptions{
		targetVersion: "3.0.0",
	})
	if err != nil {
		t.Fatalf("updateCommandForOSWithOptions failed: %v", err)
	}

	if commandName != windowsPowerShellCommand {
		t.Fatalf("command mismatch: %s", commandName)
	}
	joinedArgs := strings.Join(args, " ")
	if !strings.Contains(joinedArgs, "dispatcher-v3.0.0/scripts/install.ps1") {
		t.Fatalf("installer URL mismatch: %s", joinedArgs)
	}
	if !strings.Contains(joinedArgs, "$env:ULOOP_VERSION='dispatcher-v3.0.0'") {
		t.Fatalf("installer version missing: %s", joinedArgs)
	}
}

func TestParseUpdateOptionsNormalizesVersionPrefix(t *testing.T) {
	// Verifies parsed target versions are normalized before installer tag selection.
	options, err := parseUpdateOptions([]string{"--to-version", "v3.0.0-beta.6"})
	if err != nil {
		t.Fatalf("parseUpdateOptions failed: %v", err)
	}

	if options.targetVersion != "3.0.0-beta.6" {
		t.Fatalf("target version mismatch: %#v", options)
	}
}

func TestParseUpdateOptionsAcceptsEqualsSyntax(t *testing.T) {
	// Verifies AI-readable update commands may use a single --to-version=value token.
	options, err := parseUpdateOptions([]string{"--to-version=3.0.0-beta.6"})
	if err != nil {
		t.Fatalf("parseUpdateOptions failed: %v", err)
	}

	if options.targetVersion != "3.0.0-beta.6" {
		t.Fatalf("target version mismatch: %#v", options)
	}
}

func TestParseUpdateOptionsRejectsInvalidVersion(t *testing.T) {
	// Verifies invalid requested update versions fail before installer execution.
	_, err := parseUpdateOptions([]string{"--to-version", "not-a-version"})
	if err == nil {
		t.Fatal("expected invalid version error")
	}
}

func TestUpdateCommandForLinuxIsUnsupported(t *testing.T) {
	// Verifies Linux update fails before trying to run a platform-specific update.
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
