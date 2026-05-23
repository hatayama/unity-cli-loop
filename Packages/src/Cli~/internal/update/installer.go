package update

import "strings"

const (
	PosixScriptName   = "install.sh"
	WindowsScriptName = "install.ps1"
	LatestStable      = "latest"
	LatestBeta        = "latest-beta"

	repositoryRawBaseURL = "https://raw.githubusercontent.com/hatayama/unity-cli-loop"
	releaseTagPrefix     = "cli-v"
	betaVersionMarker    = "-beta."
)

func ScriptURL(version string, scriptName string) string {
	return repositoryRawBaseURL + "/" + ReleaseTag(version) + "/scripts/" + scriptName
}

func ReleaseTag(version string) string {
	if strings.HasPrefix(version, releaseTagPrefix) || strings.HasPrefix(version, strings.ToUpper(releaseTagPrefix)) {
		return version
	}

	return releaseTagPrefix + version
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
