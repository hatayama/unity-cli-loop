package dispatcher

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

const (
	launchLogFileOption       = "--log-file"
	launchLogFileExpectedType = "file path"
	// unityStdoutLogFile is the value Unity's -logFile reads as "write to stdout".
	unityStdoutLogFile = "-"
)

func isLaunchLogFileOption(arg string) bool {
	return arg == launchLogFileOption || strings.HasPrefix(arg, launchLogFileOption+"=")
}

// applyLaunchLogFileOption stores the requested log as an absolute path.
// Why absolute: a relative path means the caller's directory to the caller, and this removes any question of
// how the Editor would read it.
func applyLaunchLogFileOption(options *launchOptions, args []string, index int) (int, error) {
	value, consumed, err := readLaunchOptionValue(args[index], args, index)
	if err != nil {
		return index, err
	}
	// Why refuse stdout: launch detaches the Editor, so the log would go nowhere.
	if value == unityStdoutLogFile {
		return index, clierrors.InvalidValueArgumentError(launchLogFileOption, value, launchLogFileExpectedType)
	}
	absolutePath, err := filepath.Abs(value)
	if err != nil {
		return index, clierrors.InvalidValueArgumentError(launchLogFileOption, value, launchLogFileExpectedType)
	}
	options.logFile = absolutePath
	return nextLaunchOptionIndex(index, consumed), nil
}

// launchStartsEditor reports whether this launch ends up starting an Editor rather than reusing or only
// stopping one.
func launchStartsEditor(options launchOptions, runningProcess *clicore.UnityProcess) bool {
	return !options.quit && (runningProcess == nil || options.restart)
}

// prepareLaunchLogFile makes sure the Editor can open logFile and creates its missing directories.
// Why before the running Editor is stopped: stopping it cannot be undone, and a log path found to be unusable
// afterwards would leave the project with no Editor at all.
func prepareLaunchLogFile(logFile string) error {
	if logFile == "" {
		return nil
	}
	info, err := os.Stat(logFile)
	if err == nil && info.IsDir() {
		return unusableLaunchLogFileError(logFile, "the path is a directory")
	}
	if err := os.MkdirAll(filepath.Dir(logFile), 0o755); err != nil {
		return unusableLaunchLogFileError(logFile, err.Error())
	}
	return nil
}

func unusableLaunchLogFileError(logFile string, reason string) error {
	return &clierrors.ArgumentError{
		Message:     fmt.Sprintf("%s cannot be used: %s", launchLogFileOption, reason),
		Option:      launchLogFileOption,
		Received:    logFile,
		Command:     clicore.LaunchCommandName,
		NextActions: []string{fmt.Sprintf("Pass a file path to `%s` whose directory can be created.", launchLogFileOption)},
	}
}

func launchLogFileRequiresRestartError(logFile string) error {
	return &clierrors.ArgumentError{
		Message:  launchLogFileOption + " requires --restart when Unity is already running for this project.",
		Option:   launchLogFileOption,
		Received: logFile,
		Command:  clicore.LaunchCommandName,
		NextActions: []string{
			fmt.Sprintf("Run `uloop launch --restart %s %s` to relaunch Unity with the requested log.", launchLogFileOption, logFile),
		},
	}
}
