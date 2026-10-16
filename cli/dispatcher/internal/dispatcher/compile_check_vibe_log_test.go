package dispatcher

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/vibelog"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/compilecheck"
)

// readCompileCheckVibeLog reads every entry today's CLI VibeLog of the project holds, in order.
func readCompileCheckVibeLog(t *testing.T, projectRoot string) []vibelog.CLIVibeLogEntry {
	t.Helper()
	path := filepath.Join(projectRoot, vibelog.CLIVibeLogDirectory,
		vibelog.CLIVibeLogPrefix+"_"+time.Now().UTC().Format("20060102")+".json")
	content, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read the CLI VibeLog: %v", err)
	}

	entries := []vibelog.CLIVibeLogEntry{}
	scanner := bufio.NewScanner(bytes.NewReader(content))
	scanner.Buffer(make([]byte, 0, 64*1024), 1024*1024)
	for scanner.Scan() {
		var entry vibelog.CLIVibeLogEntry
		if err := json.Unmarshal(scanner.Bytes(), &entry); err != nil {
			t.Fatalf("failed to parse a CLI VibeLog line %q: %v", scanner.Text(), err)
		}
		entries = append(entries, entry)
	}

	return entries
}

// assertCompileCheckVibeRun checks the log holds exactly one start and one complete of the same run,
// and returns the complete entry.
func assertCompileCheckVibeRun(t *testing.T, entries []vibelog.CLIVibeLogEntry) vibelog.CLIVibeLogEntry {
	t.Helper()
	if len(entries) != 2 {
		t.Fatalf("expected a start and a complete entry, got %+v", entries)
	}
	start, complete := entries[0], entries[1]
	if start.Operation != "cli_compile_check_start" || complete.Operation != "cli_compile_check_complete" {
		t.Fatalf("operations = %q, %q", start.Operation, complete.Operation)
	}
	if start.CorrelationID == "" || start.CorrelationID != complete.CorrelationID {
		t.Errorf("correlation ids = %q, %q, want one shared id", start.CorrelationID, complete.CorrelationID)
	}

	return complete
}

// assertVibeContextJSON checks one context field serializes to the given JSON.
func assertVibeContextJSON(t *testing.T, entry vibelog.CLIVibeLogEntry, key string, want string) {
	t.Helper()
	encoded, err := json.Marshal(entry.Context[key])
	if err != nil {
		t.Fatalf("failed to encode %s: %v", key, err)
	}
	if string(encoded) != want {
		t.Errorf("%s = %s, want %s", key, encoded, want)
	}
}

// loggedCompileCheckResult is a run that compiled, fast-checked, replayed a fast check of, reused,
// and blocked one assembly each, and settled the surface of four of them four ways - one change with
// more names than the log keeps, one no name tells - so a field the log drops or mixes up shows.
func loggedCompileCheckResult() compilecheck.Result {
	return compilecheck.Result{
		DagDir:  "Library/Bee/artifacts/1300b0aE.dag",
		Skipped: 3,
		Blocked: []string{"Delta"},
		Units: []compilecheck.UnitResult{
			{
				Assembly: "Alpha", Succeeded: true, Duration: 1500 * time.Millisecond,
				FastPathFallbackReason: "no baseline: no full compile of this assembly is on record",
				FastPathNote:           "baseline not recorded: source changed during the compile: Assets/A.cs",
				Surface: compilecheck.SurfaceVerdict{
					Kept: true, Reason: compilecheck.SurfaceSameAPI, Asked: true, Duration: 400 * time.Millisecond,
				},
			},
			{
				Assembly: "Beta", Succeeded: true, Duration: 250 * time.Millisecond, FastChecked: true,
				FastCheckChangedFiles: []string{"Assets/B1.cs", "Assets/B2.cs"},
				Surface:               compilecheck.SurfaceVerdict{Kept: true, Reason: compilecheck.SurfaceSameBytes},
			},
			{
				Assembly: "Epsilon", Succeeded: true, FastChecked: true, FastCheckReused: true,
				FastCheckChangedFiles: []string{"Assets/E.cs"},
				Surface: compilecheck.SurfaceVerdict{
					Reason: compilecheck.SurfaceChanged, Unnamed: "operator op_Addition", Asked: true, Duration: 3 * time.Millisecond,
				},
			},
			{
				Assembly: "Gamma", Succeeded: true, Reused: true,
				Surface: compilecheck.SurfaceVerdict{
					Reason: compilecheck.SurfaceChanged, Detail: "produced: a / Unity build: b", Asked: true, FromRecord: true,
					Duration:     2 * time.Millisecond,
					ChangedNames: []string{"N01", "N02", "N03", "N04", "N05", "N06", "N07", "N08", "N09", "N10", "N11", "N12"},
				},
			},
		},
	}
}

