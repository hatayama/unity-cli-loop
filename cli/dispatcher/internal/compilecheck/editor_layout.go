// Package compilecheck compiles Unity assemblies with the C# compiler bundled with a Unity
// Editor install, without launching or contacting the Editor itself.
package compilecheck

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"sort"
	"strconv"
	"strings"
)

const (
	netCoreRuntimeDirectoryName    = "NetCoreRuntime"
	dotNetSdkRoslynDirectoryName   = "DotNetSdkRoslyn"
	dotNetSdkDirectoryName         = "DotNetSdk"
	sdkDirectoryName               = "sdk"
	roslynDirectoryName            = "Roslyn"
	bincoreDirectoryName           = "bincore"
	compilerDllFileName            = "csc.dll"
	compilerRuntimeConfigFileName  = "csc.runtimeconfig.json"
	compilerDepsFileName           = "csc.deps.json"
	codeAnalysisDllFileName        = "Microsoft.CodeAnalysis.dll"
	codeAnalysisCSharpDllFileName  = "Microsoft.CodeAnalysis.CSharp.dll"
	sharedDirectoryName            = "shared"
	sharedFrameworkName            = "Microsoft.NETCore.App"
	scriptingRootScanDepthLimit    = 4
	macOSExecutableDirectoryName   = "MacOS"
	editorContentsDirectoryName    = "Contents"
	windowsEditorDataDirectoryName = "Data"
)

// EditorCompilerPaths locates the csc and dotnet host bundled with one Unity Editor install.
type EditorCompilerPaths struct {
	DotnetHostPath      string // .../NetCoreRuntime/dotnet or .../DotNetSdk/dotnet (dotnet.exe on Windows)
	CompilerDllPath     string // .../csc.dll
	CompilerDirectory   string
	SharedFrameworkPath string // .../shared/Microsoft.NETCore.App/<version>
}

// ResolveEditorCompilerPaths finds the bundled compiler that belongs to the given Editor executable.
func ResolveEditorCompilerPaths(editorExecutablePath string) (EditorCompilerPaths, error) {
	contentsPath, err := resolveEditorContentsPath(editorExecutablePath)
	if err != nil {
		return EditorCompilerPaths{}, err
	}

	scriptingRootPath := resolveScriptingRootPath(contentsPath)
	if scriptingRootPath == "" {
		return EditorCompilerPaths{}, fmt.Errorf(
			"no Unity-bundled C# compiler layout found under %s", contentsPath)
	}

	compilerDirectoryPath := resolveCompilerDirectoryPath(scriptingRootPath)
	if compilerDirectoryPath == "" {
		return EditorCompilerPaths{}, fmt.Errorf(
			"no Unity-bundled C# compiler layout found under %s", contentsPath)
	}

	hostPath, sharedFrameworkPath := resolveRuntimePairing(scriptingRootPath, compilerDirectoryPath)
	paths := EditorCompilerPaths{
		DotnetHostPath:      hostPath,
		CompilerDllPath:     filepath.Join(compilerDirectoryPath, compilerDllFileName),
		CompilerDirectory:   compilerDirectoryPath,
		SharedFrameworkPath: sharedFrameworkPath,
	}
	if err := verifyRequiredCompilerFiles(paths, scriptingRootPath); err != nil {
		return EditorCompilerPaths{}, err
	}

	return paths, nil
}

// verifyRequiredCompilerFiles fails when any file the compiler invocation needs is absent.
func verifyRequiredCompilerFiles(paths EditorCompilerPaths, scriptingRootPath string) error {
	if paths.SharedFrameworkPath == "" {
		return fmt.Errorf(
			"required compiler file missing: %s",
			filepath.Join(scriptingRootPath, netCoreRuntimeDirectoryName, sharedDirectoryName, sharedFrameworkName))
	}

	required := []string{
		paths.DotnetHostPath,
		paths.CompilerDllPath,
		filepath.Join(paths.CompilerDirectory, compilerRuntimeConfigFileName),
		filepath.Join(paths.CompilerDirectory, compilerDepsFileName),
		filepath.Join(paths.CompilerDirectory, codeAnalysisDllFileName),
		filepath.Join(paths.CompilerDirectory, codeAnalysisCSharpDllFileName),
	}
	for _, path := range required {
		if !fileExists(path) {
			return fmt.Errorf("required compiler file missing: %s", path)
		}
	}

	return nil
}

