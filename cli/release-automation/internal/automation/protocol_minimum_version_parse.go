package automation

import (
	"encoding/json"
	"fmt"
	"regexp"
	"strconv"

	sharedversion "github.com/hatayama/unity-cli-loop/common/version"
)

// Why: REQUIRED_CLI_PROTOCOL_VERSION is the enduring IPC contract number and stays in CliConstants.cs,
// so the guard keeps a regex probe for it. The minimum project-runner version now lives in the pin JSON.
var requiredProtocolVersionPattern = regexp.MustCompile(`REQUIRED_CLI_PROTOCOL_VERSION\s*=\s*(\d+)`)

type unityPackageCliPinProjectRunnerVersion struct {
	ProjectRunnerVersion string `json:"projectRunnerVersion"`
}

// ParseProtocolMinimumVersionValues extracts the required protocol version from the CliConstants.cs
// source text and the minimum project-runner version from the package pin JSON.
func ParseProtocolMinimumVersionValues(constantsContent []byte, pinContent []byte) (ProtocolMinimumVersionValues, error) {
	pinVersion, err := parseMinimumProjectRunnerVersionFromPin(pinContent)
	if err != nil {
		return ProtocolMinimumVersionValues{}, err
	}

	values := ProtocolMinimumVersionValues{
		MinimumProjectRunnerVersion: pinVersion,
	}

	requiredMatches := requiredProtocolVersionPattern.FindStringSubmatch(string(constantsContent))
	if len(requiredMatches) == 2 {
		requiredProtocolVersion, err := strconv.Atoi(requiredMatches[1])
		if err != nil {
			return ProtocolMinimumVersionValues{}, fmt.Errorf("REQUIRED_CLI_PROTOCOL_VERSION is not an integer: %w", err)
		}
		values.RequiredProtocolVersion = requiredProtocolVersion
		values.HasRequiredProtocol = true
	}
	if !sharedversion.IsValid(pinVersion) {
		return ProtocolMinimumVersionValues{}, fmt.Errorf("%s projectRunnerVersion must be semver, got %q", unityPackageCliPinFile, pinVersion)
	}
	return values, nil
}

func parseMinimumProjectRunnerVersionFromPin(pinContent []byte) (string, error) {
	pin := unityPackageCliPinProjectRunnerVersion{}
	if err := json.Unmarshal(pinContent, &pin); err != nil {
		return "", fmt.Errorf("%s is invalid JSON: %w", unityPackageCliPinFile, err)
	}
	if pin.ProjectRunnerVersion == "" {
		return "", fmt.Errorf("%s does not define projectRunnerVersion", unityPackageCliPinFile)
	}
	return normalizeProjectRunnerVersion(pin.ProjectRunnerVersion), nil
}
