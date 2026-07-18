package dispatcher

import (
	"fmt"
	"os"
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