// resolveEditorContentsPath maps an Editor executable to the directory that holds its bundled data.
func resolveEditorContentsPath(editorExecutablePath string) (string, error) {
	if editorExecutablePath == "" {
		return "", errors.New("the Unity Editor executable path is empty")
	}

	executableDirectoryPath := filepath.Dir(editorExecutablePath)
	if filepath.Base(executableDirectoryPath) == macOSExecutableDirectoryName {
		contentsPath := filepath.Dir(executableDirectoryPath)
		if filepath.Base(contentsPath) == editorContentsDirectoryName {
			return contentsPath, nil
		}
	}

	dataDirectoryPath := filepath.Join(executableDirectoryPath, windowsEditorDataDirectoryName)
	if directoryExists(dataDirectoryPath) {
		return dataDirectoryPath, nil
	}

	return "", fmt.Errorf(
		"unrecognized Unity Editor install layout for %s", editorExecutablePath)
}

// resolveScriptingRootPath finds the directory that holds the compiler layout inside the Editor.
func resolveScriptingRootPath(contentsPath string) string {
	resourcesScriptingRootPath := filepath.Join(contentsPath, "Resources", "Scripting")
	if containsExternalCompilerLayout(resourcesScriptingRootPath) {
		return resourcesScriptingRootPath
	}

	if containsExternalCompilerLayout(contentsPath) {
		return contentsPath
	}

	return resolveScriptingRootPathByScan(contentsPath)
}

// resolveScriptingRootPathByScan walks the Editor data directory breadth-first for a compiler layout.
func resolveScriptingRootPathByScan(contentsPath string) string {
	type pendingDirectory struct {
		path  string
		depth int
	}

	pending := []pendingDirectory{{path: contentsPath, depth: 0}}
	for len(pending) > 0 {
		current := pending[0]
		pending = pending[1:]
		if containsExternalCompilerLayout(current.path) {
			return current.path
		}
		if current.depth >= scriptingRootScanDepthLimit {
			continue
		}
		for _, childPath := range childDirectoryPaths(current.path) {
			pending = append(pending, pendingDirectory{path: childPath, depth: current.depth + 1})
		}
	}

	return ""
}

// containsExternalCompilerLayout reports whether a directory holds a compiler and a runtime for it.
func containsExternalCompilerLayout(rootPath string) bool {
	compilerDirectoryPath := resolveCompilerDirectoryPath(rootPath)
	if compilerDirectoryPath == "" {
		return false
	}

	if directoryExists(filepath.Join(rootPath, netCoreRuntimeDirectoryName)) {
		return true
	}

	dotNetSdkRootPath := findDirectoryContainingSdk(compilerDirectoryPath)
	if dotNetSdkRootPath == "" {
		return false
	}

	return directoryExists(filepath.Join(dotNetSdkRootPath, sharedDirectoryName, sharedFrameworkName))
}

// resolveCompilerDirectoryPath picks the directory holding csc.dll, preferring the legacy layout.
func resolveCompilerDirectoryPath(scriptingRootPath string) string {
	if scriptingRootPath == "" {
		return ""
	}

	legacyCompilerDirectoryPath := filepath.Join(scriptingRootPath, dotNetSdkRoslynDirectoryName)
	if fileExists(filepath.Join(legacyCompilerDirectoryPath, compilerDllFileName)) {
		return legacyCompilerDirectoryPath
	}

	sdkRootPath := filepath.Join(scriptingRootPath, dotNetSdkDirectoryName, sdkDirectoryName)
	for _, sdkDirectoryPath := range sortedVersionDirectoriesDescending(sdkRootPath) {
		compilerDirectoryPath := filepath.Join(sdkDirectoryPath, roslynDirectoryName, bincoreDirectoryName)
		if directoryExists(compilerDirectoryPath) {
			return compilerDirectoryPath
		}
	}

	return ""
}