// Verifies a run writes a start and a complete entry with one correlation id, and the complete
// entry carries what the response reported, how long each compiled or checked unit took, and every
// surface the worker or its record settled, including that of a replayed unit.
func TestCompileCheckVibeLogRecordsASucceededRun(t *testing.T) {
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
	projectRoot := t.TempDir()
	result := loggedCompileCheckResult()

	logRun := startCompileCheckVibeLog(projectRoot, compileCheckOptions{all: true, jobs: 4})
	logRun.completed(buildCompileCheckResponse(result, projectRoot), result)

	entries := readCompileCheckVibeLog(t, projectRoot)
	complete := assertCompileCheckVibeRun(t, entries)
	assertVibeContextJSON(t, entries[0], "all", "true")
	assertVibeContextJSON(t, entries[0], "full", "false")
	assertVibeContextJSON(t, entries[0], "jobs", "4")
	assertVibeContextJSON(t, entries[0], "project_identity", `"`+vibelog.ProjectIdentity(projectRoot)+`"`)
	if complete.Level != "INFO" {
		t.Errorf("level = %q, want INFO", complete.Level)
	}
	assertVibeContextJSON(t, complete, "outcome", `"succeeded"`)
	assertVibeContextJSON(t, complete, "error_count", "0")
	assertVibeContextJSON(t, complete, "compiled", `["Alpha"]`)
	assertVibeContextJSON(t, complete, "fast_checked", `["Beta","Epsilon"]`)
	assertVibeContextJSON(t, complete, "reused", `["Gamma"]`)
	assertVibeContextJSON(t, complete, "blocked", `["Delta"]`)
	assertVibeContextJSON(t, complete, "skipped_count", "3")
	assertVibeContextJSON(t, complete, "fast_checks", `[{"assembly":"Beta","changed_file_count":2,"reused":false},`+
		`{"assembly":"Epsilon","changed_file_count":1,"reused":true}]`)
	assertVibeContextJSON(t, complete, "fallbacks",
		`[{"assembly":"Alpha","reason":"no baseline: no full compile of this assembly is on record"}]`)
	assertVibeContextJSON(t, complete, "baseline_notes",
		`[{"assembly":"Alpha","reason":"baseline not recorded: source changed during the compile: Assets/A.cs"}]`)
	assertVibeContextJSON(t, complete, "units",
		`[{"assembly":"Alpha","duration_ms":1500,"succeeded":true},{"assembly":"Beta","duration_ms":250,"succeeded":true}]`)
	assertVibeContextJSON(t, complete, "surface_checks",
		`[{"assembly":"Alpha","changed_name_count":0,"changed_names":[],"detail":"","duration_ms":400,"from_record":false,`+
			`"verdict":"same API"},`+
			`{"assembly":"Epsilon","changed_name_count":0,"changed_names":[],"detail":"","duration_ms":3,"from_record":false,`+
			`"unnamed":"operator op_Addition","verdict":"changed"},`+
			`{"assembly":"Gamma","changed_name_count":12,`+
			`"changed_names":["N01","N02","N03","N04","N05","N06","N07","N08","N09","N10"],`+
			`"detail":"produced: a / Unity build: b","duration_ms":2,"from_record":true,"verdict":"changed"}]`)
	if _, found := complete.Context["duration_ms"]; !found {
		t.Error("expected the run's duration to be logged")
	}
}

