package cli

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"strings"
)

var (
	dispatcherMinimumCliVersionPattern       = regexp.MustCompile(`MINIMUM_REQUIRED_CLI_VERSION\s*=\s*"([^"]+)"`)
	dispatcherRequiredProtocolVersionPattern = regexp.MustCompile(`REQUIRED_CLI_PROTOCOL_VERSION\s*=\s*(\d+)`)
)

type dispatcherPin struct {
	SchemaVersion            int    `json:"schemaVersion"`
	PackageName              string `json:"packageName"`
	PackageVersion           string `json:"packageVersion"`
	CLIVersion               string `json:"cliVersion"`
	RequiredProtocolVersion  int    `json:"requiredProtocolVersion"`
	MinimumDispatcherVersion string `json:"minimumDispatcherVersion"`
	SourcePath               string `json:"-"`
}

func loadDispatcherPin(projectRoot string) (dispatcherPin, error) {
	for _, pinPath := range dispatcherPinCandidatePaths(projectRoot) {
		pin, err := readDispatcherPin(pinPath)
		if err == nil {
			return pin, nil
		}
		if !errors.Is(err, os.ErrNotExist) {
			return dispatcherPin{}, err
		}
	}

	for _, constantsPath := range dispatcherCliConstantsCandidatePaths(projectRoot) {
		pin, err := readDispatcherPinFromCliConstants(constantsPath)
		if err == nil {
			return pin, nil
		}
		if !errors.Is(err, os.ErrNotExist) {
			return dispatcherPin{}, err
		}
	}

	return dispatcherPin{}, fmt.Errorf("cli pin not found under %s", projectRoot)
}

func dispatcherPinCandidatePaths(projectRoot string) []string {
	paths := []string{
		filepath.Join(projectRoot, dispatcherProjectPinRelativePath),
		filepath.Join(projectRoot, "Packages", "src", dispatcherPackagePinFileName),
		filepath.Join(projectRoot, "Packages", dispatcherUnityPackageName, dispatcherPackagePinFileName),
	}
	packageCachePattern := filepath.Join(
		projectRoot,
		"Library",
		"PackageCache",
		dispatcherUnityPackageName+"@*",
		dispatcherPackagePinFileName)
	matches, err := filepath.Glob(packageCachePattern)
	if err == nil {
		paths = append(paths, matches...)
	}
	return paths
}

func dispatcherCliConstantsCandidatePaths(projectRoot string) []string {
	paths := []string{
		filepath.Join(projectRoot, "Packages", "src", "Editor", "Domain", "CliConstants.cs"),
		filepath.Join(projectRoot, "Packages", dispatcherUnityPackageName, "Editor", "Domain", "CliConstants.cs"),
	}
	packageCachePattern := filepath.Join(
		projectRoot,
		"Library",
		"PackageCache",
		dispatcherUnityPackageName+"@*",
		"Editor",
		"Domain",
		"CliConstants.cs")
	matches, err := filepath.Glob(packageCachePattern)
	if err == nil {
		paths = append(paths, matches...)
	}
	return paths
}

func readDispatcherPin(pinPath string) (dispatcherPin, error) {
	content, err := os.ReadFile(pinPath)
	if err != nil {
		return dispatcherPin{}, err
	}

	pin := dispatcherPin{}
	if err := json.Unmarshal(content, &pin); err != nil {
		return dispatcherPin{}, fmt.Errorf("failed to parse %s: %w", pinPath, err)
	}
	if strings.TrimSpace(pin.CLIVersion) == "" {
		return dispatcherPin{}, fmt.Errorf("%s does not define cliVersion", pinPath)
	}
	if pin.SchemaVersion == 0 {
		return dispatcherPin{}, fmt.Errorf("%s does not define schemaVersion", pinPath)
	}
	pin.SourcePath = pinPath
	return pin, nil
}

func readDispatcherPinFromCliConstants(constantsPath string) (dispatcherPin, error) {
	content, err := os.ReadFile(constantsPath)
	if err != nil {
		return dispatcherPin{}, err
	}
	text := string(content)
	versionMatch := dispatcherMinimumCliVersionPattern.FindStringSubmatch(text)
	if len(versionMatch) != 2 {
		return dispatcherPin{}, fmt.Errorf("%s does not define MINIMUM_REQUIRED_CLI_VERSION", constantsPath)
	}
	protocolVersion := 0
	protocolMatch := dispatcherRequiredProtocolVersionPattern.FindStringSubmatch(text)
	if len(protocolMatch) == 2 {
		_, _ = fmt.Sscanf(protocolMatch[1], "%d", &protocolVersion)
	}

	return dispatcherPin{
		SchemaVersion:            1,
		PackageName:              dispatcherUnityPackageName,
		CLIVersion:               versionMatch[1],
		RequiredProtocolVersion:  protocolVersion,
		MinimumDispatcherVersion: versionMatch[1],
		SourcePath:               constantsPath,
	}, nil
}
