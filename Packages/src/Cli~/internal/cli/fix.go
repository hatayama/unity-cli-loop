package cli

import (
	"fmt"
	"io"
	"os"
	"path/filepath"
)

var staleLockFileNames = []string{
	"compiling.lock",
	"domainreload.lock",
	"serverstarting.lock",
}

// TODO: Extend fix to remove only project-owned stale IPC sockets after proving the listener is dead.
func runFix(projectRoot string, stdout io.Writer, stderr io.Writer) int {
	cleaned, err := cleanupStaleRecoveryState(projectRoot)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: "fix"})
		return 1
	}

	if cleaned == 0 {
		writeLine(stdout, "No recovery state files found.")
		return 0
	}

	writeFormat(stdout, "\nCleaned up %d recovery state file(s).\n", cleaned)
	return 0
}

func cleanupStaleRecoveryState(projectRoot string) (int, error) {
	cleaned, err := cleanupServerStateFiles(projectRoot)
	if err != nil {
		return cleaned, err
	}

	lockCleaned, err := cleanupStaleLockFiles(projectRoot)
	if err != nil {
		return cleaned, err
	}
	return cleaned + lockCleaned, nil
}

func cleanupServerStateFiles(projectRoot string) (int, error) {
	cleaned := 0
	statePath := filepath.Join(projectRoot, serverStateRelativePath)
	for _, path := range []string{
		statePath,
		statePath + serverStateCompletedTempSuffix,
		statePath + serverStateInProgressTempSuffix,
		statePath + serverStateBackupSuffix,
	} {
		if _, err := os.Stat(path); err != nil {
			if !os.IsNotExist(err) {
				return cleaned, err
			}
			continue
		}
		if err := os.Remove(path); err != nil {
			return cleaned, err
		}
		cleaned++
	}
	return cleaned, nil
}

func cleanupStaleLockFiles(projectRoot string) (int, error) {
	cleaned := 0
	tempDirectory := filepath.Join(projectRoot, "Temp")
	tempInfo, err := os.Stat(tempDirectory)
	if err != nil {
		if os.IsNotExist(err) {
			return cleaned, nil
		}
		return cleaned, err
	}
	if !tempInfo.IsDir() {
		return cleaned, fmt.Errorf("temp path is not a directory: %s", tempDirectory)
	}

	for _, lockFileName := range staleLockFileNames {
		lockFilePath := filepath.Join(tempDirectory, lockFileName)
		if _, err := os.Stat(lockFilePath); err != nil {
			if !os.IsNotExist(err) {
				return cleaned, err
			}
			continue
		}
		if err := os.Remove(lockFilePath); err != nil {
			return cleaned, err
		}
		cleaned++
	}
	return cleaned, nil
}
