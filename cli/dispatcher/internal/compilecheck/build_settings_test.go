package compilecheck

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
	"time"
)

const buildSettingsStampPath = "Library/Bee/tundra.log.json"

// buildSettingsProject is a project of two built assemblies, A and B, whose response files carry the
// Standalone platform define and the given project defines.
type buildSettingsProject struct {
	root string
}

// newBuildSettingsProject writes the two assemblies, a text ProjectSettings whose Standalone group
// holds standaloneDefines, and a build-trace file.
func newBuildSettingsProject(t *testing.T, builtDefines []string, standaloneDefines string) buildSettingsProject {
	t.Helper()
	project := buildSettingsProject{root: t.TempDir()}
	for _, name := range []string{"A", "B"} {
		writePlanAssembly(t, project.root, name, nil)
		project.writeResponseFileDefines(t, name, builtDefines)
	}
	project.writeProjectSettings(t, standaloneDefines)
	writeFileAt(t, filepath.Join(project.root, filepath.FromSlash(buildSettingsStampPath)), "{}")

	return project
}

// writeResponseFileDefines appends the platform define and the given defines to one response file.
func (project buildSettingsProject) writeResponseFileDefines(t *testing.T, name string, defines []string) {
	t.Helper()
	path := filepath.Join(project.root, planDagDirectory, name+".rsp")
	content, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read %s: %v", path, err)
	}
	lines := []string{"-define:UNITY_STANDALONE"}
	for _, define := range defines {
		lines = append(lines, "-define:"+define)
	}
	writeFileAt(t, path, string(content)+"\n"+joinLines(lines))
}

// writeProjectSettings writes a text ProjectSettings with a Standalone and an iPhone group.
func (project buildSettingsProject) writeProjectSettings(t *testing.T, standaloneDefines string) {
	t.Helper()
	writeFileAt(t, filepath.Join(project.root, filepath.FromSlash(projectSettingsRelativePath)),
		"%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n"+
			"  scriptingDefineSymbols:\n"+
			"    Standalone: "+standaloneDefines+"\n"+
			"    iPhone: IOS_ONLY\n"+
			"  additionalCompilerArguments: {}\n"+
			"  scriptingBackend:\n"+
			"    Android: 1\n")
}

// dateProjectSettings sets the modification time of ProjectSettings to the build-trace file's plus
// offset.
func (project buildSettingsProject) dateProjectSettings(t *testing.T, offset time.Duration) {
	t.Helper()
	stampInfo, err := os.Stat(filepath.Join(project.root, filepath.FromSlash(buildSettingsStampPath)))
	if err != nil {
		t.Fatalf("failed to stat the build-trace file: %v", err)
	}
	setModificationTime(t, filepath.Join(project.root, filepath.FromSlash(projectSettingsRelativePath)),
		stampInfo.ModTime().Add(offset))
}

// write writes a project-relative file.
func (project buildSettingsProject) write(t *testing.T, relativePath string, content string) {
	t.Helper()
	writeFileAt(t, filepath.Join(project.root, filepath.FromSlash(relativePath)), content)
}

// advanceStamp moves the build-trace file forward, as a Unity build does.
func (project buildSettingsProject) advanceStamp(t *testing.T) {
	t.Helper()
	setModificationTime(t, filepath.Join(project.root, filepath.FromSlash(buildSettingsStampPath)),
		time.Now().Add(time.Hour))
}

// detect runs the build-settings comparison on the project as it is now.
func (project buildSettingsProject) detect(t *testing.T) (BuildSettingsReport, error) {
	t.Helper()
	graph, err := loadAssemblyGraph(project.root, planDagDirectory)
	if err != nil {
		t.Fatalf("failed to load the assembly graph: %v", err)
	}
	assemblyDefinitions, err := IndexAssemblyDefinitions(walkProjectTree(project.root))
	if err != nil {
		t.Fatalf("failed to index the assembly definitions: %v", err)
	}

	return DetectBuildSettingsChange(project.root, planDagDirectory, graph, assemblyDefinitions)
}

