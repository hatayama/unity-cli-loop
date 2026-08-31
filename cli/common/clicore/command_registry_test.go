package clicore

import "testing"

func TestNativeCommandEntriesDeclareOwners(t *testing.T) {
	// Verifies native command ownership lives in the command registry.
	expectedOwners := map[string]CommandOwner{
		LaunchCommandName:               DispatcherOwned,
		InstallCommandName:              DispatcherOwned,
		UpdateCommandName:               DispatcherOwned,
		UninstallCommandName:            DispatcherOwned,
		VersionCommandName:              DispatcherOwned,
		SkillsCommandName:               DispatcherOwned,
		PackageCommandName:              DispatcherOwned,
		"list":                          RunnerOwned,
		"sync":                          RunnerOwned,
		"focus-window":                  RunnerOwned,
		PausePointAwaitCommandName:      RunnerOwned,
		PausePointStatusUserCommandName: RunnerOwned,
		SetCodeOptimizationCommandName:  RunnerOwned,
	}
	if len(NativeCommands) != len(expectedOwners) {
		t.Fatalf("native command owner fixture is stale: %#v", NativeCommands)
	}
	for _, command := range NativeCommands {
		expectedOwner, ok := expectedOwners[command.Name]
		if !ok {
			t.Fatalf("unexpected native command in registry: %s", command.Name)
		}
		if command.Owner != expectedOwner {
			t.Fatalf("%s owner mismatch: %s", command.Name, command.Owner)
		}
	}
}

// Verifies the dispatcher-owned command set covers exactly the bootstrap commands and nothing forwarded to a project runner.
func TestIsDispatcherOwnedCommandName(t *testing.T) {
	for _, command := range []string{
		LaunchCommandName,
		InstallCommandName,
		UpdateCommandName,
		UninstallCommandName,
		VersionCommandName,
		SkillsCommandName,
		PackageCommandName,
	} {
		if !IsDispatcherOwnedCommandName(command) {
			t.Fatalf("%s must be dispatcher-owned", command)
		}
	}
	for _, command := range []string{
		CompileCommandName,
		ExecuteDynamicCodeCommandName,
		RunTestsCommandName,
		"list",
		"sync",
		"",
	} {
		if IsDispatcherOwnedCommandName(command) {
			t.Fatalf("%s must not be dispatcher-owned", command)
		}
	}
}

func TestIsRunnerOwnedCommandName(t *testing.T) {
	// Verifies project-runner native command routing is derived from registry ownership.
	for _, command := range []string{
		"list",
		"sync",
		"focus-window",
		PausePointAwaitCommandName,
		PausePointStatusUserCommandName,
		SetCodeOptimizationCommandName,
	} {
		if !IsRunnerOwnedCommandName(command) {
			t.Fatalf("%s must be runner-owned", command)
		}
	}
	for _, command := range []string{
		LaunchCommandName,
		InstallCommandName,
		UpdateCommandName,
		UninstallCommandName,
		VersionCommandName,
		SkillsCommandName,
		PackageCommandName,
		CompileCommandName,
		"",
	} {
		if IsRunnerOwnedCommandName(command) {
			t.Fatalf("%s must not be runner-owned", command)
		}
	}
}

// Verifies pause-point-status help describes both a targeted query and an id-less marker list.
func TestPausePointStatusNativeCommandDescription(t *testing.T) {
	entry, found := NativeCommand(PausePointStatusUserCommandName)
	if !found {
		t.Fatalf("%s must be registered", PausePointStatusUserCommandName)
	}

	const want = "Show one pause point marker's state, or list all registered markers when no target is given"
	if entry.Description != want {
		t.Fatalf("description = %q, want %q", entry.Description, want)
	}
}
