package unityprocess

import (
	"errors"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"strings"
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

const (
	previousEditorLogName = "Editor-prev.log"
	// editorLogProjectPathMarker starts the line an Editor writes near the top of its log once it has
	// opened a project.
	editorLogProjectPathMarker = "Successfully changed project path to: "
	// editorLogHeadBytes bounds how much of a log is read to find its project. The marker line follows the
	// command line arguments within the first few kilobytes, and a log can grow to many megabytes.
	editorLogHeadBytes = 64 * 1024
)

// EditorLogPathFor returns the log the Editor of the project at projectRoot wrote at the default location,
// or an empty string when no log there is that Editor's.
// Why either log can miss: every Editor start renames Editor.log to Editor-prev.log, so the log moves once
// any other Editor starts and is gone after a second start, and -logFile writes it elsewhere.
func EditorLogPathFor(projectRoot string) string {
	return projectEditorLogPath(DefaultEditorLogPath(), projectRoot)
}

func projectEditorLogPath(editorLogPath string, projectRoot string) string {
	if editorLogPath == "" {
		return ""
	}
	previousLogPath := filepath.Join(filepath.Dir(editorLogPath), previousEditorLogName)
	for _, candidate := range []string{editorLogPath, previousLogPath} {
		if editorLogBelongsTo(candidate, projectRoot) {
			return candidate
		}
	}
	return ""
}

func editorLogBelongsTo(logPath string, projectRoot string) bool {
	file, err := os.Open(logPath)
	if err != nil {
		return false
	}
	defer func() { _ = file.Close() }()

	head := make([]byte, editorLogHeadBytes)
	readBytes, err := io.ReadFull(file, head)
	if err != nil && !errors.Is(err, io.ErrUnexpectedEOF) && !errors.Is(err, io.EOF) {
		return false
	}
	loggedProjectPath := loggedEditorProjectPath(string(head[:readBytes]))
	return loggedProjectPath != "" && isProjectPath(loggedProjectPath, projectRoot)
}

// loggedEditorProjectPath returns the project path a log head records, or an empty string when it records
// none.
func loggedEditorProjectPath(head string) string {
	markerIndex := strings.Index(head, editorLogProjectPathMarker)
	if markerIndex < 0 {
		return ""
	}
	line := head[markerIndex+len(editorLogProjectPathMarker):]
	if lineEnd := strings.IndexByte(line, '\n'); lineEnd >= 0 {
		line = line[:lineEnd]
	}
	// Why TrimSpace: a log written on Windows ends the line with "\r\n".
	return strings.TrimSpace(line)
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
