package dispatcher

import (
	"errors"
	"fmt"
	"io/fs"
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

// prepareLaunchLogFile makes sure the Editor can write logFile, creating it and its missing directories.
// Why before the running Editor is stopped: stopping it cannot be undone, and a log path found to be unusable
// afterwards would leave the project with no Editor at all.
func prepareLaunchLogFile(logFile string) error {
	if logFile == "" {
		return nil
	}
	info, err := os.Stat(logFile)
	if err == nil {
		return checkExistingLaunchLogFile(logFile, info)
	}
	if !errors.Is(err, fs.ErrNotExist) {
		return unusableLaunchLogFileError(logFile, err.Error())
	}
	if err := os.MkdirAll(filepath.Dir(logFile), 0o755); err != nil {
		return unusableLaunchLogFileError(logFile, err.Error())
	}
	file, err := os.OpenFile(logFile, os.O_WRONLY|os.O_CREATE, 0o644)
	if err != nil {
		return unusableLaunchLogFileError(logFile, err.Error())
	}
	if err := file.Close(); err != nil {
		return unusableLaunchLogFileError(logFile, err.Error())
	}
	return nil
}

// Why read the mode instead of opening the file: on Windows the Editor being replaced can still hold it, so an
// open could refuse a path Unity can write once that Editor has exited.
func checkExistingLaunchLogFile(logFile string, info fs.FileInfo) error {
	if info.IsDir() {
		return unusableLaunchLogFileError(logFile, "the path is a directory")
	}
	if info.Mode().Perm()&0o200 == 0 {
		return unusableLaunchLogFileError(logFile, "the file is read-only")
	}
	return nil
}

func unusableLaunchLogFileError(logFile string, reason string) error {
	return &clierrors.ArgumentError{
		Message:     fmt.Sprintf("%s cannot be used: %s", launchLogFileOption, reason),
		Option:      launchLogFileOption,
		Received:    logFile,
		Command:     clicore.LaunchCommandName,
		NextActions: []string{fmt.Sprintf("Pass a file path to `%s` that can be written.", launchLogFileOption)},
	}
}

func launchLogFileRequiresRestartError(logFile string) error {
	return &clierrors.ArgumentError{
		Message:  launchLogFileOption + " requires --restart when Unity is already running for this project.",
		Option:   launchLogFileOption,
		Received: logFile,
		Command:  clicore.LaunchCommandName,
		NextActions: []string{
			fmt.Sprintf("Run `uloop launch --restart %s \"%s\"` to relaunch Unity with the requested log.", launchLogFileOption, logFile),
		},
	}
}

// launchLogFileNeedsProcessScanError is for an Editor found only through the project IPC. It does not suggest a
// plain restart, because --restart needs the same process scan that just failed.
func launchLogFileNeedsProcessScanError(logFile string, scanError error) error {
	return &clierrors.ArgumentError{
		Message:  fmt.Sprintf("%s needs a new Editor, but launch can neither stop nor start one when the process scan fails: %v", launchLogFileOption, scanError),
		Option:   launchLogFileOption,
		Received: logFile,
		Command:  clicore.LaunchCommandName,
		NextActions: []string{
			fmt.Sprintf("Run `uloop launch --restart %s \"%s\"` where the process list can be read, for example outside the sandbox.", launchLogFileOption, logFile),
		},
	}
}
