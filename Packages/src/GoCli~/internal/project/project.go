package project

import (
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"sort"
	"strings"
)

const (
	ipcEndpointPrefix = "UnityCliLoop"
	ipcHashLength     = 16
	unixSocketDir     = "/tmp/uloop"
	windowsPipePrefix = `\\.\pipe\uloop`
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
	ExpectedProjectRoot string `json:"expectedProjectRoot"`
}

type Connection struct {
	Endpoint        Endpoint
	ProjectRoot     string
	RequestMetadata *RequestMetadata
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

	return Connection{
		Endpoint:        CreateEndpoint(canonicalProjectRoot),
		ProjectRoot:     canonicalProjectRoot,
		RequestMetadata: createRequestMetadata(canonicalProjectRoot),
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

	return projectRoot, nil
}

func createRequestMetadata(projectRoot string) *RequestMetadata {
	return &RequestMetadata{
		ExpectedProjectRoot: projectRoot,
	}
}

func createEndpointName(canonicalProjectRoot string) string {
	sum := sha256.Sum256([]byte(canonicalProjectRoot))
	hash := hex.EncodeToString(sum[:])[:ipcHashLength]
	return ipcEndpointPrefix + "-" + hash
}

func exists(path string) bool {
	_, err := os.Stat(path)
	return err == nil
}

func trimTrailingSeparators(path string) string {
	trimmed := strings.TrimRight(path, `\/`)
	if trimmed == "" {
		if strings.HasPrefix(path, "/") {
			return "/"
		}
		return path
	}

	volumeName := filepath.VolumeName(path)
	if volumeName != "" {
		rootPath := volumeName + string(filepath.Separator)
		trimmedRootPath := strings.TrimRight(rootPath, `\/`)
		if strings.EqualFold(trimmed, trimmedRootPath) {
			return rootPath
		}
	}

	return trimmed
}
