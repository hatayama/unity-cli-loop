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
		"$NpmArgs = @('uninstall', '-g', '--prefix', $LegacyPrefix, 'uloop-cli')",
		"$null = & $NpmCommand.Source @('uninstall', '-g', 'uloop-cli')",
		"Report-PathShadowing",
		"function Get-FirstUloopCommandFromPath",
		"$NormalizedPathEntry = & $NormalizePath $PathEntry",
		"if ($NormalizedPathEntry -match '^[A-Za-z]:$') {\n            $NormalizedPathEntry = $NormalizedPathEntry + '\\'\n        }",
		"$CandidatePath = Join-Path $NormalizedPathEntry $ShimName",
		"$MachinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')",
		"$ResolvedPath = Get-FirstUloopCommandFromPath -PathValue ([string]::Join(';', @($MachinePath, $UserPath)))",
		"function Write-LegacyNpmMultilineArgumentWarning",
		"if (Test-LegacyNpmUloopPath -CommandPath $ResolvedPath) {\n        Write-LegacyNpmMultilineArgumentWarning\n    }",
		"Legacy npm shims can alter multiline PowerShell arguments before the native CLI receives them.",
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
	if count := strings.Count(setupScript, "Legacy npm shims can alter multiline PowerShell arguments before the native CLI receives them."); count != 1 {
		t.Fatalf("legacy npm multiline warning should have one message definition, got %d", count)
	}
	if count := strings.Count(setupScript, "Write-LegacyNpmMultilineArgumentWarning"); count != 2 {
		t.Fatalf("legacy npm multiline warning should be centralized and called from one site, got %d occurrences", count)
	}
	if strings.Contains(setupScript, "Failed to remove the legacy npm uloop-cli package.") {
		t.Fatal("legacy npm cleanup failure should not fail installation")
	}
	if strings.Contains(setupScript, "Get-Command uloop -ErrorAction SilentlyContinue") {
		t.Fatal("Windows path shadowing should inspect persisted PATH instead of the current process PATH")
	}
	if strings.Contains(setupScript, "$RemovedAll") {
		t.Fatal("legacy npm cleanup aggregate result should not be tracked when it is intentionally ignored")
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

func TestCommandForMacPreservesRootInstallDir(t *testing.T) {
	// Verifies macOS install keeps the root directory when removing trailing separators.
	command, err := CommandForOS("darwin", Options{
		InstallDir: "/",
	})
	if err != nil {
		t.Fatalf("CommandForOS failed: %v", err)
	}

	if command.InstallDir != "/" {
		t.Fatalf("install dir mismatch: %s", command.InstallDir)
	}
	if command.TargetPath != "/uloop" {
		t.Fatalf("target path mismatch: %s", command.TargetPath)
	}
}

func shellProfileQuoteForTest(value string) string {
	return "'" + strings.ReplaceAll(value, "'", "'\\''") + "'"
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
		"export PATH=" + shellProfileQuoteForTest(installDir) + ":$PATH",
		"# <<< uloop PATH <<<",
	} {
		if !strings.Contains(content, expected) {
			t.Fatalf("profile content missing %q:\n%s", expected, content)
		}
	}
}

func TestPosixInstallScriptCreatesNestedFishProfile(t *testing.T) {
	// Verifies macOS shell setup creates nested shell profile directories when needed.
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
		"SHELL=/opt/homebrew/bin/fish",
		"PATH=/usr/bin:/bin:/usr/sbin:/sbin",
	}
	output, err := process.CombinedOutput()
	if err != nil {
		t.Fatalf("POSIX setup failed: %v\n%s", err, output)
	}

	profilePath := filepath.Join(home, ".config", "fish", "config.fish")
	profileContent, err := os.ReadFile(profilePath)
	if err != nil {
		t.Fatalf("failed to read fish profile: %v\n%s", err, output)
	}
	content := string(profileContent)
	for _, expected := range []string{
		"# >>> uloop PATH >>>",
		"fish_add_path --move " + shellProfileQuoteForTest(installDir),
		"# <<< uloop PATH <<<",
	} {
		if !strings.Contains(content, expected) {
			t.Fatalf("profile content missing %q:\n%s", expected, content)
		}
	}
}

