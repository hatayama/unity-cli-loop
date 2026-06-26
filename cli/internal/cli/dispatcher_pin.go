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
	dispatcherCLIVersionPattern              = regexp.MustCompile(`^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?(?:\+[0-9A-Za-z][0-9A-Za-z.-]*)?$`)
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

type dispatcherPinCandidatePath struct {
	Path     string
	Required bool
}

func loadDispatcherPin(projectRoot string) (dispatcherPin, error) {
	var invalidPackagePinError error
	for _, candidate := range dispatcherPinCandidatePaths(projectRoot) {
		pin, err := readDispatcherPin(candidate.Path)
		if err == nil {
			return pin, nil
		}
		if errors.Is(err, os.ErrNotExist) {
			continue
		}
		if candidate.Required {
			return dispatcherPin{}, err
		}
		// Why: source package paths can contain stale pins during upgrades; PackageCache may still hold the resolved package pin.
		if invalidPackagePinError == nil {
			invalidPackagePinError = err
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

	if invalidPackagePinError != nil {
		return dispatcherPin{}, invalidPackagePinError
	}
	return dispatcherPin{}, fmt.Errorf("cli pin not found under %s", projectRoot)
}

func dispatcherPinCandidatePaths(projectRoot string) []dispatcherPinCandidatePath {
	paths := []dispatcherPinCandidatePath{
		{Path: filepath.Join(projectRoot, dispatcherProjectPinRelativePath), Required: true},
		{Path: filepath.Join(projectRoot, "Packages", "src", dispatcherPackagePinFileName)},
		{Path: filepath.Join(projectRoot, "Packages", dispatcherUnityPackageName, dispatcherPackagePinFileName)},
	}
	packageCachePattern := filepath.Join(
		projectRoot,
		"Library",
		"PackageCache",
		dispatcherUnityPackageName+"@*",
		dispatcherPackagePinFileName)
	matches, err := filepath.Glob(packageCachePattern)
	if err == nil {
		for _, match := range matches {
			paths = append(paths, dispatcherPinCandidatePath{Path: match})
		}
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
	pin.CLIVersion = normalizeDispatcherVersion(pin.CLIVersion)
	if pin.CLIVersion == "" {
		return dispatcherPin{}, fmt.Errorf("%s does not define cliVersion", pinPath)
	}
	if err := validateDispatcherCLIVersion(pin.CLIVersion); err != nil {
		return dispatcherPin{}, fmt.Errorf("%s defines invalid cliVersion: %w", pinPath, err)
	}
	pin.MinimumDispatcherVersion = normalizeDispatcherVersion(pin.MinimumDispatcherVersion)
	if pin.MinimumDispatcherVersion != "" {
		if err := validateDispatcherCLIVersion(pin.MinimumDispatcherVersion); err != nil {
			return dispatcherPin{}, fmt.Errorf("%s defines invalid minimumDispatcherVersion: %w", pinPath, err)
		}
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
	cliVersion := normalizeDispatcherVersion(versionMatch[1])
	if err := validateDispatcherCLIVersion(cliVersion); err != nil {
		return dispatcherPin{}, fmt.Errorf("%s defines invalid MINIMUM_REQUIRED_CLI_VERSION: %w", constantsPath, err)
	}
	protocolVersion := 0
	protocolMatch := dispatcherRequiredProtocolVersionPattern.FindStringSubmatch(text)
	if len(protocolMatch) == 2 {
		_, _ = fmt.Sscanf(protocolMatch[1], "%d", &protocolVersion)
	}

	return dispatcherPin{
		SchemaVersion:            1,
		PackageName:              dispatcherUnityPackageName,
		CLIVersion:               cliVersion,
		RequiredProtocolVersion:  protocolVersion,
		MinimumDispatcherVersion: cliVersion,
		SourcePath:               constantsPath,
	}, nil
}

func normalizeDispatcherVersion(value string) string {
	trimmed := strings.TrimSpace(value)
	if strings.HasPrefix(trimmed, "v") || strings.HasPrefix(trimmed, "V") {
		return trimmed[1:]
	}
	return trimmed
}

func validateDispatcherCLIVersion(cliVersion string) error {
	if !dispatcherCLIVersionPattern.MatchString(cliVersion) {
		return fmt.Errorf("expected semantic version, got %q", cliVersion)
	}
	return nil
}
