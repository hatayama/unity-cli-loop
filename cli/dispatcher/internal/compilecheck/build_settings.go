package compilecheck

import (
	"bufio"
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

const (
	projectSettingsRelativePath  = "ProjectSettings/ProjectSettings.asset"
	packageManifestRelativePath  = "Packages/manifest.json"
	packageLockRelativePath      = "Packages/packages-lock.json"
	assetsResponseFileRelative   = "Assets/csc.rsp"
	compilerResponseFileName     = "csc.rsp"
	scriptingDefineSymbolsKey    = "scriptingDefineSymbols:"
	scriptingDefineSymbolsIndent = 2
	scriptingDefineEntryIndent   = 4
	textYAMLHeader               = "%YAML"
	dedicatedServerDefine        = "UNITY_SERVER"
)

// defineGroupsByPlatformDefine maps the platform define Bee writes into every response file to the
// key ProjectSettings files that platform's scripting defines under, in the order they are tried.
var defineGroupsByPlatformDefine = []struct {
	define string
	group  string
}{
	{"UNITY_STANDALONE", "Standalone"},
	{"UNITY_IOS", "iPhone"},
	{"UNITY_ANDROID", "Android"},
	{"UNITY_WEBGL", "WebGL"},
	{"UNITY_TVOS", "tvOS"},
	{"UNITY_VISIONOS", "VisionOS"},
	{"UNITY_WSA", "Windows Store Apps"},
}

// buildSettings are inputs of a Unity build that the response files cannot be read back into. They
// are recorded and compared with the next run's.
type buildSettings struct {
	// DefineGroup is the ProjectSettings group of the response files' platform, "" when it cannot be
	// decided.
	DefineGroup string `json:"defineGroup"`
	// ProjectDefines are that group's scripting defines, sorted; nil when DefineGroup is "".
	ProjectDefines []string `json:"projectDefines"`
	// ResponseFileDigests maps each csc.rsp Unity may read (project-relative, slash-separated) to the
	// sha256 of its content. A file that does not exist is left out.
	ResponseFileDigests map[string]string `json:"responseFileDigests"`
	// AssemblyConstraints maps each .asmdef outside the package cache (project-relative) to the
	// sha256 of its defineConstraints and versionDefines.
	AssemblyConstraints map[string]string `json:"assemblyConstraints"`
	ManifestDigest      string            `json:"manifestDigest"`    // "" when the file does not exist
	PackageLockDigest   string            `json:"packageLockDigest"` // "" when the file does not exist
}

// readBuildSettings reads the build settings of the project as they are now.
func readBuildSettings(
	projectRoot string, graph assemblyGraph, assemblyDefinitions map[string]AssemblyDefinition,
) (buildSettings, error) {
	settings := buildSettings{
		ResponseFileDigests: map[string]string{},
		AssemblyConstraints: map[string]string{},
	}

	group, defines, err := readProjectDefines(projectRoot, defineGroupOf(graph))
	if err != nil {
		return buildSettings{}, err
	}
	settings.DefineGroup, settings.ProjectDefines = group, defines

	if err := addResponseFileDigests(settings.ResponseFileDigests, projectRoot, assemblyDefinitions); err != nil {
		return buildSettings{}, err
	}
	if err := addAssemblyConstraints(settings.AssemblyConstraints, projectRoot, assemblyDefinitions); err != nil {
		return buildSettings{}, err
	}

	manifest, err := optionalFileDigest(filepath.Join(projectRoot, filepath.FromSlash(packageManifestRelativePath)))
	if err != nil {
		return buildSettings{}, err
	}
	lock, err := optionalFileDigest(filepath.Join(projectRoot, filepath.FromSlash(packageLockRelativePath)))
	if err != nil {
		return buildSettings{}, err
	}
	settings.ManifestDigest, settings.PackageLockDigest = manifest, lock

	return settings, nil
}

// defineGroupOf decides the ProjectSettings group the response files were built for, from the
// platform define of the first response file in name order. "" means it cannot be decided.
// Why UNITY_SERVER is checked first: a Dedicated Server build carries UNITY_STANDALONE too, and
// whether Unity reads its defines from the Server key or the Standalone key cannot be confirmed.
func defineGroupOf(graph assemblyGraph) string {
	if len(graph.names) == 0 {
		return ""
	}
	defines := map[string]bool{}
	for _, define := range graph.byName[graph.names[0]].Defines {
		defines[define] = true
	}
	if defines[dedicatedServerDefine] {
		return ""
	}
	for _, candidate := range defineGroupsByPlatformDefine {
		if defines[candidate.define] {
			return candidate.group
		}
	}

	return ""
}

// readProjectDefines reads the scripting defines ProjectSettings keeps for one group, and returns
// the group it could read them for: "" with nil defines when the group is unknown or the file is
// not text.
func readProjectDefines(projectRoot string, group string) (string, []string, error) {
	if group == "" {
		return "", nil, nil
	}
	content, err := os.ReadFile(filepath.Join(projectRoot, filepath.FromSlash(projectSettingsRelativePath)))
	if errors.Is(err, fs.ErrNotExist) {
		return "", nil, nil
	}
	if err != nil {
		return "", nil, fmt.Errorf("failed to read %s: %w", projectSettingsRelativePath, err)
	}
	// Why a binary file gives up on the defines: Unity can serialize ProjectSettings in binary,
	// which this line scan cannot read, and guessing would refuse runs Unity would build.
	if !bytes.HasPrefix(content, []byte(textYAMLHeader)) {
		return "", nil, nil
	}

	value, found := scriptingDefinesValue(content, group)
	if !found {
		return group, []string{}, nil
	}

	return group, splitDefines(value), nil
}

// scriptingDefinesValue finds the value of one group's line under scriptingDefineSymbols.
func scriptingDefinesValue(content []byte, group string) (string, bool) {
	scanner := bufio.NewScanner(bytes.NewReader(content))
	scanner.Buffer(make([]byte, 0, 64*1024), len(content)+1)
	inside := false
	for scanner.Scan() {
		line := strings.TrimRight(scanner.Text(), "\r")
		indent := len(line) - len(strings.TrimLeft(line, " "))
		trimmed := strings.TrimSpace(line)
		if !inside {
			inside = indent == scriptingDefineSymbolsIndent && trimmed == scriptingDefineSymbolsKey
			continue
		}
		if trimmed == "" {
			continue
		}
		if indent < scriptingDefineEntryIndent {
			return "", false
		}
		key, value, hasValue := strings.Cut(trimmed, ":")
		if hasValue && key == group {
			return strings.TrimSpace(value), true
		}
	}

	return "", false
}

// splitDefines splits a scripting define value on every separator Unity accepts, sorted.
func splitDefines(value string) []string {
	defines := strings.FieldsFunc(value, func(character rune) bool {
		return character == ';' || character == ',' || character == ' ' || character == '\t'
	})
	sort.Strings(defines)

	return defines
}

// addResponseFileDigests records Assets/csc.rsp and the csc.rsp beside each .asmdef outside the
// package cache, by the digest of their content.
func addResponseFileDigests(
	digests map[string]string, projectRoot string, assemblyDefinitions map[string]AssemblyDefinition,
) error {
	paths := []string{filepath.Join(projectRoot, filepath.FromSlash(assetsResponseFileRelative))}
	for _, definition := range assemblyDefinitions {
		if isUnderPackageCache(projectRoot, definition.Directory) {
			continue
		}
		paths = append(paths, filepath.Join(definition.Directory, compilerResponseFileName))
	}

	for _, path := range paths {
		digest, err := optionalFileDigest(path)
		if err != nil {
			return err
		}
		if digest != "" {
			digests[recordSourcePath(projectRoot, path)] = digest
		}
	}

	return nil
}

// addAssemblyConstraints records the defineConstraints and versionDefines of each .asmdef outside
// the package cache.
// Why the package cache is left out: the package manager restores it and nobody edits it by hand,
// and a package version change already shows in packages-lock.json.
func addAssemblyConstraints(
	constraints map[string]string, projectRoot string, assemblyDefinitions map[string]AssemblyDefinition,
) error {
	for _, definition := range assemblyDefinitions {
		if isUnderPackageCache(projectRoot, definition.Directory) {
			continue
		}
		var read struct {
			DefineConstraints []string         `json:"defineConstraints"`
			VersionDefines    []map[string]any `json:"versionDefines"`
		}
		if err := readUnityJSONFile(definition.Path, &read); err != nil {
			return fmt.Errorf("failed to read %s: %w", definition.Path, err)
		}
		normalized, err := json.Marshal(read)
		if err != nil {
			return fmt.Errorf("failed to read %s: %w", definition.Path, err)
		}
		constraints[recordSourcePath(projectRoot, definition.Path)] = digestOf(normalized)
	}

	return nil
}

// optionalFileDigest is the sha256 of a file's content, or "" when the file does not exist.
func optionalFileDigest(path string) (string, error) {
	content, err := os.ReadFile(path)
	if errors.Is(err, fs.ErrNotExist) {
		return "", nil
	}
	if err != nil {
		return "", fmt.Errorf("failed to read %s: %w", path, err)
	}

	return digestOf(content), nil
}

// digestOf is the hex sha256 of some bytes.
func digestOf(content []byte) string {
	sum := sha256.Sum256(content)

	return hex.EncodeToString(sum[:])
}
