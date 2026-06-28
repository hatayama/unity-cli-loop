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
	dispatcherMinimumProjectRunnerVersionPattern = regexp.MustCompile(`MINIMUM_REQUIRED_PROJECT_RUNNER_VERSION\s*=\s*"([^"]+)"`)
	dispatcherLegacyMinimumCliVersionPattern     = regexp.MustCompile(`MINIMUM_REQUIRED_CLI_VERSION\s*=\s*"([^"]+)"`)
	dispatcherMinimumVersionPattern              = regexp.MustCompile(`MINIMUM_REQUIRED_DISPATCHER_VERSION\s*=\s*"([^"]+)"`)
	dispatcherRequiredProtocolVersionPattern     = regexp.MustCompile(`REQUIRED_CLI_PROTOCOL_VERSION\s*=\s*(\d+)`)
	dispatcherProjectRunnerVersionPattern        = regexp.MustCompile(`^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?(?:\+[0-9A-Za-z][0-9A-Za-z.-]*)?$`)
)

type dispatcherPin struct {
	SchemaVersion            int    `json:"schemaVersion"`
	PackageName              string `json:"packageName"`
	PackageVersion           string `json:"packageVersion"`
	ProjectRunnerVersion     string `json:"projectRunnerVersion"`
	LegacyCliVersion         string `json:"cliVersion"`
	RequiredProtocolVersion  int    `json:"requiredProtocolVersion"`
	MinimumDispatcherVersion string `json:"minimumDispatcherVersion"`
	LegacyRelease            bool   `json:"-"`
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
	return dispatcherPin{}, fmt.Errorf("project runner pin not found under %s", projectRoot)
}

func dispatcherPinCandidatePaths(projectRoot string) []dispatcherPinCandidatePath {
	paths := []dispatcherPinCandidatePath{
		{Path: filepath.Join(projectRoot, dispatcherProjectPinRelativePath), Required: true},
		{Path: filepath.Join(projectRoot, dispatcherLegacyProjectPinRelativePath), Required: true},
		{Path: filepath.Join(projectRoot, "Packages", "src", dispatcherPackagePinFileName)},
		{Path: filepath.Join(projectRoot, "Packages", "src", dispatcherLegacyPackagePinFileName)},
		{Path: filepath.Join(projectRoot, "Packages", dispatcherUnityPackageName, dispatcherPackagePinFileName)},
		{Path: filepath.Join(projectRoot, "Packages", dispatcherUnityPackageName, dispatcherLegacyPackagePinFileName)},
	}
	for _, packagePinFileName := range []string{dispatcherPackagePinFileName, dispatcherLegacyPackagePinFileName} {
		packageCachePattern := filepath.Join(
			projectRoot,
			"Library",
			"PackageCache",
			dispatcherUnityPackageName+"@*",
			packagePinFileName)
		matches, err := filepath.Glob(packageCachePattern)
		if err == nil {
			for _, match := range matches {
				paths = append(paths, dispatcherPinCandidatePath{Path: match})
			}
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
	if strings.TrimSpace(pin.ProjectRunnerVersion) == "" && strings.TrimSpace(pin.LegacyCliVersion) != "" {
		pin.ProjectRunnerVersion = pin.LegacyCliVersion
		pin.LegacyRelease = true
	}
	pin.ProjectRunnerVersion = normalizeDispatcherVersion(pin.ProjectRunnerVersion)
	if pin.ProjectRunnerVersion == "" {
		return dispatcherPin{}, fmt.Errorf("%s does not define projectRunnerVersion", pinPath)
	}
	if err := validateDispatcherProjectRunnerVersion(pin.ProjectRunnerVersion); err != nil {
		return dispatcherPin{}, fmt.Errorf("%s defines invalid projectRunnerVersion: %w", pinPath, err)
	}
	pin.MinimumDispatcherVersion = normalizeDispatcherVersion(pin.MinimumDispatcherVersion)
	if pin.MinimumDispatcherVersion != "" {
		if err := validateDispatcherProjectRunnerVersion(pin.MinimumDispatcherVersion); err != nil {
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
	versionMatch, legacyRelease := dispatcherMinimumProjectRunnerVersionMatch(text)
	if len(versionMatch) != 2 {
		return dispatcherPin{}, fmt.Errorf("%s does not define MINIMUM_REQUIRED_PROJECT_RUNNER_VERSION", constantsPath)
	}
	projectRunnerVersion := normalizeDispatcherVersion(versionMatch[1])
	if err := validateDispatcherProjectRunnerVersion(projectRunnerVersion); err != nil {
		return dispatcherPin{}, fmt.Errorf("%s defines invalid MINIMUM_REQUIRED_PROJECT_RUNNER_VERSION: %w", constantsPath, err)
	}
	dispatcherVersionMatch := dispatcherMinimumVersionPattern.FindStringSubmatch(text)
	if len(dispatcherVersionMatch) != 2 {
		return dispatcherPin{}, fmt.Errorf("%s does not define MINIMUM_REQUIRED_DISPATCHER_VERSION", constantsPath)
	}
	minimumDispatcherVersion := normalizeDispatcherVersion(dispatcherVersionMatch[1])
	if err := validateDispatcherProjectRunnerVersion(minimumDispatcherVersion); err != nil {
		return dispatcherPin{}, fmt.Errorf("%s defines invalid MINIMUM_REQUIRED_DISPATCHER_VERSION: %w", constantsPath, err)
	}
	protocolVersion := 0
	protocolMatch := dispatcherRequiredProtocolVersionPattern.FindStringSubmatch(text)
	if len(protocolMatch) == 2 {
		_, _ = fmt.Sscanf(protocolMatch[1], "%d", &protocolVersion)
	}

	return dispatcherPin{
		SchemaVersion:            1,
		PackageName:              dispatcherUnityPackageName,
		ProjectRunnerVersion:     projectRunnerVersion,
		RequiredProtocolVersion:  protocolVersion,
		MinimumDispatcherVersion: minimumDispatcherVersion,
		LegacyRelease:            legacyRelease,
		SourcePath:               constantsPath,
	}, nil
}

func dispatcherMinimumProjectRunnerVersionMatch(text string) ([]string, bool) {
	versionMatch := dispatcherMinimumProjectRunnerVersionPattern.FindStringSubmatch(text)
	if len(versionMatch) == 2 {
		return versionMatch, false
	}
	return dispatcherLegacyMinimumCliVersionPattern.FindStringSubmatch(text), true
}

func normalizeDispatcherVersion(value string) string {
	trimmed := strings.TrimSpace(value)
	if strings.HasPrefix(trimmed, "v") || strings.HasPrefix(trimmed, "V") {
		return trimmed[1:]
	}
	return trimmed
}

func validateDispatcherProjectRunnerVersion(projectRunnerVersion string) error {
	if !dispatcherProjectRunnerVersionPattern.MatchString(projectRunnerVersion) {
		return fmt.Errorf("expected semantic version, got %q", projectRunnerVersion)
	}
	return nil
}
