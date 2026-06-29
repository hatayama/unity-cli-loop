package automation

import (
	"fmt"
	"regexp"
	"strconv"

	sharedversion "github.com/hatayama/unity-cli-loop/cli/internal/version"
)

var (
	requiredProtocolVersionPattern              = regexp.MustCompile(`REQUIRED_CLI_PROTOCOL_VERSION\s*=\s*(\d+)`)
	minimumProjectRunnerVersionPattern          = regexp.MustCompile(`MINIMUM_REQUIRED_PROJECT_RUNNER_VERSION\s*=\s*"([^"]+)"`)
	preRenameMinimumProjectRunnerVersionPattern = regexp.MustCompile(`MINIMUM_REQUIRED_CLI_VERSION\s*=\s*"([^"]+)"`)
)

func ParseProtocolMinimumVersionValues(content []byte) (ProtocolMinimumVersionValues, error) {
	text := string(content)
	values, err := parseProtocolMinimumVersionValuesWithMinimumVersion(text)
	if err != nil {
		return ProtocolMinimumVersionValues{}, err
	}
	return values, nil
}

func parseProtocolMinimumVersionBaseValues(content []byte) (ProtocolMinimumVersionValues, error) {
	text := string(content)
	if _, ok := parseMinimumProjectRunnerVersion(text); ok {
		return ParseProtocolMinimumVersionValues(content)
	}

	minimumMatches := preRenameMinimumProjectRunnerVersionPattern.FindStringSubmatch(text)
	if len(minimumMatches) != 2 {
		return ParseProtocolMinimumVersionValues(content)
	}
	values, err := parseProtocolMinimumVersionValues(text, normalizeProjectRunnerVersion(minimumMatches[1]), "MINIMUM_REQUIRED_CLI_VERSION")
	if err != nil {
		return ProtocolMinimumVersionValues{}, err
	}
	values.UsesPreRenameMinimumVersion = true
	return values, nil
}

func parseProtocolMinimumVersionValuesWithMinimumVersion(text string) (ProtocolMinimumVersionValues, error) {
	minimumProjectRunnerVersion, ok := parseMinimumProjectRunnerVersion(text)
	if !ok {
		return ProtocolMinimumVersionValues{}, fmt.Errorf("%s does not define MINIMUM_REQUIRED_PROJECT_RUNNER_VERSION", protocolMinimumVersionFile)
	}
	return parseProtocolMinimumVersionValues(text, minimumProjectRunnerVersion, "MINIMUM_REQUIRED_PROJECT_RUNNER_VERSION")
}

func parseMinimumProjectRunnerVersion(text string) (string, bool) {
	minimumMatches := minimumProjectRunnerVersionPattern.FindStringSubmatch(text)
	if len(minimumMatches) == 2 {
		return normalizeProjectRunnerVersion(minimumMatches[1]), true
	}
	return "", false
}

func parseProtocolMinimumVersionValues(
	text string,
	minimumProjectRunnerVersion string,
	minimumVersionConstantName string,
) (ProtocolMinimumVersionValues, error) {
	values := ProtocolMinimumVersionValues{
		MinimumProjectRunnerVersion: minimumProjectRunnerVersion,
	}

	requiredMatches := requiredProtocolVersionPattern.FindStringSubmatch(text)
	if len(requiredMatches) == 2 {
		requiredProtocolVersion, err := strconv.Atoi(requiredMatches[1])
		if err != nil {
			return ProtocolMinimumVersionValues{}, fmt.Errorf("REQUIRED_CLI_PROTOCOL_VERSION is not an integer: %w", err)
		}
		values.RequiredProtocolVersion = requiredProtocolVersion
		values.HasRequiredProtocol = true
	}
	if _, ok := sharedversion.Compare(minimumProjectRunnerVersion, minimumProjectRunnerVersion); !ok {
		return ProtocolMinimumVersionValues{}, fmt.Errorf("%s %s must be semver, got %q", protocolMinimumVersionFile, minimumVersionConstantName, minimumProjectRunnerVersion)
	}
	return values, nil
}