// Verifies a run that found compile errors is logged as a warning with the errors counted.
func TestCompileCheckVibeLogRecordsARunWithErrors(t *testing.T) {
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
	projectRoot := t.TempDir()
	result := compilecheck.Result{Units: []compilecheck.UnitResult{{
		Assembly: "Alpha", Diagnostics: []compilecheck.Diagnostic{
			{Severity: "error", Code: "CS0103", Message: "missing", File: "Assets/A.cs", Line: 1, Column: 1},
			{Severity: "error", Code: "CS0029", Message: "cannot convert", File: "Assets/A.cs", Line: 2, Column: 1},
		},
	}}}

	logRun := startCompileCheckVibeLog(projectRoot, compileCheckOptions{})
	logRun.completed(buildCompileCheckResponse(result, projectRoot), result)

	complete := assertCompileCheckVibeRun(t, readCompileCheckVibeLog(t, projectRoot))
	if complete.Level != "WARNING" {
		t.Errorf("level = %q, want WARNING", complete.Level)
	}
	assertVibeContextJSON(t, complete, "outcome", `"errors"`)
	assertVibeContextJSON(t, complete, "error_count", "2")
}

// Verifies a run that could not finish is logged as an error with what stopped it.
func TestCompileCheckVibeLogRecordsAFailedRun(t *testing.T) {
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
	projectRoot := t.TempDir()

	logRun := startCompileCheckVibeLog(projectRoot, compileCheckOptions{})
	logRun.failed(errors.New("no Bee build found"))

	complete := assertCompileCheckVibeRun(t, readCompileCheckVibeLog(t, projectRoot))
	if complete.Level != "ERROR" {
		t.Errorf("level = %q, want ERROR", complete.Level)
	}
	assertVibeContextJSON(t, complete, "outcome", `"failed"`)
	assertVibeContextJSON(t, complete, "error", `"no Bee build found"`)
	wantCode := clierrors.ClassifyError(errors.New("no Bee build found"), clierrors.ErrorContext{}).ErrorCode
	assertVibeContextJSON(t, complete, "error_code", `"`+wantCode+`"`)
}

// Verifies runCompileCheck logs the run that stopped because the project's Editor is not
// installed, as a start and a failed complete of the same run.
func TestCompileCheckVibeLogRecordsAnEditorResolutionFailure(t *testing.T) {
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
	isolateCompileCheckEditorLookup(t)
	projectRoot := createDispatcherUnityProject(t)
	writeDispatcherTestFile(t, filepath.Join(projectRoot, projectVersionFilePath),
		"m_EditorVersion: "+bogusCompileCheckEditorVersion+"\n")
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runCompileCheck(context.Background(), compileCheckOptions{projectPath: projectRoot}, projectRoot, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("code = %d, want 1", code)
	}
	complete := assertCompileCheckVibeRun(t, readCompileCheckVibeLog(t, projectRoot))
	assertVibeContextJSON(t, complete, "outcome", `"failed"`)
}

