package uninstall

import (
	"encoding/base64"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
	"unicode/utf16"
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
	uninstallScript := posixUninstallScript(command.TargetPath)
	if !strings.Contains(uninstallScript, `TargetPath='/Users/ExampleUser/.local/bin/uloop'`) {
		t.Fatalf("target path assignment missing: %s", uninstallScript)
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
		"$ParentPid = [int]'5678'",
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

func TestPosixUninstallScriptReplacesTemplateValues(t *testing.T) {
	// Verifies POSIX uninstall templates cannot ship with unresolved placeholders.
	targetPath := "/tmp/uloop's bin/uloop"
	uninstallScript := posixUninstallScript(targetPath)

	if strings.Contains(uninstallScript, "{{") {
		t.Fatalf("uninstall script contains unresolved template placeholder: %s", uninstallScript)
	}
	if !strings.Contains(uninstallScript, `TargetPath='/tmp/uloop'"'"'s bin/uloop'`) {
		t.Fatalf("uninstall script does not quote target path correctly: %s", uninstallScript)
	}
}

func TestPosixUninstallScriptRemovesShellPathBlocks(t *testing.T) {
	// Verifies macOS uninstall removes only the shell PATH blocks owned by the installer.
	if runtime.GOOS == "windows" {
		t.Skip("POSIX uninstall script is not available on Windows")
	}

	home := t.TempDir()
	installDir := filepath.Join(home, ".local", "bin")
	if err := os.MkdirAll(installDir, 0o755); err != nil {
		t.Fatalf("failed to create install dir: %v", err)
	}
	targetPath := filepath.Join(installDir, PosixCommandName)
	if err := os.WriteFile(targetPath, []byte("binary"), 0o755); err != nil {
		t.Fatalf("failed to create target binary: %v", err)
	}

	profilePaths := []string{
		filepath.Join(home, ".bash_profile"),
		filepath.Join(home, ".zshrc"),
		filepath.Join(home, ".config", "fish", "config.fish"),
	}
	for _, profilePath := range profilePaths {
		if err := os.MkdirAll(filepath.Dir(profilePath), 0o755); err != nil {
			t.Fatalf("failed to create profile dir: %v", err)
		}
		content := "before\n# >>> uloop PATH >>>\nexport PATH='" + installDir + ":$PATH'\n# <<< uloop PATH <<<\nafter\n"
		if err := os.WriteFile(profilePath, []byte(content), 0o600); err != nil {
			t.Fatalf("failed to write profile: %v", err)
		}
	}

	command, err := CommandForOS("darwin", Options{
		InstallDir: installDir,
		CurrentPID: 1234,
	})
	if err != nil {
		t.Fatalf("CommandForOS failed: %v", err)
	}

	process := exec.Command(command.Name, command.Args...)
	process.Env = []string{
		"HOME=" + home,
		"PATH=/usr/bin:/bin:/usr/sbin:/sbin",
	}
	output, err := process.CombinedOutput()
	if err != nil {
		t.Fatalf("POSIX uninstall failed: %v\n%s", err, output)
	}
	if _, err := os.Stat(targetPath); !os.IsNotExist(err) {
		t.Fatalf("target binary should be removed, stat err=%v", err)
	}

	for _, profilePath := range profilePaths {
		profileContent, err := os.ReadFile(profilePath)
		if err != nil {
			t.Fatalf("failed to read profile: %v", err)
		}
		content := string(profileContent)
		if strings.Contains(content, "# >>> uloop PATH >>>") || strings.Contains(content, "# <<< uloop PATH <<<") {
			t.Fatalf("profile still contains uloop marker block:\n%s", content)
		}
		if !strings.Contains(content, "before\n") || !strings.Contains(content, "after\n") {
			t.Fatalf("profile should preserve unrelated content:\n%s", content)
		}
	}
}

func TestWindowsUninstallScriptsReplaceTemplateValues(t *testing.T) {
	// Verifies Windows uninstall templates cannot ship with unresolved placeholders.
	targetPath := `C:\Temp\uloop's bin\uloop.exe`
	deletionScript := windowsDeletionScript(targetPath, 5678)
	encodedDeletionScript := encodePowerShellCommand(deletionScript)
	launchScript := windowsLaunchScript(encodedDeletionScript)

	for _, script := range []string{deletionScript, launchScript} {
		if strings.Contains(script, "{{") {
			t.Fatalf("uninstall script contains unresolved template placeholder: %s", script)
		}
	}
	if !strings.Contains(deletionScript, `$Target = 'C:\Temp\uloop''s bin\uloop.exe'`) {
		t.Fatalf("deletion script does not quote target path correctly: %s", deletionScript)
	}
	if !strings.Contains(deletionScript, `$ParentPid = [int]'5678'`) {
		t.Fatalf("deletion script does not quote parent pid correctly: %s", deletionScript)
	}
	if !strings.Contains(launchScript, "$EncodedDeletion = '") {
		t.Fatalf("launch script does not assign encoded deletion command: %s", launchScript)
	}
	decodedDeletionScript := decodePowerShellCommandForTest(t, encodedDeletionScript)
	if !strings.Contains(decodedDeletionScript, `$Target = 'C:\Temp\uloop''s bin\uloop.exe'`) {
		t.Fatalf("encoded deletion script does not contain replaced target path: %s", decodedDeletionScript)
	}
}

func decodePowerShellCommandForTest(t *testing.T, encodedCommand string) string {
	t.Helper()

	encodedBytes, err := base64.StdEncoding.DecodeString(encodedCommand)
	if err != nil {
		t.Fatalf("failed to decode PowerShell command: %v", err)
	}
	if len(encodedBytes)%2 != 0 {
		t.Fatalf("encoded PowerShell command byte length must be even: %d", len(encodedBytes))
	}

	utf16Values := make([]uint16, 0, len(encodedBytes)/2)
	for index := 0; index < len(encodedBytes); index += 2 {
		utf16Values = append(utf16Values, uint16(encodedBytes[index])|uint16(encodedBytes[index+1])<<8)
	}
	return string(utf16.Decode(utf16Values))
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