// mustDetect runs the comparison and fails the test when it refuses.
func (project buildSettingsProject) mustDetect(t *testing.T) BuildSettingsReport {
	t.Helper()
	report, err := project.detect(t)
	if err != nil {
		t.Fatalf("expected the run to pass, got error: %v", err)
	}

	return report
}

// recordPath is where the comparison keeps its record.
func (project buildSettingsProject) recordPath() string {
	return filepath.Join(project.root, filepath.FromSlash(compileCheckOutputRoot),
		filepath.Base(planDagDirectory), buildSettingsRecordFileName)
}

// assertBuildRequired checks the error is a Unity-build-required refusal containing every fragment.
func assertBuildRequired(t *testing.T, err error, fragments ...string) {
	t.Helper()
	var required UnityBuildRequiredError
	if !errors.As(err, &required) {
		t.Fatalf("expected a Unity build to be required, got %v", err)
	}
	for _, fragment := range fragments {
		if !strings.Contains(err.Error(), fragment) {
			t.Errorf("expected the refusal to contain %q, got: %v", fragment, err)
		}
	}
}

// Verifies a scripting define of the active group that the response files lack is refused, before
// any record exists.
func TestDetectBuildSettingsChangeRefusesADefineTheLastBuildLacks(t *testing.T) {
	project := newBuildSettingsProject(t, []string{"FOO"}, "FOO;ADDED_LATER")
	project.dateProjectSettings(t, time.Hour)

	report, err := project.detect(t)

	assertBuildRequired(t, err, "ADDED_LATER", "not in the last Unity build")
	if !report.StampFound || !report.DefinesChecked {
		t.Errorf("report = %+v, want the build-trace file found and the defines checked", report)
	}
	if want := "scripting defines of Standalone (not in the last build: ADDED_LATER)"; report.RefusedPart != want {
		t.Errorf("refused part = %q, want %q", report.RefusedPart, want)
	}
	if _, statErr := os.Stat(project.recordPath()); statErr == nil {
		t.Error("expected no record to be written for a refused run")
	}
}

// Verifies a define the response files lack is left to the record when ProjectSettings is older than
// the last build: the build already had it in force and left it out for a reason of its own, such as
// a build profile overriding Player Settings, so refusing it would never clear.
func TestDetectBuildSettingsChangeLeavesADefineOfAnOlderProjectSettingsToTheRecord(t *testing.T) {
	project := newBuildSettingsProject(t, []string{"FOO"}, "FOO;OVERRIDDEN_AWAY")
	project.dateProjectSettings(t, -time.Hour)

	report := project.mustDetect(t)

	if report.Record != buildSettingsRecordNew || report.DefinesChecked {
		t.Errorf("report = %+v, want a new record without the defines checked", report)
	}
}

// Verifies a define in Assets/csc.rsp that the response files lack is not refused by the one-way
// check, and the settings are recorded so a later change shows against the record.
func TestDetectBuildSettingsChangeLeavesCscRspToTheRecord(t *testing.T) {
	project := newBuildSettingsProject(t, []string{"FOO"}, "FOO")
	project.write(t, assetsResponseFileRelative, "-define:NOT_IN_THE_BUILD\n")

	report := project.mustDetect(t)

	if report.Record != buildSettingsRecordNew {
		t.Errorf("record = %q, want %q", report.Record, buildSettingsRecordNew)
	}
}

// Verifies the csc.rsp of an assembly the last build left out does not refuse the run, whatever
// defines it holds.
func TestDetectBuildSettingsChangeIgnoresTheCscRspOfAnAssemblyTheBuildLeftOut(t *testing.T) {
	project := newBuildSettingsProject(t, []string{"FOO"}, "FOO")
	project.write(t, "Assets/Unbuilt/Unbuilt.asmdef", `{"name":"Unbuilt","includePlatforms":["Android"]}`)
	project.write(t, "Assets/Unbuilt/csc.rsp", "-define:NOT_IN_THE_BUILD\n")

	project.mustDetect(t)
}