// Verifies the units entries say which references a unit assumed kept Unity's surface, which one
// made it compile again and how long the discarded compile took, and that it waited for a record,
// each only on the units it applies to.
func TestCompileCheckVibeLogRecordsUnitsCompiledAgain(t *testing.T) {
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
	projectRoot := t.TempDir()
	result := compilecheck.Result{Units: []compilecheck.UnitResult{
		{Assembly: "Alpha", Succeeded: true, Duration: 100 * time.Millisecond},
		{
			Assembly: "Beta", Succeeded: true, Duration: 200 * time.Millisecond,
			AssumedReferences: []string{"Gamma", "Delta"},
		},
		{
			Assembly: "Epsilon", Succeeded: true, Duration: 300 * time.Millisecond,
			CompiledAgainAfter: "Gamma", DiscardedDuration: 50 * time.Millisecond, WaitedForRecord: true,
		},
	}}

	logRun := startCompileCheckVibeLog(projectRoot, compileCheckOptions{})
	logRun.completed(buildCompileCheckResponse(result, projectRoot), result)

	complete := assertCompileCheckVibeRun(t, readCompileCheckVibeLog(t, projectRoot))
	assertVibeContextJSON(t, complete, "units",
		`[{"assembly":"Alpha","duration_ms":100,"succeeded":true},`+
			`{"assembly":"Beta","assumed_kept":["Gamma","Delta"],"duration_ms":200,"succeeded":true},`+
			`{"assembly":"Epsilon","compiled_again_after":"Gamma","discarded_ms":50,"duration_ms":300,`+
			`"succeeded":true,"waited_for_record":true}]`)
}

// Verifies a run that narrowed dependents by names logs which references touched each unit and by
// which name or for which reason, which units it left out by names, and what reading the names took.
func TestCompileCheckVibeLogRecordsNameNarrowing(t *testing.T) {
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
	projectRoot := t.TempDir()
	result := compilecheck.Result{
		Units: []compilecheck.UnitResult{
			{Assembly: "Alpha", Succeeded: true, Duration: 100 * time.Millisecond},
			{
				Assembly: "Beta", Succeeded: true, Duration: 200 * time.Millisecond,
				ChangedReferences: []compilecheck.ChangedReference{
					{Assembly: "Alpha", Name: "Foo"}, {Assembly: "Gamma"}, {Assembly: "Epsilon", Reason: "no saved generator output"},
				},
			},
		},
		Skipped:   1,
		NameSkips: []compilecheck.NameSkip{{Assembly: "Delta", ChangedReferences: []string{"Alpha"}}},
		NameIndex: compilecheck.NameIndexReport{
			Duration: 30 * time.Millisecond, Units: 3, WithGenerated: 1, WithoutGenerated: 1, Unreadable: 1, SourcesRead: true,
		},
	}

	logRun := startCompileCheckVibeLog(projectRoot, compileCheckOptions{})
	logRun.completed(buildCompileCheckResponse(result, projectRoot), result)

	complete := assertCompileCheckVibeRun(t, readCompileCheckVibeLog(t, projectRoot))
	assertVibeContextJSON(t, complete, "units",
		`[{"assembly":"Alpha","duration_ms":100,"succeeded":true},`+
			`{"assembly":"Beta","changed_by":[{"name":"Foo","reference":"Alpha"},{"name":"","reference":"Gamma"},`+
			`{"name":"","reason":"no saved generator output","reference":"Epsilon"}],`+
			`"duration_ms":200,"succeeded":true}]`)
	assertVibeContextJSON(t, complete, "name_skips", `[{"assembly":"Delta","unaffected_by":["Alpha"]}]`)
	assertVibeContextJSON(t, complete, "name_index",
		`{"duration_ms":30,"sources_read":true,"units":3,"unreadable":1,"with_generated":1,"without_generated":1}`)
}

// Verifies a run that read only the saved generator output of the units with references logs the
// name index and says it never read their sources.
func TestCompileCheckVibeLogRecordsANameIndexWhoseSourcesWereNotRead(t *testing.T) {
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
	projectRoot := t.TempDir()
	result := compilecheck.Result{
		Units:     []compilecheck.UnitResult{{Assembly: "Alpha", Succeeded: true, Reused: true}},
		Skipped:   2,
		NameIndex: compilecheck.NameIndexReport{Duration: 2 * time.Millisecond, Units: 2},
	}

	logRun := startCompileCheckVibeLog(projectRoot, compileCheckOptions{})
	logRun.completed(buildCompileCheckResponse(result, projectRoot), result)

	complete := assertCompileCheckVibeRun(t, readCompileCheckVibeLog(t, projectRoot))
	assertVibeContextJSON(t, complete, "name_index",
		`{"duration_ms":2,"sources_read":false,"units":2,"unreadable":0,"with_generated":0,"without_generated":0}`)
}

