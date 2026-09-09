package compilecheck

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"os"
	"os/exec"
	"path/filepath"
	"sort"
	"strings"
	"time"
)

const (
	beeDirectoryName            = "Bee"
	beeArtifactsDirectoryName   = "artifacts"
	tundraLogFileName           = "tundra.log.json"
	editorScriptingSettingsFile = "EditorOnlyScriptingSettings.json"
	dagDirectorySuffix          = ".dag"
	debugDagDirectorySuffix     = "Dbg.dag"

	unitCompileTimeout = 120 * time.Second
)

// Options are the inputs of one compile-check run.
type Options struct {
	ProjectRoot          string
	EditorExecutablePath string
	All                  bool
}

// Result is everything one compile-check run produced.
type Result struct {
	DagDir       string
	Units        []UnitResult
	Skipped      int
	ChangedCount int
}

// Run compiles the assemblies that need checking and collects their diagnostics.
func Run(ctx context.Context, options Options) (Result, error) {
	dagDir, err := ResolveActiveDagDir(options.ProjectRoot)
	if err != nil {
		return Result{}, err
	}

	paths, err := ResolveEditorCompilerPaths(options.EditorExecutablePath)
	if err != nil {
		return Result{}, err
	}

	plan, err := BuildCompilePlan(options.ProjectRoot, dagDir, options.All)
	if err != nil {
		return Result{}, err
	}

	compiler := Compiler{
		Paths:       paths,
		ProjectRoot: options.ProjectRoot,
		Timeout:     unitCompileTimeout,
		Run:         defaultRun,
	}

	units := make([]UnitResult, 0, len(plan.Units))
	for _, unit := range plan.Units {
		// Why the run continues after a failure: an assembly that failed leaves its dependents
		// reading Unity's older reference assembly, which can add follow-on errors, but reporting
		// every assembly's diagnostics in one pass is the point of the command.
		result, compileErr := compiler.CompileUnit(ctx, plan, unit)
		if compileErr != nil {
			return Result{}, compileErr
		}
		units = append(units, result)
	}

	return Result{
		DagDir:       dagDir,
		Units:        units,
		Skipped:      plan.Skipped,
		ChangedCount: len(plan.Units),
	}, nil
}

// defaultRun runs one compiler invocation and reports its output and exit code.
func defaultRun(_ context.Context, cmd *exec.Cmd) (string, string, int, error) {
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	cmd.Stdout = &stdout
	cmd.Stderr = &stderr

	err := cmd.Run()
	exitCode := 0
	var exitError *exec.ExitError
	if errors.As(err, &exitError) {
		exitCode = exitError.ExitCode()
	}

	return stdout.String(), stderr.String(), exitCode, err
}

// ResolveActiveDagDir names the Bee dag directory Unity used for its most recent build.
func ResolveActiveDagDir(projectRoot string) (string, error) {
	beeDirectory := filepath.Join(projectRoot, libraryDirectoryName, beeDirectoryName)
	artifactsDirectory := filepath.Join(beeDirectory, beeArtifactsDirectoryName)

	candidates, err := dagDirectoryNames(artifactsDirectory)
	if err != nil {
		return "", err
	}

	if name, found := dagNameFromTundraLog(beeDirectory); found && containsName(candidates, name) {
		return relativeDagDir(name), nil
	}
	if len(candidates) == 1 {
		return relativeDagDir(candidates[0]), nil
	}

	name, found := dagNameFromScriptDebugSetting(projectRoot, candidates)
	if !found {
		return "", unityBuildRequired("cannot tell which Bee build to check in %s", artifactsDirectory)
	}

	return relativeDagDir(name), nil
}

// relativeDagDir spells one dag directory the way the response files inside it spell project paths.
func relativeDagDir(name string) string {
	return filepath.Join(libraryDirectoryName, beeDirectoryName, beeArtifactsDirectoryName, name)
}

// dagDirectoryNames lists the dag directories Bee has written for this project.
func dagDirectoryNames(artifactsDirectory string) ([]string, error) {
	entries, err := os.ReadDir(artifactsDirectory)
	if err != nil {
		return nil, unityBuildRequired("no Bee build artifacts found in %s", artifactsDirectory)
	}

	names := []string{}
	for _, entry := range entries {
		if !strings.HasSuffix(entry.Name(), dagDirectorySuffix) {
			continue
		}
		if directoryExists(filepath.Join(artifactsDirectory, entry.Name())) {
			names = append(names, entry.Name())
		}
	}
	if len(names) == 0 {
		return nil, unityBuildRequired("no Bee build artifacts found in %s", artifactsDirectory)
	}
	sort.Strings(names)

	return names, nil
}

// dagNameFromTundraLog reads the dag Unity opened for its last build out of the Bee log.
// Why the log rather than modification times: both dag directories are touched by unrelated Bee
// work, so their timestamps do not say which one the Editor actually built.
func dagNameFromTundraLog(beeDirectory string) (string, bool) {
	file, err := os.Open(filepath.Join(beeDirectory, tundraLogFileName))
	if err != nil {
		return "", false
	}
	defer func() { _ = file.Close() }()

	var firstLine struct {
		DagFile string `json:"dagFile"`
	}
	if err := json.NewDecoder(file).Decode(&firstLine); err != nil || firstLine.DagFile == "" {
		return "", false
	}

	return filepath.Base(filepath.FromSlash(firstLine.DagFile)), true
}

// dagNameFromScriptDebugSetting picks between the debug and release dag using the project setting.
func dagNameFromScriptDebugSetting(projectRoot string, candidates []string) (string, bool) {
	content, err := os.ReadFile(
		filepath.Join(projectRoot, libraryDirectoryName, editorScriptingSettingsFile))
	if err != nil {
		return "", false
	}

	var settings struct {
		ScriptDebugInfoEnabled struct {
			Value bool `json:"m_Value"`
		} `json:"m_ScriptDebugInfoEnabled"`
	}
	if err := json.Unmarshal(content, &settings); err != nil {
		return "", false
	}

	for _, name := range candidates {
		if strings.HasSuffix(name, debugDagDirectorySuffix) == settings.ScriptDebugInfoEnabled.Value {
			return name, true
		}
	}

	return "", false
}

// containsName reports whether a name is in a list.
func containsName(names []string, name string) bool {
	for _, candidate := range names {
		if candidate == name {
			return true
		}
	}

	return false
}
