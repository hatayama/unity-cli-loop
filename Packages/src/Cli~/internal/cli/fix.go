package cli

import (
	"io"
	"os"
	"path/filepath"
)

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
	return cleanupServerStateFiles(projectRoot)
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
