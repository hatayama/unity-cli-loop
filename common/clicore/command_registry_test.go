package clicore

import "testing"

// Verifies the dispatcher-owned command set covers exactly the bootstrap commands and nothing forwarded to a project runner.
func TestIsDispatcherOwnedCommandName(t *testing.T) {
	for _, command := range []string{
		LaunchCommandName,
		InstallCommandName,
		UpdateCommandName,
		UninstallCommandName,
		SkillsCommandName,
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
		"",
	} {
		if IsDispatcherOwnedCommandName(command) {
			t.Fatalf("%s must not be dispatcher-owned", command)
		}
	}
}