func TestPosixInstallScriptWritesThroughSymlinkedProfile(t *testing.T) {
	// Verifies macOS shell setup preserves managed shell profile symlinks.
	if runtime.GOOS == "windows" {
		t.Skip("POSIX shell setup is not available on Windows")
	}

	home := t.TempDir()
	dotfilesDir := filepath.Join(home, "dotfiles")
	if err := os.MkdirAll(dotfilesDir, 0o755); err != nil {
		t.Fatalf("failed to create dotfiles directory: %v", err)
	}
	targetProfile := filepath.Join(dotfilesDir, "zshrc")
	if err := os.WriteFile(targetProfile, []byte("export EXISTING_PROFILE=1\n"), 0o644); err != nil {
		t.Fatalf("failed to write target profile: %v", err)
	}
	profilePath := filepath.Join(home, ".zshrc")
	if err := os.Symlink(filepath.Join("dotfiles", "zshrc"), profilePath); err != nil {
		t.Fatalf("failed to create profile symlink: %v", err)
	}

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

	profileInfo, err := os.Lstat(profilePath)
	if err != nil {
		t.Fatalf("failed to stat profile symlink: %v", err)
	}
	if profileInfo.Mode()&os.ModeSymlink == 0 {
		t.Fatalf("profile symlink was replaced: %s", profileInfo.Mode())
	}
	profileContent, err := os.ReadFile(targetProfile)
	if err != nil {
		t.Fatalf("failed to read target profile: %v", err)
	}
	content := string(profileContent)
	for _, expected := range []string{
		"export EXISTING_PROFILE=1",
		"export PATH=" + shellProfileQuoteForTest(installDir) + ":$PATH",
	} {
		if !strings.Contains(content, expected) {
			t.Fatalf("profile target missing %q:\n%s", expected, content)
		}
	}
}

func TestPosixInstallScriptPreservesProfileWhenFilteringFails(t *testing.T) {
	// Verifies macOS shell setup does not replace an existing profile when its current content cannot be read.
	if runtime.GOOS == "windows" {
		t.Skip("POSIX shell setup is not available on Windows")
	}

	home := t.TempDir()
	mockBin := filepath.Join(home, "mock-bin")
	if err := os.MkdirAll(mockBin, 0o755); err != nil {
		t.Fatalf("failed to create mock bin: %v", err)
	}
	if err := os.WriteFile(filepath.Join(mockBin, "awk"), []byte("#!/bin/sh\nexit 7\n"), 0o755); err != nil {
		t.Fatalf("failed to write mock awk: %v", err)
	}
	profilePath := filepath.Join(home, ".zshrc")
	existingProfile := "export EXISTING_PROFILE=1\n"
	if err := os.WriteFile(profilePath, []byte(existingProfile), 0o644); err != nil {
		t.Fatalf("failed to write existing profile: %v", err)
	}

	command, err := CommandForOS("darwin", Options{
		InstallDir: filepath.Join(home, ".local", "bin"),
	})
	if err != nil {
		t.Fatalf("CommandForOS failed: %v", err)
	}

	process := exec.Command(command.Name, command.Args...)
	process.Env = []string{
		"HOME=" + home,
		"ZDOTDIR=" + home,
		"SHELL=/bin/zsh",
		"PATH=" + mockBin + ":/usr/bin:/bin:/usr/sbin:/sbin",
	}
	output, err := process.CombinedOutput()
	if err == nil {
		t.Fatalf("expected POSIX setup failure:\n%s", output)
	}
	if !strings.Contains(string(output), "Could not read existing shell profile") {
		t.Fatalf("setup failure did not include profile read error:\n%s", output)
	}
	profileContent, err := os.ReadFile(profilePath)
	if err != nil {
		t.Fatalf("failed to read profile after setup failure: %v", err)
	}
	if string(profileContent) != existingProfile {
		t.Fatalf("profile should remain unchanged:\n%s", profileContent)
	}
}

func TestPosixInstallScriptEscapesInstallDirInProfiles(t *testing.T) {
	// Verifies macOS shell setup writes literal install paths into persisted shell profile commands.
	if runtime.GOOS == "windows" {
		t.Skip("POSIX shell setup is not available on Windows")
	}

	home := t.TempDir()
	installDir := filepath.Join(home, "bin with $HOME \"quote\" `date` 'single' (echo hi)")
	for _, scenario := range []struct {
		name        string
		env         []string
		profilePath string
		expected    string
	}{
		{
			name: "zsh",
			env: []string{
				"HOME=" + home,
				"ZDOTDIR=" + home,
				"SHELL=/bin/zsh",
				"PATH=/usr/bin:/bin:/usr/sbin:/sbin",
			},
			profilePath: filepath.Join(home, ".zshrc"),
			expected:    "export PATH=" + shellProfileQuoteForTest(installDir) + ":$PATH",
		},
		{
			name: "fish",
			env: []string{
				"HOME=" + home,
				"SHELL=/opt/homebrew/bin/fish",
				"PATH=/usr/bin:/bin:/usr/sbin:/sbin",
			},
			profilePath: filepath.Join(home, ".config", "fish", "config.fish"),
			expected:    "fish_add_path --move " + shellProfileQuoteForTest(installDir),
		},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			command, err := CommandForOS("darwin", Options{
				InstallDir: installDir,
			})
			if err != nil {
				t.Fatalf("CommandForOS failed: %v", err)
			}

			process := exec.Command(command.Name, command.Args...)
			process.Env = scenario.env
			output, err := process.CombinedOutput()
			if err != nil {
				t.Fatalf("POSIX setup failed: %v\n%s", err, output)
			}
			profileContent, err := os.ReadFile(scenario.profilePath)
			if err != nil {
				t.Fatalf("failed to read shell profile: %v\n%s", err, output)
			}
			if !strings.Contains(string(profileContent), scenario.expected) {
				t.Fatalf("profile content missing escaped install dir %q:\n%s", scenario.expected, profileContent)
			}
		})
	}
}

