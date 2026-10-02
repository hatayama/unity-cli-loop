package dispatcher

import (
	"bytes"
	"context"
	"errors"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

func TestInstallDispatcherV2CLIInstallsVersionIntoVersionedCache(t *testing.T) {
	// Verifies the installer uses npm with an isolated cache directory for the requested version.
	cacheRoot := t.TempDir()
	var commandName string
	var commandArgs []string
	deps := dispatcherV2InstallDeps{
		runCommand: func(ctx context.Context, name string, args []string, stderr io.Writer) error {
			commandName = name
			commandArgs = append([]string{}, args...)
			installPath := args[2]
			writeInstalledDispatcherV2Package(t, installPath, "2.2.0")
			return nil
		},
	}

	installPath, err := installDispatcherV2CLI(context.Background(), cacheRoot, "2.2.0", "darwin", io.Discard, deps)
	if err != nil {
		t.Fatalf("install V2 CLI: %v", err)
	}
	wantPath := filepath.Join(cacheRoot, dispatcherV2CacheDirectoryName, "2.2.0")
	if installPath != wantPath {
		t.Fatalf("install path = %q, want %q", installPath, wantPath)
	}
	if commandName != dispatcherNPMCommandName {
		t.Fatalf("command name = %q, want %q", commandName, dispatcherNPMCommandName)
	}
	if len(commandArgs) != 4 || commandArgs[0] != "install" || commandArgs[1] != "--prefix" || commandArgs[3] != dispatcherV2CLIPackageName+"@2.2.0" {
		t.Fatalf("npm arguments = %#v", commandArgs)
	}
	if !filepath.IsAbs(commandArgs[2]) || filepath.Dir(commandArgs[2]) != filepath.Join(cacheRoot, dispatcherV2CacheDirectoryName) {
		t.Fatalf("npm prefix = %q, want temporary directory under version cache", commandArgs[2])
	}
	if !isInstalledDispatcherV2CLI(installPath, "2.2.0") {
		t.Fatalf("installed V2 CLI missing from %s", installPath)
	}
}

func TestInstallDispatcherV2CLISkipsNPMWhenRequestedVersionIsInstalled(t *testing.T) {
	// Verifies an already installed matching version does not invoke npm again.
	cacheRoot := t.TempDir()
	installPath := filepath.Join(cacheRoot, dispatcherV2CacheDirectoryName, "2.2.0")
	writeInstalledDispatcherV2Package(t, installPath, "2.2.0")
	deps := dispatcherV2InstallDeps{
		runCommand: func(context.Context, string, []string, io.Writer) error {
			t.Fatal("npm must not run for an installed matching V2 CLI")
			return nil
		},
	}

	actualPath, err := installDispatcherV2CLI(context.Background(), cacheRoot, "2.2.0", "darwin", io.Discard, deps)
	if err != nil {
		t.Fatalf("install V2 CLI: %v", err)
	}
	if actualPath != installPath {
		t.Fatalf("install path = %q, want %q", actualPath, installPath)
	}
}

func TestDispatcherV2NPMCommandNameUsesCmdOnWindows(t *testing.T) {
	// Verifies the Windows npm command uses the cmd shim executable.
	if actual := dispatcherV2NPMCommandName("windows"); actual != dispatcherNPMWindowsCommandName {
		t.Fatalf("npm command = %q, want %q", actual, dispatcherNPMWindowsCommandName)
	}
}

func writeInstalledDispatcherV2Package(t *testing.T, installPath string, version string) {
	t.Helper()
	packagePath := filepath.Join(installPath, "node_modules", dispatcherV2CLIPackageName, dispatcherPackageJSONFileName)
	if err := os.MkdirAll(filepath.Dir(packagePath), 0o755); err != nil {
		t.Fatalf("create installed package directory: %v", err)
	}
	content := "{\n  \"version\": \"" + version + "\"\n}\n"
	if err := os.WriteFile(packagePath, []byte(content), 0o644); err != nil {
		t.Fatalf("write installed package: %v", err)
	}
}

func TestInstallDispatcherV2CLIReportsInstallFailures(t *testing.T) {
	// Verifies a failing npm run or an npm run that installs nothing is reported and leaves no cached install.
	cases := []struct {
		name        string
		runCommand  func(context.Context, string, []string, io.Writer) error
		wantMessage string
	}{
		{
			name: "npm fails",
			runCommand: func(context.Context, string, []string, io.Writer) error {
				return errors.New("npm exited 1")
			},
			wantMessage: "npm exited 1",
		},
		{
			name: "npm installs another version",
			runCommand: func(_ context.Context, _ string, args []string, _ io.Writer) error {
				writeInstalledDispatcherV2Package(t, args[2], "2.1.0")
				return nil
			},
			wantMessage: "npm did not install uloop-cli@2.2.0",
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			cacheRoot := t.TempDir()

			_, err := installDispatcherV2CLI(context.Background(), cacheRoot, "2.2.0", "darwin", io.Discard, dispatcherV2InstallDeps{runCommand: testCase.runCommand})

			if err == nil || !strings.Contains(err.Error(), testCase.wantMessage) {
				t.Fatalf("expected error containing %q, got %v", testCase.wantMessage, err)
			}
			if fileExists(dispatcherV2InstallPath(cacheRoot, "2.2.0")) {
				t.Fatal("a failed install must not leave the versioned cache directory")
			}
		})
	}
}