// Verifies a run that read no names and left nothing out by names logs neither.
func TestCompileCheckVibeLogLeavesOutNameNarrowingWhenNothingWasRead(t *testing.T) {
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
	projectRoot := t.TempDir()
	result := compilecheck.Result{Units: []compilecheck.UnitResult{{Assembly: "Alpha", Succeeded: true}}}

	logRun := startCompileCheckVibeLog(projectRoot, compileCheckOptions{})
	logRun.completed(buildCompileCheckResponse(result, projectRoot), result)

	complete := assertCompileCheckVibeRun(t, readCompileCheckVibeLog(t, projectRoot))
	for _, key := range []string{"name_skips", "name_index"} {
		if _, found := complete.Context[key]; found {
			t.Errorf("expected no %s in the log, got %v", key, complete.Context[key])
		}
	}
}

// Verifies a completed run logs whether the last Unity build left a build-trace file and what
// happened to the build-settings record.
func TestCompileCheckVibeLogRecordsTheBuildSettingsOfACompletedRun(t *testing.T) {
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
	projectRoot := t.TempDir()
	result := compilecheck.Result{
		BuildSettings: compilecheck.BuildSettingsReport{StampFound: true, Record: "new", DefinesChecked: true},
	}

	logRun := startCompileCheckVibeLog(projectRoot, compileCheckOptions{})
	logRun.completed(buildCompileCheckResponse(result, projectRoot), result)

	complete := assertCompileCheckVibeRun(t, readCompileCheckVibeLog(t, projectRoot))
	assertVibeContextJSON(t, complete, "build_settings",
		`{"defines_checked":true,"record":"new","refused_part":"","stamp_found":true}`)
}

// Verifies a run refused for a build setting that moved logs the refused setting on the failed
// complete entry, and the error still classifies as a Unity build being required.
func TestCompileCheckVibeLogRecordsTheBuildSettingOfARefusedRun(t *testing.T) {
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
	projectRoot := t.TempDir()
	refusal := compilecheck.BuildSettingsError{
		Report: compilecheck.BuildSettingsReport{StampFound: true, Record: "kept", RefusedPart: "Assets/csc.rsp"},
		Err:    compilecheck.UnityBuildRequiredError{Reason: "Assets/csc.rsp changed since the last Unity build"},
	}

	logRun := startCompileCheckVibeLog(projectRoot, compileCheckOptions{})
	logRun.failed(refusal)

	complete := assertCompileCheckVibeRun(t, readCompileCheckVibeLog(t, projectRoot))
	assertVibeContextJSON(t, complete, "build_settings",
		`{"defines_checked":false,"record":"kept","refused_part":"Assets/csc.rsp","stamp_found":true}`)
	assertVibeContextJSON(t, complete, "error_code", `"`+clierrors.ErrorCodeCompileCheckUnityBuildRequired+`"`)
}

// Verifies a run that failed before the build settings were compared logs no build_settings field.
func TestCompileCheckVibeLogLeavesOutBuildSettingsOfARunThatFailedEarlier(t *testing.T) {
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
	projectRoot := t.TempDir()

	logRun := startCompileCheckVibeLog(projectRoot, compileCheckOptions{})
	logRun.failed(errors.New("no Bee build found"))

	complete := assertCompileCheckVibeRun(t, readCompileCheckVibeLog(t, projectRoot))
	if _, found := complete.Context["build_settings"]; found {
		t.Errorf("expected no build_settings on a run that failed before comparing, got %v",
			complete.Context["build_settings"])
	}
}
