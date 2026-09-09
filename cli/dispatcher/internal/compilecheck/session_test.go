package compilecheck

import (
	"path/filepath"
	"testing"
)

// newDagProject lays out a project whose Bee artifacts hold the named dag directories.
func newDagProject(t *testing.T, dagNames ...string) string {
	t.Helper()

	projectRoot := t.TempDir()
	for _, name := range dagNames {
		makeDirAt(t, filepath.Join(
			projectRoot, libraryDirectoryName, beeDirectoryName, beeArtifactsDirectoryName, name))
	}

	return projectRoot
}

// writeTundraLog writes a Bee log whose first object names the dag file of the last build.
func writeTundraLog(t *testing.T, projectRoot string, dagName string) {
	t.Helper()

	writeFileAt(t,
		filepath.Join(projectRoot, libraryDirectoryName, beeDirectoryName, tundraLogFileName),
		`{"dagFile":"Library/Bee/`+dagName+`","other":1}`+"\n{\"msg\":\"later line\"}\n")
}

// writeScriptDebugSetting writes the project setting that says whether debug scripting is on.
func writeScriptDebugSetting(t *testing.T, projectRoot string, enabled bool) {
	t.Helper()

	value := "false"
	if enabled {
		value = "true"
	}
	writeFileAt(t,
		filepath.Join(projectRoot, libraryDirectoryName, editorScriptingSettingsFile),
		`{"m_ScriptDebugInfoEnabled":{"m_Value":`+value+`}}`)
}

// Verifies the Bee log decides the dag even when several dag directories exist.
func TestResolveActiveDagDirPrefersTheDagNamedByTheBeeLog(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag", "aaaaDbg.dag")
	writeTundraLog(t, projectRoot, "aaaaDbg.dag")
	// The setting disagrees with the log, so only a resolver that reads the log passes.
	writeScriptDebugSetting(t, projectRoot, false)

	dagDir, err := ResolveActiveDagDir(projectRoot)
	if err != nil {
		t.Fatalf("expected the dag to resolve, got error: %v", err)
	}

	want := filepath.Join(libraryDirectoryName, beeDirectoryName, beeArtifactsDirectoryName, "aaaaDbg.dag")
	if dagDir != want {
		t.Errorf("expected %q, got %q", want, dagDir)
	}
}

// Verifies a log naming a dag Bee never wrote is ignored rather than trusted.
func TestResolveActiveDagDirIgnoresABeeLogNamingAMissingDag(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag", "aaaaDbg.dag")
	writeTundraLog(t, projectRoot, "gone.dag")
	writeScriptDebugSetting(t, projectRoot, true)

	dagDir, err := ResolveActiveDagDir(projectRoot)
	if err != nil {
		t.Fatalf("expected the dag to resolve, got error: %v", err)
	}

	if filepath.Base(dagDir) != "aaaaDbg.dag" {
		t.Errorf("expected the setting to decide, got %q", dagDir)
	}
}

// Verifies a single dag directory is used even with no Bee log at all.
func TestResolveActiveDagDirUsesTheOnlyDagWithoutABeeLog(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag")

	dagDir, err := ResolveActiveDagDir(projectRoot)
	if err != nil {
		t.Fatalf("expected the dag to resolve, got error: %v", err)
	}

	if filepath.Base(dagDir) != "aaaa.dag" {
		t.Errorf("expected the only dag, got %q", dagDir)
	}
}

// Verifies the script-debug setting picks the release dag when it is off and no log exists.
func TestResolveActiveDagDirFallsBackToTheScriptDebugSetting(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag", "aaaaDbg.dag")
	writeScriptDebugSetting(t, projectRoot, false)

	dagDir, err := ResolveActiveDagDir(projectRoot)
	if err != nil {
		t.Fatalf("expected the dag to resolve, got error: %v", err)
	}

	if filepath.Base(dagDir) != "aaaa.dag" {
		t.Errorf("expected the release dag, got %q", dagDir)
	}
}

// Verifies an ambiguous project with no log and no setting fails instead of guessing.
func TestResolveActiveDagDirFailsWhenNothingDistinguishesTheDags(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag", "aaaaDbg.dag")

	if _, err := ResolveActiveDagDir(projectRoot); err == nil {
		t.Fatal("expected an ambiguous project to fail")
	}
}

// Verifies a project Unity has never built reports that instead of an empty dag.
func TestResolveActiveDagDirFailsWhenNoArtifactsExist(t *testing.T) {
	if _, err := ResolveActiveDagDir(t.TempDir()); err == nil {
		t.Fatal("expected a project without Bee artifacts to fail")
	}
}
