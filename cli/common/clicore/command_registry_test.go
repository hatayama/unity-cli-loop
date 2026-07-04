package clicore

import "testing"

func TestNativeCommandEntriesDeclareOwners(t *testing.T) {
	// Verifies native command ownership lives in the command registry.
	expectedOwners := map[string]CommandOwner{
		LaunchCommandName:               DispatcherOwned,
		InstallCommandName:              DispatcherOwned,
		UpdateCommandName:               DispatcherOwned,
		UninstallCommandName:            DispatcherOwned,
		SkillsCommandName:               DispatcherOwned,
		CompletionCommand:               DispatcherOwned,
		"list":                          RunnerOwned,
		"sync":                          RunnerOwned,
		"focus-window":                  RunnerOwned,
		PausePointWaitCommandName:       RunnerOwned,
		PausePointStatusUserCommandName: RunnerOwned,
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
		SkillsCommandName,
		CompletionCommand,
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
		PausePointWaitCommandName,
		PausePointStatusUserCommandName,
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
		SkillsCommandName,
		CompletionCommand,
		CompileCommandName,
		"",
	} {
		if IsRunnerOwnedCommandName(command) {
			t.Fatalf("%s must not be runner-owned", command)
		}
	}
}
