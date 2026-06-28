package update

import "strings"

const (
	PosixScriptName   = "install.sh"
	WindowsScriptName = "install.ps1"
	LatestStable      = "latest"
	LatestBeta        = "latest-beta"

	repositoryRawBaseURL          = "https://raw.githubusercontent.com/hatayama/unity-cli-loop"
	projectRunnerReleaseTagPrefix = "uloop-project-runner-v"
	legacyCliReleaseTagPrefix     = "cli-v"
	dispatcherTagPrefix           = "dispatcher-v"
	betaVersionMarker             = "-beta."
)

func ScriptURL(version string, scriptName string) string {
	return repositoryRawBaseURL + "/" + DispatcherReleaseTag(version) + "/scripts/" + scriptName
}

func ProjectRunnerReleaseTag(version string) string {
	if strings.HasPrefix(version, projectRunnerReleaseTagPrefix) || strings.HasPrefix(version, strings.ToUpper(projectRunnerReleaseTagPrefix)) {
		return version
	}

	return projectRunnerReleaseTagPrefix + version
}

func DispatcherReleaseTag(version string) string {
	if strings.HasPrefix(version, dispatcherTagPrefix) || strings.HasPrefix(version, strings.ToUpper(dispatcherTagPrefix)) {
		return version
	}

	return dispatcherTagPrefix + version
}

func UpdateSelectorForVersion(version string) string {
	if IsBetaVersion(version) {
		return LatestBeta
	}

	return LatestStable
}

func IsBetaVersion(version string) bool {
	return strings.Contains(strings.ToLower(version), betaVersionMarker)
}
