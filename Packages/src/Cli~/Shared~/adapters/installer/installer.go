package installer

import "strings"

const (
	PosixScriptName   = "install.sh"
	WindowsScriptName = "install.ps1"

	repositoryRawBaseURL = "https://raw.githubusercontent.com/hatayama/unity-cli-loop"
	stableSourceRef      = "main"
	betaSourceRef        = "v3-beta"
	releaseTagPrefix     = "v"
	betaVersionMarker    = "-beta."
)

func ScriptURL(version string, scriptName string) string {
	return repositoryRawBaseURL + "/" + SourceRefForVersion(version) + "/scripts/" + scriptName
}

func ReleaseTag(version string) string {
	if strings.HasPrefix(version, releaseTagPrefix) || strings.HasPrefix(version, strings.ToUpper(releaseTagPrefix)) {
		return version
	}

	return releaseTagPrefix + version
}

func SourceRefForVersion(version string) string {
	if strings.Contains(strings.ToLower(version), betaVersionMarker) {
		return betaSourceRef
	}

	return stableSourceRef
}