func TestInstallDispatcherV2CLIReportsUnusableCacheRoot(t *testing.T) {
	// Verifies a cache root that is a regular file fails before npm runs.
	cacheRoot := filepath.Join(t.TempDir(), "cache-file")
	writeDispatcherTestFile(t, cacheRoot, "not a directory")
	deps := dispatcherV2InstallDeps{
		runCommand: func(context.Context, string, []string, io.Writer) error {
			t.Fatal("npm must not run when the cache directory cannot be created")
			return nil
		},
	}

	_, err := installDispatcherV2CLI(context.Background(), cacheRoot, "2.2.0", "darwin", io.Discard, deps)
	if err == nil || !strings.Contains(err.Error(), "mkdir "+cacheRoot+": not a directory") {
		t.Fatalf("expected a cache directory error, got %v", err)
	}
}

func TestInstallDispatcherV2CLIReusesConcurrentlyInstalledVersion(t *testing.T) {
	// Verifies an install that loses the rename race to a concurrent matching install reuses that install.
	cacheRoot := t.TempDir()
	installPath := dispatcherV2InstallPath(cacheRoot, "2.2.0")
	deps := dispatcherV2InstallDeps{
		runCommand: func(_ context.Context, _ string, args []string, _ io.Writer) error {
			writeInstalledDispatcherV2Package(t, args[2], "2.2.0")
			writeInstalledDispatcherV2Package(t, installPath, "2.2.0")
			writeDispatcherTestFile(t, filepath.Join(installPath, "marker"), "concurrent")
			return nil
		},
	}

	actualPath, err := installDispatcherV2CLI(context.Background(), cacheRoot, "2.2.0", "darwin", io.Discard, deps)

	if err != nil || actualPath != installPath {
		t.Fatalf("unexpected result: path=%q err=%v", actualPath, err)
	}
	assertFileContent(t, filepath.Join(installPath, "marker"), "concurrent")
}

func TestInstallDispatcherV2CLIReplacesBrokenExistingInstall(t *testing.T) {
	// Verifies a stale install directory without the requested version is replaced by the fresh npm install.
	cacheRoot := t.TempDir()
	installPath := dispatcherV2InstallPath(cacheRoot, "2.2.0")
	writeInstalledDispatcherV2Package(t, installPath, "2.1.0")
	deps := dispatcherV2InstallDeps{
		runCommand: func(_ context.Context, _ string, args []string, _ io.Writer) error {
			writeInstalledDispatcherV2Package(t, args[2], "2.2.0")
			return nil
		},
	}

	actualPath, err := installDispatcherV2CLI(context.Background(), cacheRoot, "2.2.0", "darwin", io.Discard, deps)

	if err != nil || actualPath != installPath {
		t.Fatalf("unexpected result: path=%q err=%v", actualPath, err)
	}
	if !isInstalledDispatcherV2CLI(installPath, "2.2.0") {
		t.Fatal("the stale install must be replaced by the requested version")
	}
}

func TestInstallDispatcherV2CLIReportsUnremovableBrokenInstall(t *testing.T) {
	// Verifies a stale install directory that cannot be removed aborts the install instead of mixing generations.
	if runtime.GOOS == "windows" {
		t.Skip("POSIX directory permissions are required to make removal fail.")
	}
	if os.Geteuid() == 0 {
		t.Skip("root ignores directory permissions.")
	}
	cacheRoot := t.TempDir()
	installPath := dispatcherV2InstallPath(cacheRoot, "2.2.0")
	lockedDirectory := filepath.Join(installPath, "locked")
	writeDispatcherTestFile(t, filepath.Join(lockedDirectory, "file"), "x")
	if err := os.Chmod(lockedDirectory, 0o555); err != nil {
		t.Fatalf("failed to lock directory: %v", err)
	}
	t.Cleanup(func() {
		_ = os.Chmod(lockedDirectory, 0o755)
	})
	deps := dispatcherV2InstallDeps{
		runCommand: func(_ context.Context, _ string, args []string, _ io.Writer) error {
			writeInstalledDispatcherV2Package(t, args[2], "2.2.0")
			return nil
		},
	}

	_, err := installDispatcherV2CLI(context.Background(), cacheRoot, "2.2.0", "darwin", io.Discard, deps)
	if err == nil || !strings.Contains(err.Error(), "unlinkat "+filepath.Join(lockedDirectory, "file")) {
		t.Fatalf("expected the stale install removal failure, got %v", err)
	}
	assertFileContent(t, filepath.Join(lockedDirectory, "file"), "x")
}

func TestRunDispatcherV2InstallCommandSendsAllOutputToStderr(t *testing.T) {
	// Verifies npm stdout and stderr both go to the dispatcher stderr so the delegated command's stdout stays clean.
	scriptPath := writeDispatcherTestScript(t, `echo "out:$*"; echo "err" >&2`)

	var stderr bytes.Buffer
	err := runDispatcherV2InstallCommand(context.Background(), scriptPath, []string{"install", "pkg"}, &stderr)
	if err != nil {
		t.Fatalf("runDispatcherV2InstallCommand failed: %v", err)
	}
	if stderr.String() != "out:install pkg\nerr\n" {
		t.Fatalf("stderr mismatch: %q", stderr.String())
	}
}
