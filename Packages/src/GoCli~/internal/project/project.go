package project

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"sort"
	"strings"
)

const (
	settingsRelativePath = "UserSettings/UnityMcpSettings.json"
	ipcEndpointPrefix    = "uLoopMCP"
	ipcHashLength        = 16
	unixSocketDir        = "/tmp/uloop"
	windowsPipePrefix    = `\\.\pipe\uloop`
)

var excludedProjectSearchDirs = map[string]bool{
	".git":         true,
	"Build":        true,
	"Builds":       true,
	"Library":      true,
	"Logs":         true,
	"Temp":         true,
	"node_modules": true,
	"obj":          true,
}

type Endpoint struct {
	Network string
	Address string
}

type RequestMetadata struct {
	ExpectedProjectRoot     string `json:"expectedProjectRoot"`
	ExpectedServerSessionID string `json:"expectedServerSessionId"`
}

type Connection struct {
	Endpoint        Endpoint
	ProjectRoot     string
	RequestMetadata *RequestMetadata
}

type unitySettings struct {
	IsServerRunning bool   `json:"isServerRunning"`
	ProjectRootPath string `json:"projectRootPath"`
	ServerSessionID string `json:"serverSessionId"`
}

func ResolveConnection(startPath string, explicitProjectPath string) (Connection, error) {
	projectRoot, err := resolveProjectRoot(startPath, explicitProjectPath)
	if err != nil {
		return Connection{}, err
	}

	canonicalProjectRoot, err := filepath.EvalSymlinks(projectRoot)
	if err != nil {
		return Connection{}, err
	}
	canonicalProjectRoot = trimTrailingSeparators(canonicalProjectRoot)

	settings, err := readSettings(canonicalProjectRoot)
	if err != nil {
		return Connection{}, err
	}

	return Connection{
		Endpoint:        CreateEndpoint(canonicalProjectRoot),
		ProjectRoot:     canonicalProjectRoot,
		RequestMetadata: createRequestMetadata(settings, canonicalProjectRoot),
	}, nil
}

func CreateEndpoint(canonicalProjectRoot string) Endpoint {
	endpointName := createEndpointName(canonicalProjectRoot)
	if runtime.GOOS == "windows" {
		return Endpoint{
			Network: "pipe",
			Address: fmt.Sprintf(`%s-%s`, windowsPipePrefix, endpointName),
		}
	}

	return Endpoint{
		Network: "unix",
		Address: filepath.Join(unixSocketDir, endpointName+".sock"),
	}
}

func FindProjectRoot(startPath string) (string, error) {
	currentPath, err := filepath.Abs(startPath)
	if err != nil {
		return "", err
	}

	for {
		if isUnityProjectWithUloop(currentPath) {
			return currentPath, nil
		}

		if exists(filepath.Join(currentPath, ".git")) {
			return "", fmt.Errorf("unity project not found. Use --project-path option to specify the target")
		}

		parentPath := filepath.Dir(currentPath)
		if parentPath == currentPath {
			return "", fmt.Errorf("unity project not found. Use --project-path option to specify the target")
		}
		currentPath = parentPath
	}
}

func FindUnityProjectRoot(startPath string) (string, error) {
	currentPath, err := filepath.Abs(startPath)
	if err != nil {
		return "", err
	}

	return findUnityProjectRootInParents(currentPath)
}

func FindUnityProjectRootWithin(startPath string, maxDepth int) (string, error) {
	currentPath, err := filepath.Abs(startPath)
	if err != nil {
		return "", err
	}

	if IsUnityProject(currentPath) {
		return currentPath, nil
	}

	childProjects := findUnityProjectsInChildren(currentPath, maxDepth)
	if len(childProjects) > 0 {
		return childProjects[0], nil
	}

	return findUnityProjectRootInParents(currentPath)
}