// Verifies a project without a build-trace file passes and writes no record.
func TestDetectBuildSettingsChangeSkipsTheRecordWithoutABuildStamp(t *testing.T) {
	project := newBuildSettingsProject(t, []string{"FOO"}, "FOO")
	if err := os.Remove(filepath.Join(project.root, filepath.FromSlash(buildSettingsStampPath))); err != nil {
		t.Fatalf("failed to remove the build-trace file: %v", err)
	}

	report := project.mustDetect(t)

	if report.StampFound || report.Record != "" {
		t.Errorf("report = %+v, want no stamp and no record", report)
	}
	if _, statErr := os.Stat(project.recordPath()); statErr == nil {
		t.Error("expected no record without a build-trace file")
	}
}

// Verifies the first run after a build records the settings it found, keyed to that build.
func TestDetectBuildSettingsChangeRecordsTheSettingsOfANewBuild(t *testing.T) {
	project := newBuildSettingsProject(t, []string{"FOO"}, "FOO")

	report := project.mustDetect(t)

	if !report.StampFound || report.Record != buildSettingsRecordNew {
		t.Fatalf("report = %+v, want a new record", report)
	}
	recorded, loaded := loadBuildSettingsRecord(project.recordPath())
	if !loaded {
		t.Fatal("expected the record to be readable")
	}
	if recorded.Settings.DefineGroup != "Standalone" ||
		!reflect.DeepEqual(recorded.Settings.ProjectDefines, []string{"FOO"}) {
		t.Errorf("recorded settings = %+v", recorded.Settings)
	}
	if recorded.Stamp.Path != buildSettingsStampPath {
		t.Errorf("stamp path = %q, want %q", recorded.Stamp.Path, buildSettingsStampPath)
	}
}

// Verifies a record of an earlier build is replaced, not compared, once Unity built again, even
// when the settings differ from it.
func TestDetectBuildSettingsChangeReplacesTheRecordAfterAnotherUnityBuild(t *testing.T) {
	project := newBuildSettingsProject(t, []string{"FOO"}, "FOO")
	project.mustDetect(t)
	project.write(t, packageLockRelativePath, `{"dependencies":{}}`)
	project.advanceStamp(t)

	report := project.mustDetect(t)

	if report.Record != buildSettingsRecordReplaced {
		t.Errorf("record = %q, want %q", report.Record, buildSettingsRecordReplaced)
	}
}

// Verifies a record this version cannot read - written by another format version, or not JSON at
// all - is replaced rather than compared, even when it carries the current stamp and other settings.
func TestDetectBuildSettingsChangeReplacesARecordItCannotRead(t *testing.T) {
	cases := map[string]func(stamp buildStamp) string{
		"another format version": func(stamp buildStamp) string {
			encoded, err := json.Marshal(buildSettingsRecord{
				FormatVersion: buildSettingsRecordFormatVersion + 1,
				Stamp:         stamp,
				Settings:      buildSettings{ResponseFileDigests: map[string]string{assetsResponseFileRelative: "0000"}},
			})
			if err != nil {
				t.Fatalf("failed to encode the record: %v", err)
			}
			return string(encoded)
		},
		"not JSON": func(buildStamp) string { return "{" },
	}
	for name, recordOf := range cases {
		t.Run(name, func(t *testing.T) {
			project := newBuildSettingsProject(t, []string{"FOO"}, "FOO")
			stamp, found := readBuildStamp(project.root)
			if !found {
				t.Fatal("expected the fixture to have a build-trace file")
			}
			writeFileAt(t, project.recordPath(), recordOf(stamp))

			report := project.mustDetect(t)

			if report.Record != buildSettingsRecordNew {
				t.Errorf("record = %q, want %q", report.Record, buildSettingsRecordNew)
			}
			rewritten, loaded := loadBuildSettingsRecord(project.recordPath())
			if !loaded || rewritten.FormatVersion != buildSettingsRecordFormatVersion {
				t.Errorf("rewritten record = %+v (loaded %v), want format version %d",
					rewritten, loaded, buildSettingsRecordFormatVersion)
			}
		})
	}
}

