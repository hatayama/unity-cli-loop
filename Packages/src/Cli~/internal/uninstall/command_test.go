package uninstall

import (
	"strings"
	"testing"
)

func TestCommandForDarwinRemovesUloopFromInstallDirectory(t *testing.T) {
	// Verifies macOS uninstall removes the launcher binary from the selected install directory.
	command, err := CommandForOS("darwin", Options{
		InstallDir: "/Users/ExampleUser/.local/bin",
		CurrentPID: 1234,
	})
	if err != nil {
		t.Fatalf("CommandForOS failed: %v", err)
	}

	if command.Name != "sh" {
		t.Fatalf("command name mismatch: %s", command.Name)
	}
	joinedArgs := strings.Join(command.Args, " ")
	if !strings.Contains(joinedArgs, "/Users/ExampleUser/.local/bin/uloop") {
		t.Fatalf("target path missing: %s", joinedArgs)
	}
	if !strings.Contains(joinedArgs, "rm -f") {
		t.Fatalf("remove command missing: %s", joinedArgs)
	}
	if command.TargetPath != "/Users/ExampleUser/.local/bin/uloop" {
		t.Fatalf("target path mismatch: %s", command.TargetPath)
	}
}

func TestCommandForWindowsSchedulesRemovalAfterCurrentProcessExits(t *testing.T) {
	// Verifies Windows uninstall defers deletion until the running launcher process exits.
	command, err := CommandForOS("windows", Options{
		InstallDir: `C:\Users\ExampleUser\AppData\Local\Programs\uloop\bin`,
		CurrentPID: 5678,
	})
	if err != nil {
		t.Fatalf("CommandForOS failed: %v", err)
	}

	if command.Name != "powershell" {
		t.Fatalf("command name mismatch: %s", command.Name)
	}
	joinedArgs := strings.Join(command.Args, " ")
	if !strings.Contains(joinedArgs, "-EncodedCommand") {
		t.Fatalf("encoded command flag missing: %s", joinedArgs)
	}
	deletionScript := windowsDeletionScript(command.TargetPath, 5678)
	for _, expected := range []string{
		"Get-Process -Id $ParentPid",
		"$ParentProcess | Wait-Process",
		"$ParentPid = 5678",
		`return $Path.Trim().Trim('"').TrimEnd([char[]]@('\','/')).Replace('/','\')`,
		"[Environment]::SetEnvironmentVariable('Path', $NewUserPath, 'User')",
	} {
		if !strings.Contains(deletionScript, expected) {
			t.Fatalf("expected %s in deletion script: %s", expected, deletionScript)
		}
	}
	if !command.Deferred {
		t.Fatal("windows uninstall should be deferred")
	}
	if command.TargetPath != `C:\Users\ExampleUser\AppData\Local\Programs\uloop\bin\uloop.exe` {
		t.Fatalf("target path mismatch: %s", command.TargetPath)
	}
}

func TestCommandForWindowsRemovesUserPathBeforeDeletingLauncher(t *testing.T) {
	// Verifies Unity does not observe launcher removal before persistent PATH cleanup finishes.
	deletionScript := windowsDeletionScript(
		`C:\Users\ExampleUser\AppData\Local\Programs\uloop\bin\uloop.exe`,
		5678)

	pathRemovalIndex := strings.Index(deletionScript, "[Environment]::SetEnvironmentVariable('Path', $NewUserPath, 'User')")
	targetRemovalIndex := strings.Index(deletionScript, "Remove-Item -LiteralPath $Target")
	if pathRemovalIndex < 0 {
		t.Fatalf("path removal missing from deletion script: %s", deletionScript)
	}
	if targetRemovalIndex < 0 {
		t.Fatalf("target removal missing from deletion script: %s", deletionScript)
	}
	if pathRemovalIndex > targetRemovalIndex {
		t.Fatalf("target removal must happen after User PATH cleanup: %s", deletionScript)
	}
}

func TestCommandForOSRejectsUnsupportedOS(t *testing.T) {
	// Verifies unsupported platforms fail before building any destructive command.
	_, err := CommandForOS("linux", Options{
		InstallDir: "/tmp/bin",
		CurrentPID: 1234,
	})
	if err == nil {
		t.Fatal("expected unsupported OS error")
	}
	if !strings.Contains(err.Error(), "macOS and Windows") {
		t.Fatalf("unexpected unsupported OS error: %v", err)
	}
}