func findUnityProjectRootInParents(currentPath string) (string, error) {
	for {
		if IsUnityProject(currentPath) {
			return currentPath, nil
		}

		if exists(filepath.Join(currentPath, ".git")) {
			return "", fmt.Errorf("unity project not found. Use --project-path option to specify the target")
		}

		parentPath := filepath.Dir(currentPath)
		if parentPath == currentPath {
			return "", fmt.Errorf("unity project not found. Use --project-path option to specify the target")
		}
		currentPath = parentPath
	}
}

func findUnityProjectsInChildren(startPath string, maxDepth int) []string {
	projects := []string{}

	var scan func(string, int)
	scan = func(currentPath string, depth int) {
		if maxDepth >= 0 && depth > maxDepth {
			return
		}
		if IsUnityProject(currentPath) {
			projects = append(projects, currentPath)
			return
		}

		entries, err := os.ReadDir(currentPath)
		if err != nil {
			return
		}

		for _, entry := range entries {
			if !entry.IsDir() || excludedProjectSearchDirs[entry.Name()] {
				continue
			}
			scan(filepath.Join(currentPath, entry.Name()), depth+1)
		}
	}

	scan(startPath, 0)
	sort.Strings(projects)
	return projects
}

func IsUnityProject(projectPath string) bool {
	return exists(filepath.Join(projectPath, "Assets")) && exists(filepath.Join(projectPath, "ProjectSettings"))
}

func resolveProjectRoot(startPath string, explicitProjectPath string) (string, error) {
	if explicitProjectPath == "" {
		return FindProjectRoot(startPath)
	}

	projectRoot, err := filepath.Abs(explicitProjectPath)
	if err != nil {
		return "", err
	}
	if !IsUnityProject(projectRoot) {
		return "", fmt.Errorf("not a Unity project: %s", projectRoot)
	}
	if !hasUloopInstalled(projectRoot) {
		return "", fmt.Errorf("the Unity CLI Loop is not installed in this project: %s", projectRoot)
	}

	return projectRoot, nil
}

func readSettings(projectRoot string) (unitySettings, error) {
	paths := []string{
		filepath.Join(projectRoot, settingsRelativePath),
		filepath.Join(projectRoot, settingsRelativePath+".tmp"),
		filepath.Join(projectRoot, settingsRelativePath+".bak"),
	}

	for _, path := range paths {
		content, err := os.ReadFile(path)
		if err != nil {
			continue
		}

		var settings unitySettings
		if json.Unmarshal(content, &settings) == nil {
			return settings, nil
		}
	}

	return unitySettings{}, fmt.Errorf("could not read Unity server session from settings")
}

func createRequestMetadata(settings unitySettings, projectRoot string) *RequestMetadata {
	if settings.ProjectRootPath == "" || settings.ServerSessionID == "" {
		return nil
	}

	settingsProjectRoot := trimTrailingSeparators(settings.ProjectRootPath)
	if settingsProjectRoot != projectRoot {
		return nil
	}

	return &RequestMetadata{
		ExpectedProjectRoot:     projectRoot,
		ExpectedServerSessionID: settings.ServerSessionID,
	}
}

func createEndpointName(canonicalProjectRoot string) string {
	sum := sha256.Sum256([]byte(canonicalProjectRoot))
	hash := hex.EncodeToString(sum[:])[:ipcHashLength]
	return ipcEndpointPrefix + "-" + hash
}

func isUnityProjectWithUloop(projectPath string) bool {
	return IsUnityProject(projectPath) && hasUloopInstalled(projectPath)
}

func hasUloopInstalled(projectPath string) bool {
	return exists(filepath.Join(projectPath, settingsRelativePath)) ||
		exists(filepath.Join(projectPath, settingsRelativePath+".tmp")) ||
		exists(filepath.Join(projectPath, settingsRelativePath+".bak"))
}

func exists(path string) bool {
	_, err := os.Stat(path)
	return err == nil
}

func trimTrailingSeparators(path string) string {
	if len(path) >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/') {
		return strings.TrimRight(path, `\/`) + `\`
	}

	trimmed := strings.TrimRight(path, `\/`)
	if trimmed != "" {
		return trimmed
	}
	if strings.HasPrefix(path, "/") {
		return "/"
	}
	return path
}
