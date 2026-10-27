package unityprocess

import (
	"os"
	"path/filepath"
	"runtime"
)

const (
	// TempDirectoryName is the project directory Unity keeps its per-session files in.
	TempDirectoryName = "Temp"
	// LockfileName is the file Unity holds while it has the project open and removes on a normal
	// shutdown.
	LockfileName = "UnityLockfile"
)

// LockfilePath returns where Unity keeps the lockfile for the project at projectRoot.
func LockfilePath(projectRoot string) string {
	return filepath.Join(projectRoot, TempDirectoryName, LockfileName)
}

// DefaultEditorLogPath returns where Unity writes its Editor log when it was not started with -logFile,
// or an empty string when the platform's base directory cannot be resolved.
func DefaultEditorLogPath() string {
	homeDir, err := os.UserHomeDir()
	if err != nil {
		homeDir = ""
	}
	return editorLogPathFor(runtime.GOOS, homeDir, os.Getenv("LOCALAPPDATA"))
}

// editorLogPathFor takes the platform and its base directories as parameters so every platform's
// layout can be checked on any host.
func editorLogPathFor(goos string, homeDir string, localAppData string) string {
	switch goos {
	case "darwin":
		return joinUnderBase(homeDir, "Library", "Logs", "Unity", "Editor.log")
	case "windows":
		return joinUnderBase(localAppData, "Unity", "Editor", "Editor.log")
	case "linux":
		return joinUnderBase(homeDir, ".config", "unity3d", "Editor.log")
	default:
		return ""
	}
}

// Why refuse an empty base: filepath.Join would turn it into a path relative to the working directory,
// which points at a file that does not exist.
func joinUnderBase(base string, elements ...string) string {
	if base == "" {
		return ""
	}
	return filepath.Join(append([]string{base}, elements...)...)
}
