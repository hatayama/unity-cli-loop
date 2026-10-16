package clicore

import "testing"

func TestNativeCommandEntriesDeclareOwners(t *testing.T) {
	// Verifies native command ownership lives in the command registry.
	expectedOwners := map[string]CommandOwner{
		LaunchCommandName:               DispatcherOwned,
		CompileCheckCommandName:         DispatcherOwned,
		VerifyProjectCommandName:        DispatcherOwned,
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
		StatusCommandName:               RunnerOwned,
		CancelCommandName:               RunnerOwned,
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
		VerifyProjectCommandName,
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
		StatusCommandName,
		CancelCommandName,
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

// Verifies an entry marked hidden is still found by name but left out of the listed commands and
// of completion.
func TestVisibleNativeCommandsLeaveOutAHiddenEntry(t *testing.T) {
	original := NativeCommands
	t.Cleanup(func() { NativeCommands = original })
	const probe = "hidden-probe"
	NativeCommands = append(append([]NativeCommandEntry{}, original...),
		NativeCommandEntry{Name: probe, Owner: DispatcherOwned, Hidden: true})

	if _, found := NativeCommand(probe); !found {
		t.Fatalf("%s must stay routable", probe)
	}
	for _, entry := range VisibleNativeCommands() {
		if entry.Name == probe {
			t.Fatalf("%s must not be listed: %#v", probe, VisibleNativeCommands())
		}
	}
	for _, name := range NativeCommandNamesForCompletion() {
		if name == probe {
			t.Fatalf("%s must not be offered as a completion: %#v", probe, NativeCommandNamesForCompletion())
		}
	}
	if len(VisibleNativeCommands()) != len(original) {
		t.Fatalf("only %s should be hidden: %#v", probe, VisibleNativeCommands())
	}
}
