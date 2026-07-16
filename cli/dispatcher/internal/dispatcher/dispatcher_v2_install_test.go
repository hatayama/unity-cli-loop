package dispatcher

import (
	"context"
	"io"
	"os"
	"path/filepath"
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
