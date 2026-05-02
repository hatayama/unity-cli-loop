package cli

import (
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
	cleaned, err := cleanupStaleLockFiles(projectRoot)
	if err != nil {
		writeLine(stderr, err.Error())
		return 1
	}

	if cleaned == 0 {
		writeLine(stdout, "No lock files found.")
		return 0
	}

	writeFormat(stdout, "\nCleaned up %d lock file(s).\n", cleaned)
	return 0
}

func cleanupStaleLockFiles(projectRoot string) (int, error) {
	cleaned := 0
	tempDirectory := filepath.Join(projectRoot, "Temp")
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
