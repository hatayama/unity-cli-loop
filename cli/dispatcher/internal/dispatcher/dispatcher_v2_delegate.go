package dispatcher

import (
	"encoding/json"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
)

const dispatcherNodeCommandName = "node"

type dispatcherV2CLIPackageJSON struct {
	Bin json.RawMessage `json:"bin"`
}

// resolveDispatcherV2CLIEntrypoint resolves the JavaScript file declared by the installed V2 CLI package.
// Why: executing the package entrypoint directly avoids platform-dependent node_modules/.bin shims.
func resolveDispatcherV2CLIEntrypoint(installPath string) (string, error) {
	packageDirectory := filepath.Join(installPath, "node_modules", dispatcherV2CLIPackageName)
	packagePath := filepath.Join(packageDirectory, dispatcherPackageJSONFileName)
	content, err := os.ReadFile(packagePath)
	if err != nil {
		return "", err
	}
	packageInfo := dispatcherV2CLIPackageJSON{}
	if err := json.Unmarshal(content, &packageInfo); err != nil {
		return "", fmt.Errorf("parse %s: %w", packagePath, err)
	}
	entrypoint, err := dispatcherV2BinEntrypoint(packageInfo.Bin)
	if err != nil {
		return "", err
	}
	if filepath.IsAbs(entrypoint) {
		return "", fmt.Errorf("%s bin entrypoint must be relative", dispatcherV2CLIPackageName)
	}
	return filepath.Join(packageDirectory, entrypoint), nil
}

func dispatcherV2BinEntrypoint(bin json.RawMessage) (string, error) {
	entrypoint := ""
	if err := json.Unmarshal(bin, &entrypoint); err == nil {
		return entrypoint, nil
	}
	entries := map[string]string{}
	if err := json.Unmarshal(bin, &entries); err != nil {
		return "", fmt.Errorf("%s package bin must be a string or object: %w", dispatcherV2CLIPackageName, err)
	}
	entrypoint, found := entries["uloop"]
	if !found || entrypoint == "" {
		return "", fmt.Errorf("%s package bin does not define uloop", dispatcherV2CLIPackageName)
	}
	return entrypoint, nil
}

func resolveDispatcherV2Node(lookPath func(string) (string, error)) (string, error) {
	return lookPath(dispatcherNodeCommandName)
}

func defaultDispatcherV2NodePath() (string, error) {
	return resolveDispatcherV2Node(exec.LookPath)
}
