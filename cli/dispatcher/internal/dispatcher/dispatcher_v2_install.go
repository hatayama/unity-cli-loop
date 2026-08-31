package dispatcher

import (
	"context"
	"errors"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
)

const (
	dispatcherV2CacheDirectoryName  = "v2"
	dispatcherV2CLIPackageName      = "uloop-cli"
	dispatcherNPMCommandName        = "npm"
	dispatcherNPMWindowsCommandName = "npm.cmd"
)

type dispatcherV2InstallDeps struct {
	runCommand func(context.Context, string, []string, io.Writer) error
}

func defaultDispatcherV2InstallDeps() dispatcherV2InstallDeps {
	return dispatcherV2InstallDeps{runCommand: runDispatcherV2InstallCommand}
}

// installDispatcherV2CLI installs the exact V2 CLI version into its isolated cache directory.
// Why: each Unity package version requires its matching CLI, so a shared cache slot would reinstall on every project switch.
func installDispatcherV2CLI(ctx context.Context, cacheRoot string, version string, goos string, stderr io.Writer, deps dispatcherV2InstallDeps) (string, error) {
	installPath := dispatcherV2InstallPath(cacheRoot, version)
	if isInstalledDispatcherV2CLI(installPath, version) {
		return installPath, nil
	}

	parentDirectory := filepath.Dir(installPath)
	if err := os.MkdirAll(parentDirectory, 0o755); err != nil {
		return "", err
	}
	temporaryDirectory, err := os.MkdirTemp(parentDirectory, ".install-")
	if err != nil {
		return "", err
	}
	defer func() {
		_ = os.RemoveAll(temporaryDirectory)
	}()

	args := []string{"install", "--prefix", temporaryDirectory, dispatcherV2CLIPackageName + "@" + version}
	if err := deps.runCommand(ctx, dispatcherV2NPMCommandName(goos), args, stderr); err != nil {
		return "", err
	}
	if !isInstalledDispatcherV2CLI(temporaryDirectory, version) {
		return "", fmt.Errorf("npm did not install %s@%s", dispatcherV2CLIPackageName, version)
	}
	if err := os.Rename(temporaryDirectory, installPath); err == nil {
		return installPath, nil
	}
	if isInstalledDispatcherV2CLI(installPath, version) {
		return installPath, nil
	}
	if err := os.RemoveAll(installPath); err != nil && !errors.Is(err, os.ErrNotExist) {
		return "", err
	}
	if err := os.Rename(temporaryDirectory, installPath); err != nil {
		return "", err
	}
	return installPath, nil
}

func dispatcherV2InstallPath(cacheRoot string, version string) string {
	return filepath.Join(cacheRoot, dispatcherV2CacheDirectoryName, version)
}

func isInstalledDispatcherV2CLI(installPath string, version string) bool {
	packagePath := filepath.Join(installPath, "node_modules", dispatcherV2CLIPackageName, dispatcherPackageJSONFileName)
	installedVersion, err := readDispatcherPackageVersion(packagePath)
	return err == nil && installedVersion == version
}

func dispatcherV2NPMCommandName(goos string) string {
	if goos == "windows" {
		return dispatcherNPMWindowsCommandName
	}
	return dispatcherNPMCommandName
}

func runDispatcherV2InstallCommand(ctx context.Context, name string, args []string, stderr io.Writer) error {
	command := exec.CommandContext(ctx, name, args...)
	command.Stdout = stderr
	command.Stderr = stderr
	command.Env = os.Environ()
	return command.Run()
}
