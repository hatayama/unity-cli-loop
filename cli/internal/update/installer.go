package update

import "strings"

const (
	PosixScriptName   = "install.sh"
	WindowsScriptName = "install.ps1"
	LatestStable      = "latest"
	LatestBeta        = "latest-beta"

	repositoryRawBaseURL = "https://raw.githubusercontent.com/hatayama/unity-cli-loop"
	cliReleaseTagPrefix  = "cli-v"
	dispatcherTagPrefix  = "dispatcher-v"
	betaVersionMarker    = "-beta."
)

func ScriptURL(version string, scriptName string) string {
	return repositoryRawBaseURL + "/" + DispatcherReleaseTag(version) + "/scripts/" + scriptName
}

func CLIReleaseTag(version string) string {
	if strings.HasPrefix(version, cliReleaseTagPrefix) || strings.HasPrefix(version, strings.ToUpper(cliReleaseTagPrefix)) {
		return version
	}

	return cliReleaseTagPrefix + version
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
