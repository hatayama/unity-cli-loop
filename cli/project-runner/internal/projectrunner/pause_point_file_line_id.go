package projectrunner

import (
	"strconv"
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

// pausePointQueryTarget records the file:line form before it is converted into the marker id
// understood by Unity.
type pausePointQueryTarget struct {
	file    string
	line    int
	hasFile bool
	hasLine bool
}

// composePausePointFileLineID matches Unity's source-location marker id convention.
func composePausePointFileLineID(file string, line int) string {
	return strings.ReplaceAll(file, "\\", "/") + ":" + strconv.Itoa(line)
}

// setPausePointQueryTargetLine parses the source line before marker-id construction so leading
// zeroes cannot create a different id from the integer line Unity received at enable time.
func setPausePointQueryTargetLine(target *pausePointQueryTarget, value string) error {
	line, err := strconv.Atoi(value)
	if err != nil || line <= 0 {
		return clierrors.InvalidValueArgumentError("--"+PausePointLineFlagName, value, "positive integer")
	}
	target.line = line
	target.hasLine = true
	return nil
}

// resolvePausePointQueryID validates the id and file:line target forms before selecting the id
// sent to Unity. Keeping the check shared prevents status and await from accepting different forms.
func resolvePausePointQueryID(
	id string,
	idProvided bool,
	target pausePointQueryTarget,
	command string,
) (string, error) {
	if idProvided && (target.hasFile || target.hasLine) {
		return "", &clierrors.ArgumentError{
			Message: "--id cannot be combined with --file or --line.",
			Option:  "--" + PausePointIDFlagName,
			Command: command,
		}
	}
	if target.hasFile && !target.hasLine {
		return "", &clierrors.ArgumentError{
			Message: "--file requires --line.",
			Option:  "--" + PausePointFileFlagName,
			Command: command,
		}
	}
	if target.hasLine && !target.hasFile {
		return "", &clierrors.ArgumentError{
			Message: "--line requires --file.",
			Option:  "--" + PausePointLineFlagName,
			Command: command,
		}
	}
	if target.hasFile && target.hasLine {
		return composePausePointFileLineID(target.file, target.line), nil
	}
	if id != "" {
		return id, nil
	}
	return "", missingPausePointQueryTargetError(command)
}

// missingPausePointQueryTargetError preserves the established missing-id wire shape while naming
// the alternate file:line form that can now identify the same marker.
func missingPausePointQueryTargetError(command string) *clierrors.ArgumentError {
	return &clierrors.ArgumentError{
		Message:      "Missing required option: --id",
		Option:       "--" + PausePointIDFlagName,
		ExpectedType: "value",
		Command:      command,
		NextActions:  []string{pausePointMissingIDNextAction},
	}
}
