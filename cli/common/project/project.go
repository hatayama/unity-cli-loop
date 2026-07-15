package project

import (
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"sort"
	"strconv"
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	ipcEndpointPrefix = "UnityCliLoop"
	ipcHashLength     = 16
	unixSocketParent  = "/tmp"
	unixSocketPrefix  = "uloop-"
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

func ResolveConnection(startPath string, explicitProjectPath string) (unityipc.Connection, error) {
	return resolveConnection(startPath, explicitProjectPath)
}

func resolveConnection(
	startPath string,
	explicitProjectPath string,
) (unityipc.Connection, error) {
	projectRoot, err := resolveProjectRoot(startPath, explicitProjectPath)
	if err != nil {
		return unityipc.Connection{}, err
	}

	canonicalProjectRoot, err := filepath.EvalSymlinks(projectRoot)
	if err != nil {
		return unityipc.Connection{}, err
	}
	canonicalProjectRoot = trimTrailingSeparators(canonicalProjectRoot)

	return unityipc.Connection{
		Endpoint:    CreateEndpoint(canonicalProjectRoot),
		ProjectRoot: canonicalProjectRoot,
	}, nil
}

func CreateEndpoint(canonicalProjectRoot string) unityipc.Endpoint {
	endpointName := createEndpointName(canonicalProjectRoot)
	if runtime.GOOS == "windows" {
		return unityipc.Endpoint{
			Network: "pipe",
			Address: fmt.Sprintf(`%s-%s`, windowsPipePrefix, endpointName),
		}
	}

	return unityipc.Endpoint{
		Network: "unix",
		Address: filepath.Join(
			unixSocketParent,
			unixSocketPrefix+strconv.Itoa(os.Geteuid()),
			endpointName+".sock",
		),
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
			return "", clierrors.ProjectNotFoundError{}
		}

		parentPath := filepath.Dir(currentPath)
		if parentPath == currentPath {
			return "", clierrors.ProjectNotFoundError{}
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
	if len(childProjects) == 1 {
		return childProjects[0], nil
	}
	if len(childProjects) > 1 {
		return "", fmt.Errorf("multiple Unity projects found under %s; use --project-path to choose one", currentPath)
	}

	return findUnityProjectRootInParents(currentPath)
}

func findUnityProjectRootInParents(currentPath string) (string, error) {
	for {
		if IsUnityProject(currentPath) {
			return currentPath, nil
		}

		if exists(filepath.Join(currentPath, ".git")) {
			return "", clierrors.ProjectNotFoundError{}
		}

		parentPath := filepath.Dir(currentPath)
		if parentPath == currentPath {
			return "", clierrors.ProjectNotFoundError{}
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

	return ResolveExplicitProjectRoot(explicitProjectPath)
}

// ResolveExplicitProjectRoot resolves a user-supplied Unity project path for the current platform.
func ResolveExplicitProjectRoot(explicitProjectPath string) (string, error) {
	resolution := normalizeExplicitProjectPathForOS(explicitProjectPath, runtime.GOOS, exists)
	projectRoot, err := filepath.Abs(resolution.path)
	if err != nil {
		return "", err
	}
	if !IsUnityProject(projectRoot) {
		return "", notUnityProjectError(projectRoot, resolution.suggestion)
	}

	return projectRoot, nil
}

type explicitProjectPathResolution struct {
	path       string
	suggestion string
}

func normalizeExplicitProjectPathForOS(
	explicitProjectPath string,
	goos string,
	pathExists func(string) bool,
) explicitProjectPathResolution {
	if goos != "windows" {
		return explicitProjectPathResolution{path: explicitProjectPath}
	}

	candidate, ok := windowsPosixProjectPathCandidate(explicitProjectPath)
	if !ok {
		return explicitProjectPathResolution{path: explicitProjectPath}
	}
	if pathExists(candidate) {
		return explicitProjectPathResolution{path: candidate}
	}
	return explicitProjectPathResolution{
		path:       explicitProjectPath,
		suggestion: candidate,
	}
}

func windowsPosixProjectPathCandidate(projectPath string) (string, bool) {
	if projectPath == "" {
		return "", false
	}
	if projectPath[0] != '/' {
		return "", false
	}

	slashPath := strings.ReplaceAll(projectPath, `\`, "/")
	if len(slashPath) >= 2 && slashPath[0] == '/' && isASCIIAlpha(slashPath[1]) {
		if len(slashPath) == 2 {
			return windowsDrivePath(slashPath[1], ""), true
		}
		if slashPath[2] == '/' {
			return windowsDrivePath(slashPath[1], slashPath[3:]), true
		}
	}

	if len(slashPath) >= 6 &&
		strings.EqualFold(slashPath[:5], "/mnt/") &&
		isASCIIAlpha(slashPath[5]) {
		if len(slashPath) == 6 {
			return windowsDrivePath(slashPath[5], ""), true
		}
		if slashPath[6] == '/' {
			return windowsDrivePath(slashPath[5], slashPath[7:]), true
		}
	}

	return "", false
}

func windowsDrivePath(driveLetter byte, rest string) string {
	drive := string(toUpperASCIILetter(driveLetter)) + `:\`
	if rest == "" {
		return drive
	}
	return drive + strings.ReplaceAll(rest, "/", `\`)
}

func notUnityProjectError(projectRoot string, suggestion string) error {
	return clierrors.NotUnityProjectError{
		ProjectRoot: projectRoot,
		Suggestion:  suggestion,
	}
}

func isASCIIAlpha(value byte) bool {
	return (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z')
}

func toUpperASCIILetter(value byte) byte {
	if value >= 'a' && value <= 'z' {
		return value - ('a' - 'A')
	}
	return value
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