// resolveRuntimePairing chooses the dotnet host and shared framework that satisfy csc's required major.
// Why NetCoreRuntime first: the editors that ship both already satisfy the required major there, and
// switching unconditionally would change which runtime those installs compile with.
func resolveRuntimePairing(scriptingRootPath string, compilerDirectoryPath string) (string, string) {
	netCoreHostPath := filepath.Join(scriptingRootPath, netCoreRuntimeDirectoryName, hostFileName())
	netCoreSharedRootPath := filepath.Join(
		scriptingRootPath, netCoreRuntimeDirectoryName, sharedDirectoryName, sharedFrameworkName)
	netCoreSharedPath := resolveHighestSharedFrameworkPath(netCoreSharedRootPath)

	requiredMajor, hasRequiredMajor := readRequiredRuntimeMajor(compilerDirectoryPath)
	if !hasRequiredMajor || sharedRootSatisfiesRequiredMajor(netCoreSharedRootPath, requiredMajor) {
		return netCoreHostPath, netCoreSharedPath
	}

	sdkHostPath, sdkSharedPath := resolveDotNetSdkRuntimePairing(compilerDirectoryPath, requiredMajor)
	if sdkHostPath == "" {
		return netCoreHostPath, netCoreSharedPath
	}

	return sdkHostPath, sdkSharedPath
}

// resolveDotNetSdkRuntimePairing pairs the compiler with the runtime shipped inside its own SDK root.
func resolveDotNetSdkRuntimePairing(compilerDirectoryPath string, requiredMajor int) (string, string) {
	dotNetSdkRootPath := findDirectoryContainingSdk(compilerDirectoryPath)
	if dotNetSdkRootPath == "" {
		return "", ""
	}

	hostPath := filepath.Join(dotNetSdkRootPath, hostFileName())
	if !fileExists(hostPath) {
		return "", ""
	}

	sharedRootPath := filepath.Join(dotNetSdkRootPath, sharedDirectoryName, sharedFrameworkName)
	if !sharedRootSatisfiesRequiredMajor(sharedRootPath, requiredMajor) {
		return "", ""
	}

	sharedPath := resolveHighestSharedFrameworkPath(sharedRootPath)
	if sharedPath == "" {
		return "", ""
	}

	return hostPath, sharedPath
}

// readRequiredRuntimeMajor reads the major runtime version csc.runtimeconfig.json demands.
func readRequiredRuntimeMajor(compilerDirectoryPath string) (int, bool) {
	content, err := os.ReadFile(filepath.Join(compilerDirectoryPath, compilerRuntimeConfigFileName))
	if err != nil {
		return 0, false
	}

	var config struct {
		RuntimeOptions struct {
			Framework struct {
				Version string `json:"version"`
			} `json:"framework"`
		} `json:"runtimeOptions"`
	}
	if err := json.Unmarshal(content, &config); err != nil {
		return 0, false
	}

	parts, ok := parseVersionParts(config.RuntimeOptions.Framework.Version)
	if !ok {
		return 0, false
	}

	return parts[0], true
}

// sharedRootSatisfiesRequiredMajor reports whether any installed framework meets the required major.
func sharedRootSatisfiesRequiredMajor(sharedRootPath string, requiredMajor int) bool {
	for _, directoryPath := range childDirectoryPaths(sharedRootPath) {
		parts, ok := parseVersionParts(filepath.Base(directoryPath))
		if ok && parts[0] >= requiredMajor {
			return true
		}
	}

	return false
}

