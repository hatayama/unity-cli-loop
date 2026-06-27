package update

import (
	"errors"
	"fmt"
	"strings"

	sharedversion "github.com/hatayama/unity-cli-loop/cli/internal/version"
)

const (
	UnsupportedOSMessage = "native update is only supported on macOS and Windows"
)

type Options struct {
	CurrentVersion string
	TargetVersion  string
}

type Command struct {
	Name string
	Args []string
}

func CommandForOS(goos string, options Options) (Command, error) {
	version := ScriptVersion(options)
	updateSelector := Selector(options)
	switch goos {
	case "darwin":
		scriptURL := ScriptURL(version, PosixScriptName)
		script := fmt.Sprintf(`tmp=$(mktemp) && curl -fSL %s -o "$tmp" && ULOOP_VERSION=%s sh "$tmp"; ec=$?; rm -f "$tmp"; exit $ec`, shellQuote(scriptURL), shellQuote(updateSelector))
		return Command{Name: "sh", Args: []string{"-c", script}}, nil
	case "windows":
		scriptURL := ScriptURL(version, WindowsScriptName)
		return Command{Name: "powershell", Args: []string{
			"-NoProfile",
			"-ExecutionPolicy",
			"Bypass",
			"-Command",
			fmt.Sprintf("$env:ULOOP_VERSION=%s; irm %s | iex", shellQuote(updateSelector), shellQuote(scriptURL)),
		}}, nil
	default:
		return Command{}, errors.New(UnsupportedOSMessage)
	}
}

func IsValidTargetVersion(value string) bool {
	_, ok := sharedversion.Compare(value, value)
	return ok
}

func NormalizeTargetVersion(value string) string {
	trimmed := strings.TrimSpace(value)
	lower := strings.ToLower(trimmed)
	if strings.HasPrefix(lower, dispatcherTagPrefix) {
		return trimmed[len(dispatcherTagPrefix):]
	}
	if strings.HasPrefix(lower, cliReleaseTagPrefix) {
		return trimmed[len(cliReleaseTagPrefix):]
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

func shellQuote(value string) string {
	return "'" + strings.ReplaceAll(value, "'", "'\"'\"'") + "'"
}