// Verifies settings that match the record of the same build pass and keep the record.
func TestDetectBuildSettingsChangeAcceptsUnchangedSettings(t *testing.T) {
	project := newBuildSettingsProject(t, []string{"FOO"}, "FOO")
	project.mustDetect(t)

	report := project.mustDetect(t)

	if report.Record != buildSettingsRecordKept || report.RefusedPart != "" {
		t.Errorf("report = %+v, want a kept record and no refusal", report)
	}
}

// Verifies every recorded setting that moved since the recorded build refuses the run and names
// itself.
func TestDetectBuildSettingsChangeRefusesSettingsThatMovedSinceTheRecordedBuild(t *testing.T) {
	cases := []struct {
		name   string
		before func(project buildSettingsProject, t *testing.T)
		change func(project buildSettingsProject, t *testing.T)
		part   string
	}{
		{
			name:   "a removed define",
			change: func(project buildSettingsProject, t *testing.T) { project.writeProjectSettings(t, "") },
			part:   "scripting defines of Standalone (removed: FOO)",
		},
		{
			name: "the content of Assets/csc.rsp",
			before: func(project buildSettingsProject, t *testing.T) {
				project.write(t, assetsResponseFileRelative, "-nowarn:CS0168\n")
			},
			change: func(project buildSettingsProject, t *testing.T) {
				project.write(t, assetsResponseFileRelative, "-nowarn:CS0169\n")
			},
			part: assetsResponseFileRelative,
		},
		{
			name: "a csc.rsp added beside an assembly definition",
			change: func(project buildSettingsProject, t *testing.T) {
				project.write(t, "Assets/A/csc.rsp", "-nowarn:CS0168\n")
			},
			part: "Assets/A/csc.rsp",
		},
		{
			name: "defineConstraints",
			change: func(project buildSettingsProject, t *testing.T) {
				project.write(t, "Assets/A/A.asmdef", `{"name":"A","defineConstraints":["FOO"]}`)
			},
			part: "defineConstraints or versionDefines of Assets/A/A.asmdef",
		},
		{
			name: "versionDefines",
			change: func(project buildSettingsProject, t *testing.T) {
				project.write(t, "Assets/A/A.asmdef",
					`{"name":"A","versionDefines":[{"name":"com.example","expression":"1.0","define":"HAS_EXAMPLE"}]}`)
			},
			part: "defineConstraints or versionDefines of Assets/A/A.asmdef",
		},
		{
			name: "packages-lock.json",
			change: func(project buildSettingsProject, t *testing.T) {
				project.write(t, packageLockRelativePath, `{"dependencies":{"com.example":{}}}`)
			},
			part: packageLockRelativePath,
		},
		{
			name: "manifest.json",
			change: func(project buildSettingsProject, t *testing.T) {
				project.write(t, packageManifestRelativePath, `{"dependencies":{"com.example":"1.0.0"}}`)
			},
			part: packageManifestRelativePath,
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			project := newBuildSettingsProject(t, []string{"FOO"}, "FOO")
			if testCase.before != nil {
				testCase.before(project, t)
			}
			project.mustDetect(t)
			testCase.change(project, t)

			report, err := project.detect(t)

			assertBuildRequired(t, err, testCase.part, "changed since the last Unity build")
			if report.RefusedPart != testCase.part || report.Record != buildSettingsRecordKept {
				t.Errorf("report = %+v, want a kept record refused for %q", report, testCase.part)
			}
		})
	}
}

// Verifies a refusal for a moved setting clears once Unity builds again: the stamp moves forward
// and the next run records the settings as they are.
func TestDetectBuildSettingsChangeClearsARefusalOnceUnityBuildsAgain(t *testing.T) {
	project := newBuildSettingsProject(t, []string{"FOO"}, "FOO")
	project.mustDetect(t)
	project.write(t, assetsResponseFileRelative, "-nowarn:CS0168\n")
	if _, err := project.detect(t); err == nil {
		t.Fatal("expected the changed csc.rsp to be refused before Unity builds again")
	}
	project.advanceStamp(t)

	report := project.mustDetect(t)

	if report.Record != buildSettingsRecordReplaced {
		t.Errorf("record = %q, want %q", report.Record, buildSettingsRecordReplaced)
	}
	project.mustDetect(t)
}

