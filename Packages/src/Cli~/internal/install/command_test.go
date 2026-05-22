package install

import (
	"context"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
	"time"
)

func TestCommandForWindowsConfiguresUserPathAndLegacyCleanup(t *testing.T) {
	// Verifies Windows install delegates PATH and legacy npm cleanup to the native setup command.
	command, err := CommandForOS("windows", Options{
		InstallDir: `C:\Users\ExampleUser\AppData\Local\Programs\uloop\bin`,
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
	setupScript := windowsInstallScript(command.InstallDir, command.TargetPath)
	for _, expected := range []string{
		"[Environment]::SetEnvironmentVariable('Path', $NewUserPath, 'User')",
		"GetExtension($CommandPath), '.exe'",
		"foreach ($ShimName in @('uloop', 'uloop.cmd', 'uloop.ps1'))",
		"Invoke-AllLegacyNpmPackageRemoval -ExpectedUloopPath $ExpectedUloopPath",
		"npm uninstall -g --prefix",
		"npm uninstall -g uloop-cli",
		"Report-PathShadowing",
	} {
		if !strings.Contains(setupScript, expected) {
			t.Fatalf("expected %s in setup script: %s", expected, setupScript)
		}
	}
	if !command.UpdatesPath {
		t.Fatal("windows install should update User PATH")
	}
	if !command.CleansLegacy {
		t.Fatal("windows install should clean legacy launchers")
	}
	if command.TargetPath != `C:\Users\ExampleUser\AppData\Local\Programs\uloop\bin\uloop.exe` {
		t.Fatalf("target path mismatch: %s", command.TargetPath)
	}
	if strings.Contains(setupScript, "foreach ($ShimName in @('uloop', 'uloop.cmd', 'uloop.ps1', 'uloop.exe'))") {
		t.Fatal("legacy npm shim detection should not content-scan native exe files")
	}
}

func TestCommandForMacConfiguresShellPathAndLegacyCleanup(t *testing.T) {
	// Verifies macOS install delegates shell PATH setup and legacy npm cleanup to the native setup command.
	command, err := CommandForOS("darwin", Options{
		InstallDir: "/Users/ExampleUser/.local/bin",
	})
	if err != nil {
		t.Fatalf("CommandForOS failed: %v", err)
	}

	if command.Name != "sh" {
		t.Fatalf("command name mismatch: %s", command.Name)
	}
	joinedArgs := strings.Join(command.Args, " ")
	if !strings.Contains(joinedArgs, "-c") {
		t.Fatalf("shell command flag missing: %s", joinedArgs)
	}
	setupScript := posixInstallScript(command.InstallDir, command.TargetPath)
	for _, expected := range []string{
		"# >>> uloop PATH >>>",
		"# <<< uloop PATH <<<",
		"fish_add_path --move",
		"npm uninstall -g --prefix",
		"npm uninstall -g uloop-cli",
		"report_path_shadowing",
	} {
		if !strings.Contains(setupScript, expected) {
			t.Fatalf("expected %s in setup script: %s", expected, setupScript)
		}
	}
	if !command.UpdatesPath {
		t.Fatal("macOS install should update shell PATH")
	}
	if !command.CleansLegacy {
		t.Fatal("macOS install should clean legacy launchers")
	}
	if command.TargetPath != "/Users/ExampleUser/.local/bin/uloop" {
		t.Fatalf("target path mismatch: %s", command.TargetPath)
	}
}

func TestPosixInstallScriptWritesZshPathBlock(t *testing.T) {
	// Verifies macOS shell setup writes a replaceable profile block for future terminals.
	if runtime.GOOS == "windows" {
		t.Skip("POSIX shell setup is not available on Windows")
	}

	home := t.TempDir()
	installDir := filepath.Join(home, ".local", "bin")
	command, err := CommandForOS("darwin", Options{
		InstallDir: installDir,
	})
	if err != nil {
		t.Fatalf("CommandForOS failed: %v", err)
	}

	process := exec.Command(command.Name, command.Args...)
	process.Env = []string{
		"HOME=" + home,
		"ZDOTDIR=" + home,
		"SHELL=/bin/zsh",
		"PATH=/usr/bin:/bin:/usr/sbin:/sbin",
	}
	output, err := process.CombinedOutput()
	if err != nil {
		t.Fatalf("POSIX setup failed: %v\n%s", err, output)
	}

	profileContent, err := os.ReadFile(filepath.Join(home, ".zshrc"))
	if err != nil {
		t.Fatalf("failed to read zsh profile: %v", err)
	}
	content := string(profileContent)
	for _, expected := range []string{
		"# >>> uloop PATH >>>",
		"export PATH=\"" + installDir + ":$PATH\"",
		"# <<< uloop PATH <<<",
	} {
		if !strings.Contains(content, expected) {
			t.Fatalf("profile content missing %q:\n%s", expected, content)
		}
	}
}

func TestCommandForOSRejectsUnsupportedOS(t *testing.T) {
	// Verifies unsupported platforms fail before building any setup command.
	_, err := CommandForOS("linux", Options{
		InstallDir: "/Users/ExampleUser/.local/bin",
	})
	if err == nil {
		t.Fatal("expected unsupported OS error")
	}
	if !strings.Contains(err.Error(), "macOS and Windows") {
		t.Fatalf("unexpected unsupported OS error: %v", err)
	}
}

func TestWindowsInstallScriptParsesOnWindows(t *testing.T) {
	// Verifies the embedded setup script remains valid PowerShell on Windows.
	if runtime.GOOS != "windows" {
		t.Skip("PowerShell parser check is Windows-only")
	}

	setupScript := windowsInstallScript(
		`C:\Users\ExampleUser\AppData\Local\Programs\uloop\bin`,
		`C:\Users\ExampleUser\AppData\Local\Programs\uloop\bin\uloop.exe`)
	scriptPath := t.TempDir() + `\install-setup.ps1`
	if err := os.WriteFile(scriptPath, []byte(setupScript), 0o600); err != nil {
		t.Fatalf("failed to write setup script: %v", err)
	}

	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	command := exec.CommandContext(
		ctx,
		"powershell",
		"-NoProfile",
		"-Command",
		`$parseErrors = $null; $null = [System.Management.Automation.PSParser]::Tokenize((Get-Content -Raw $args[0]), [ref]$parseErrors); if ($parseErrors) { $parseErrors | Out-String; exit 1 }`,
		scriptPath)
	output, err := command.CombinedOutput()
	if err != nil {
		t.Fatalf("embedded setup script does not parse: %v\n%s", err, output)
	}
}
