package update

import (
	"errors"
	"strings"

	sharedversion "github.com/hatayama/unity-cli-loop/common/version"
)

const (
	UnsupportedOSMessage = "native update is only supported on macOS and Windows"
)

type Options struct {
	CurrentVersion string
	TargetVersion  string
}

type Command struct {
	Name                 string
	Args                 []string
	Env                  []string
	InstallerName        string
	InstallerURL         string
	InstallerChecksumURL string
	// ReleaseTag is the dispatcher release tag the InstallerURL resolves to
	// (e.g. "dispatcher-v3.0.1-beta.12"). The attestation verifier resolves
	// this to a commit SHA and binds it to the certificate's Source Repository
	// Digest extension so a stolen OIDC token cannot be reused on an unrelated
	// tag.
	ReleaseTag string
}

func CommandForOS(goos string, options Options) (Command, error) {
	version := ScriptVersion(options)
	updateSelector := Selector(options)
	switch goos {
	case "darwin":
		return commandForScript("sh", PosixScriptName, version, updateSelector), nil
	case "windows":
		return commandForScript("powershell", WindowsScriptName, version, updateSelector), nil
	default:
		return Command{}, errors.New(UnsupportedOSMessage)
	}
}

func commandForScript(name string, scriptName string, version string, updateSelector string) Command {
	installerURL := ScriptAssetURL(version, scriptName)
	return Command{
		Name:                 name,
		Env:                  []string{"ULOOP_VERSION=" + updateSelector},
		InstallerName:        scriptName,
		InstallerURL:         installerURL,
		InstallerChecksumURL: installerURL + ".sha256",
		ReleaseTag:           DispatcherReleaseTag(version),
	}
}

func IsValidTargetVersion(value string) bool {
	return sharedversion.IsValid(value)
}

func NormalizeTargetVersion(value string) string {
	trimmed := strings.TrimSpace(value)
	lower := strings.ToLower(trimmed)
	if strings.HasPrefix(lower, dispatcherTagPrefix) {
		return trimmed[len(dispatcherTagPrefix):]
	}
	if strings.HasPrefix(lower, projectRunnerReleaseTagPrefix) {
		return trimmed[len(projectRunnerReleaseTagPrefix):]
	}
	if strings.HasPrefix(lower, "v") {
		return trimmed[1:]
	}
	return trimmed
}

func ScriptVersion(options Options) string {
	if options.TargetVersion != "" {
		return NormalizeTargetVersion(options.TargetVersion)
	}
	return options.CurrentVersion
}

func Selector(options Options) string {
	if options.TargetVersion != "" {
		return DispatcherReleaseTag(NormalizeTargetVersion(options.TargetVersion))
	}
	return UpdateSelectorForVersion(options.CurrentVersion)
}