// resolveHighestSharedFrameworkPath picks the newest installed shared framework directory.
func resolveHighestSharedFrameworkPath(sharedRootPath string) string {
	directoryPaths := sortedVersionDirectoriesDescending(sharedRootPath)
	if len(directoryPaths) == 0 {
		return ""
	}

	return directoryPaths[0]
}

// sortedVersionDirectoriesDescending orders child directories newest first, version names before others.
func sortedVersionDirectoriesDescending(rootPath string) []string {
	directoryPaths := childDirectoryPaths(rootPath)
	sort.SliceStable(directoryPaths, func(left int, right int) bool {
		return compareVersionDirectoryNames(
			filepath.Base(directoryPaths[left]), filepath.Base(directoryPaths[right])) < 0
	})

	return directoryPaths
}

// compareVersionDirectoryNames orders two directory names newest first for version-shaped names.
func compareVersionDirectoryNames(leftName string, rightName string) int {
	leftParts, leftIsVersion := parseVersionParts(leftName)
	rightParts, rightIsVersion := parseVersionParts(rightName)
	switch {
	case leftIsVersion && rightIsVersion:
		if comparison := compareVersionParts(rightParts, leftParts); comparison != 0 {
			return comparison
		}
	case leftIsVersion:
		return -1
	case rightIsVersion:
		return 1
	}

	return strings.Compare(rightName, leftName)
}

// parseVersionParts reads a dotted numeric version name, mirroring the .NET Version parser.
func parseVersionParts(name string) ([]int, bool) {
	segments := strings.Split(name, ".")
	if len(segments) < 2 || len(segments) > 4 {
		return nil, false
	}

	parts := make([]int, 0, len(segments))
	for _, segment := range segments {
		value, err := strconv.Atoi(segment)
		if err != nil || value < 0 {
			return nil, false
		}
		parts = append(parts, value)
	}

	return parts, true
}

// compareVersionParts orders two parsed versions, treating an absent component as lower.
func compareVersionParts(left []int, right []int) int {
	for index := 0; index < len(left) && index < len(right); index++ {
		if left[index] != right[index] {
			if left[index] < right[index] {
				return -1
			}
			return 1
		}
	}

	switch {
	case len(left) < len(right):
		return -1
	case len(left) > len(right):
		return 1
	default:
		return 0
	}
}

// findDirectoryContainingSdk walks up from the compiler directory to the .NET SDK root above it.
func findDirectoryContainingSdk(startDirectoryPath string) string {
	currentPath := startDirectoryPath
	for currentPath != "" {
		if directoryExists(filepath.Join(currentPath, sdkDirectoryName)) {
			return currentPath
		}
		parentPath := filepath.Dir(currentPath)
		if parentPath == currentPath {
			return ""
		}
		currentPath = parentPath
	}

	return ""
}

// childDirectoryPaths lists the immediate subdirectories of a directory in ordinal name order.
func childDirectoryPaths(rootPath string) []string {
	entries, err := os.ReadDir(rootPath)
	if err != nil {
		return nil
	}

	// Why os.Stat instead of entry.IsDir: Editor installs link some directories, and a symlinked
	// directory must be walked like a real one.
	directoryPaths := make([]string, 0, len(entries))
	for _, entry := range entries {
		childPath := filepath.Join(rootPath, entry.Name())
		if directoryExists(childPath) {
			directoryPaths = append(directoryPaths, childPath)
		}
	}
	sort.Strings(directoryPaths)

	return directoryPaths
}

// hostFileName is the dotnet host executable name for the running platform.
func hostFileName() string {
	if runtime.GOOS == "windows" {
		return "dotnet.exe"
	}

	return "dotnet"
}

func fileExists(path string) bool {
	info, err := os.Stat(path)

	return err == nil && !info.IsDir()
}

func directoryExists(path string) bool {
	info, err := os.Stat(path)

	return err == nil && info.IsDir()
}