func TestPosixInstallScriptFailsWhenSymlinkProfileTargetCannotBeWritten(t *testing.T) {
	// Verifies macOS shell setup reports profile write failures to the installer.
	if runtime.GOOS == "windows" {
		t.Skip("POSIX shell setup is not available on Windows")
	}

	home := t.TempDir()
	profilePath := filepath.Join(home, ".zshrc")
	if err := os.Symlink(filepath.Join(string(os.PathSeparator), "dev", "null", "uloop"), profilePath); err != nil {
		t.Fatalf("failed to create profile symlink: %v", err)
	}

	command, err := CommandForOS("darwin", Options{
		InstallDir: filepath.Join(home, ".local", "bin"),
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
	if err == nil {
		t.Fatalf("expected POSIX setup failure:\n%s", output)
	}
	if !strings.Contains(string(output), "Could not create shell profile directory") {
		t.Fatalf("setup failure did not include profile directory error:\n%s", output)
	}
}

func TestPosixInstallScriptFailsWhenShellProfileIsUnknown(t *testing.T) {
	// Verifies macOS shell setup reports manual PATH setup when no profile can be updated.
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
		"SHELL=/bin/tcsh",
		"PATH=/usr/bin:/bin:/usr/sbin:/sbin",
	}
	output, err := process.CombinedOutput()
	if err == nil {
		t.Fatalf("expected POSIX setup failure:\n%s", output)
	}
	outputText := string(output)
	if !strings.Contains(outputText, "Add this directory to PATH in your shell profile:") {
		t.Fatalf("setup failure should include manual PATH guidance:\n%s", outputText)
	}
	if !strings.Contains(outputText, installDir) {
		t.Fatalf("manual PATH guidance should include install dir:\n%s", outputText)
	}
}

func TestPosixInstallScriptRemovesAbsoluteLegacyNpmShimBeforePrependingPath(t *testing.T) {
	// Verifies macOS legacy cleanup sees the old npm shim before the native path is prepended.
	if runtime.GOOS == "windows" {
		t.Skip("POSIX shell setup is not available on Windows")
	}

	home := t.TempDir()
	installDir := filepath.Join(home, ".local", "bin")
	legacyPrefix := filepath.Join(home, "npm-global")
	legacyBin := filepath.Join(legacyPrefix, "bin")
	mockBin := filepath.Join(home, "mock-bin")
	npmLog := filepath.Join(home, "npm.log")
	for _, dir := range []string{installDir, legacyBin, mockBin} {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			t.Fatalf("failed to create test directory: %v", err)
		}
	}
	if err := os.WriteFile(filepath.Join(installDir, PosixCommandName), []byte("#!/bin/sh\necho native uloop\n"), 0o755); err != nil {
		t.Fatalf("failed to write native uloop: %v", err)
	}
	if err := os.WriteFile(filepath.Join(legacyBin, PosixCommandName), []byte("#!/bin/sh\n# node_modules/uloop-cli legacy shim\necho legacy uloop\n"), 0o755); err != nil {
		t.Fatalf("failed to write legacy uloop: %v", err)
	}
	if err := os.WriteFile(filepath.Join(mockBin, "npm"), []byte("#!/bin/sh\nprintf '%s\\n' \"$*\" >> \"$NPM_LOG\"\n"), 0o755); err != nil {
		t.Fatalf("failed to write mock npm: %v", err)
	}

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
		"NPM_LOG=" + npmLog,
		"PATH=" + legacyBin + ":" + mockBin + ":/usr/bin:/bin:/usr/sbin:/sbin",
	}
	output, err := process.CombinedOutput()
	if err != nil {
		t.Fatalf("POSIX setup failed: %v\n%s", err, output)
	}

	npmLogContent, err := os.ReadFile(npmLog)
	if err != nil {
		t.Fatalf("failed to read npm log: %v\n%s", err, output)
	}
	expected := "uninstall -g --prefix " + legacyPrefix + " uloop-cli"
	if !strings.Contains(string(npmLogContent), expected) {
		t.Fatalf("npm log missing %q:\n%s", expected, npmLogContent)
	}
}

