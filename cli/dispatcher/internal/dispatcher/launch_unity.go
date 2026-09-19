package dispatcher

import (
	"fmt"
	"os"
	"path"
	"path/filepath"
	"strings"
)

func windowsUnityExecutableCandidates(version string) []string {
	candidates := []string{}
	for _, base := range []string{os.Getenv("ProgramFiles"), os.Getenv("ProgramFiles(x86)"), os.Getenv("LOCALAPPDATA"), `C:\Program Files`} {
		if base != "" {
			candidates = append(candidates, filepath.Join(base, "Unity", "Hub", "Editor", version, "Editor", "Unity.exe"))
		}
	}
	return candidates
}

// linuxUnityExecutableCandidates lists where Unity Hub installs an Editor on Linux.
// An unknown home directory yields no candidate rather than a relative path that
// would be resolved against the caller's working directory. path.Join keeps the
// '/'-separated result identical when the tests run on Windows.
func linuxUnityExecutableCandidates(version string, homeDir string) []string {
	if homeDir == "" {
		return []string{}
	}
	return []string{path.Join(homeDir, "Unity", "Hub", "Editor", version, "Editor", "Unity")}
}

func readUnityEditorVersion(projectRoot string) (string, error) {
	content, err := os.ReadFile(filepath.Join(projectRoot, projectVersionFilePath))
	if err != nil {
		return "", err
	}
	matches := editorVersionPattern.FindStringSubmatch(string(content))
	if len(matches) != 2 {
		return "", fmt.Errorf("unity editor version not found in %s", projectVersionFilePath)
	}
	version := strings.TrimSpace(matches[1])
	if version == "" {
		return "", fmt.Errorf("unity editor version is empty in %s", projectVersionFilePath)
	}
	return version, nil
}