// Verifies a binary ProjectSettings leaves the scripting defines out of the settings, so only the
// other settings are compared.
func TestReadBuildSettingsLeavesTheDefinesOutOfABinaryProjectSettings(t *testing.T) {
	project := newBuildSettingsProject(t, []string{}, "")
	project.write(t, projectSettingsRelativePath, "\x00\x00\x00\x00binary FOO;ADDED_LATER")

	report := project.mustDetect(t)
	recorded, _ := loadBuildSettingsRecord(project.recordPath())

	if report.Record != buildSettingsRecordNew || recorded.Settings.DefineGroup != "" ||
		recorded.Settings.ProjectDefines != nil {
		t.Errorf("report = %+v, settings = %+v, want no define group and no defines", report, recorded.Settings)
	}
}

// Verifies the ProjectSettings group is picked from the platform define of the response files, and
// a Dedicated Server build or an unknown platform leaves it undecided.
func TestReadProjectDefinesPicksTheGroupOfTheResponseFilePlatform(t *testing.T) {
	cases := []struct {
		name    string
		defines []string
		group   string
	}{
		{"Standalone", []string{"UNITY_STANDALONE"}, "Standalone"},
		{"iPhone", []string{"UNITY_IOS"}, "iPhone"},
		{"Dedicated Server", []string{"UNITY_STANDALONE", "UNITY_SERVER"}, ""},
		{"unknown", []string{"UNITY_SOMETHING_ELSE"}, ""},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			graph := assemblyGraph{
				names:  []string{"A"},
				byName: map[string]ResponseFile{"A": {AssemblyName: "A", Defines: testCase.defines}},
			}

			if group := defineGroupOf(graph); group != testCase.group {
				t.Errorf("group = %q, want %q", group, testCase.group)
			}
		})
	}
}

// Verifies a scripting define value is split on semicolons, commas and spaces alike.
func TestReadProjectDefinesSplitsOnEverySeparator(t *testing.T) {
	project := newBuildSettingsProject(t, []string{}, "A;B, C D")

	_, defines, err := readProjectDefines(project.root, "Standalone")
	if err != nil {
		t.Fatalf("failed to read the defines: %v", err)
	}
	if !reflect.DeepEqual(defines, []string{"A", "B", "C", "D"}) {
		t.Errorf("defines = %v, want [A B C D]", defines)
	}
}

// Verifies the scan of one group's defines ends where the next map of the same depth begins, so a
// group with no defines does not take another map's value for its own.
func TestReadProjectDefinesStopsAtTheNextMap(t *testing.T) {
	project := newBuildSettingsProject(t, []string{}, "")

	content, err := os.ReadFile(filepath.Join(project.root, filepath.FromSlash(projectSettingsRelativePath)))
	if err != nil {
		t.Fatalf("failed to read the fixture: %v", err)
	}
	if value, found := scriptingDefinesValue(content, "Android"); found {
		t.Errorf("Android defines = %q, want none found", value)
	}
}

// Verifies an empty scriptingDefineSymbols map gives the group no defines rather than an unknown
// group.
func TestReadProjectDefinesReadsAnEmptyMap(t *testing.T) {
	project := newBuildSettingsProject(t, []string{}, "")
	project.write(t, projectSettingsRelativePath,
		"%YAML 1.1\nPlayerSettings:\n  scriptingDefineSymbols: {}\n  additionalCompilerArguments: {}\n")

	group, defines, err := readProjectDefines(project.root, "Standalone")
	if err != nil {
		t.Fatalf("failed to read the defines: %v", err)
	}
	if group != "Standalone" || defines == nil || len(defines) != 0 {
		t.Errorf("group = %q, defines = %#v, want Standalone with an empty list", group, defines)
	}
}
