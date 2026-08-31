package dispatcher

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"os"
	"os/exec"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

var dispatcherReadInstalledVersion = readInstalledDispatcherVersion

type dispatcherVersionPayload struct {
	DispatcherVersion string
}

func readInstalledDispatcherVersion(ctx context.Context) (string, error) {
	executablePath, err := os.Executable()
	if err != nil {
		return "", err
	}

	command := exec.CommandContext(ctx, executablePath, "--version", "--json")
	output, err := command.Output()
	if err != nil {
		return "", err
	}

	payload := dispatcherVersionPayload{}
	if err := json.Unmarshal(output, &payload); err != nil {
		return "", err
	}

	updatedVersion := normalizeDispatcherVersion(payload.DispatcherVersion)
	if updatedVersion == "" {
		return "", errors.New("updated dispatcher version is empty")
	}
	if err := validateDispatcherProjectRunnerVersion(updatedVersion); err != nil {
		return "", err
	}
	return updatedVersion, nil
}

func dispatcherInstalledVersionOrEmpty(ctx context.Context) string {
	updatedVersion, err := dispatcherReadInstalledVersion(ctx)
	if err != nil {
		return ""
	}
	return updatedVersion
}

func dispatcherVersionChanged(fromVersion string, toVersion string) bool {
	_, _, changed := normalizedDispatcherUpdateVersions(fromVersion, toVersion)
	return changed
}

func normalizedDispatcherUpdateVersions(fromVersion string, toVersion string) (string, string, bool) {
	normalizedFromVersion := normalizeDispatcherVersion(fromVersion)
	normalizedToVersion := normalizeDispatcherVersion(toVersion)
	changed := normalizedFromVersion != "" && normalizedToVersion != "" && normalizedFromVersion != normalizedToVersion
	return normalizedFromVersion, normalizedToVersion, changed
}

func writeOptionalDispatcherUpdateCompletion(stderr io.Writer, fromVersion string, toVersion string) {
	normalizedFromVersion, normalizedToVersion, changed := normalizedDispatcherUpdateVersions(fromVersion, toVersion)
	if !changed {
		return
	}
	clicore.WriteFormat(
		stderr,
		"uloop: dispatcher updated from %s to %s. Future uloop commands will use the updated dispatcher.\n",
		normalizedFromVersion,
		normalizedToVersion)
}

func writeManualDispatcherUpdateCompletion(stdout io.Writer, fromVersion string, toVersion string) {
	if toVersion == "" {
		clicore.WriteLine(stdout, "uloop dispatcher update completed.")
		return
	}
	normalizedFromVersion, normalizedToVersion, changed := normalizedDispatcherUpdateVersions(fromVersion, toVersion)
	if !changed {
		clicore.WriteLine(stdout, "uloop dispatcher is already up to date at "+normalizedToVersion+".")
		return
	}
	clicore.WriteLine(stdout, "uloop dispatcher updated from "+normalizedFromVersion+" to "+normalizedToVersion+".")
}
