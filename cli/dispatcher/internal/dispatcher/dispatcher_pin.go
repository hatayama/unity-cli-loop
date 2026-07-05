package dispatcher

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"strings"
)

var dispatcherProjectRunnerVersionPattern = regexp.MustCompile(`^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?(?:\+[0-9A-Za-z][0-9A-Za-z.-]*)?$`)

type dispatcherPin struct {
	ProjectRunnerVersion     string `json:"projectRunnerVersion"`
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

	if invalidPackagePinError != nil {
		return dispatcherPin{}, invalidPackagePinError
	}
	return dispatcherPin{}, fmt.Errorf("project runner pin not found under %s", projectRoot)
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

func readDispatcherPin(pinPath string) (dispatcherPin, error) {
	content, err := os.ReadFile(pinPath)
	if err != nil {
		return dispatcherPin{}, err
	}

	pin := dispatcherPin{}
	if err := json.Unmarshal(content, &pin); err != nil {
		return dispatcherPin{}, fmt.Errorf("failed to parse %s: %w", pinPath, err)
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
	pin.SourcePath = pinPath
	return pin, nil
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