func TestPosixInstallScriptSilencesLegacyFailureWhenLegacyShimRemains(t *testing.T) {
	// Verifies macOS legacy cleanup stays quiet when npm leaves the old shim behind.
	if runtime.GOOS == "windows" {
		t.Skip("POSIX shell setup is not available on Windows")
	}

	home := t.TempDir()
	installDir := filepath.Join(home, ".local", "bin")
	legacyPrefix := filepath.Join(home, "npm-global")
	legacyBin := filepath.Join(legacyPrefix, "bin")
	mockBin := filepath.Join(home, "mock-bin")
	for _, dir := range []string{installDir, legacyBin, mockBin} {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			t.Fatalf("failed to create test directory: %v", err)
		}
	}
	if err := os.WriteFile(filepath.Join(installDir, PosixCommandName), []byte("#!/bin/sh\necho native uloop\n"), 0o755); err != nil {
		t.Fatalf("failed to write native uloop: %v", err)
	}
	legacyUloop := filepath.Join(legacyBin, PosixCommandName)
	if err := os.WriteFile(legacyUloop, []byte("#!/bin/sh\n# node_modules/uloop-cli legacy shim\necho legacy uloop\n"), 0o755); err != nil {
		t.Fatalf("failed to write legacy uloop: %v", err)
	}
	if err := os.WriteFile(filepath.Join(mockBin, "npm"), []byte("#!/bin/sh\nexit 0\n"), 0o755); err != nil {
		t.Fatalf("failed to write mock npm: %v", err)
	}

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
		"PATH=" + legacyBin + ":" + mockBin + ":/usr/bin:/bin:/usr/sbin:/sbin",
	}
	output, err := process.CombinedOutput()
	if err != nil {
		t.Fatalf("POSIX setup failed: %v\n%s", err, output)
	}

	outputText := string(output)
	if strings.Contains(outputText, "Removed legacy npm package: uloop-cli") {
		t.Fatalf("cleanup should not report success while the legacy shim remains:\n%s", outputText)
	}
	if strings.Contains(outputText, "Could not remove the legacy npm package automatically.") {
		t.Fatalf("cleanup should not report legacy npm removal failure:\n%s", outputText)
	}
	if strings.Contains(outputText, "npm uninstall -g --prefix \""+legacyPrefix+"\" uloop-cli") {
		t.Fatalf("cleanup should not print manual removal guidance:\n%s", outputText)
	}
	if _, err := os.Stat(legacyUloop); err != nil {
		t.Fatalf("legacy uloop should remain for this scenario: %v", err)
	}
}

func TestPosixInstallScriptSkipsDefaultNpmCleanupForInstallPrefix(t *testing.T) {
	// Verifies default npm cleanup does not remove the freshly installed native launcher.
	if runtime.GOOS == "windows" {
		t.Skip("POSIX shell setup is not available on Windows")
	}

	home := t.TempDir()
	npmPrefix := filepath.Join(home, "npm-global")
	installDir := filepath.Join(npmPrefix, "bin")
	mockBin := filepath.Join(home, "mock-bin")
	npmLog := filepath.Join(home, "npm.log")
	for _, dir := range []string{installDir, mockBin} {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			t.Fatalf("failed to create test directory: %v", err)
		}
	}
	if err := os.WriteFile(filepath.Join(installDir, PosixCommandName), []byte("#!/bin/sh\necho native uloop\n"), 0o755); err != nil {
		t.Fatalf("failed to write native uloop: %v", err)
	}
	npmScript := "#!/bin/sh\n" +
		"if [ \"$1\" = \"prefix\" ] && [ \"$2\" = \"-g\" ]; then\n" +
		"  echo \"$NPM_PREFIX\"\n" +
		"  exit 0\n" +
		"fi\n" +
		"printf '%s\\n' \"$*\" >> \"$NPM_LOG\"\n"
	if err := os.WriteFile(filepath.Join(mockBin, "npm"), []byte(npmScript), 0o755); err != nil {
		t.Fatalf("failed to write mock npm: %v", err)
	}

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
		"NPM_LOG=" + npmLog,
		"NPM_PREFIX=" + npmPrefix,
		"PATH=" + installDir + ":" + mockBin + ":/usr/bin:/bin:/usr/sbin:/sbin",
	}
	output, err := process.CombinedOutput()
	if err != nil {
		t.Fatalf("POSIX setup failed: %v\n%s", err, output)
	}

	npmLogContent, err := os.ReadFile(npmLog)
	if err != nil && !os.IsNotExist(err) {
		t.Fatalf("failed to read npm log: %v\n%s", err, output)
	}
	if strings.Contains(string(npmLogContent), "uninstall -g uloop-cli") {
		t.Fatalf("default npm cleanup should be skipped:\n%s", npmLogContent)
	}
	if _, err := os.Stat(filepath.Join(installDir, PosixCommandName)); err != nil {
		t.Fatalf("native uloop should remain installed: %v", err)
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
