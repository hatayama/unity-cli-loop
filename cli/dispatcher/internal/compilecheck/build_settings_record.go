package compilecheck

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"slices"
	"sort"
	"strings"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/compilecheck/resultstore"
)

const (
	buildSettingsRecordFileName      = "build-settings.json"
	buildSettingsRecordFormatVersion = 1
	buildSettingsRecordNew           = "new"
	buildSettingsRecordReplaced      = "replaced"
	buildSettingsRecordKept          = "kept"
)

// buildStampRelativePaths are the files Bee rewrites on every build, even one that compiles nothing,
// tried in this order.
var buildStampRelativePaths = []string{
	"Library/Bee/tundra.log.json",
	"Library/Bee/TundraBuildState.state",
	"Library/Bee/fullprofile.json",
}

// buildStamp identifies the last Unity build by the newest file Bee rewrote for it.
type buildStamp struct {
	Path    string `json:"path"`    // project-relative
	ModTime int64  `json:"modTime"` // UnixNano
	Size    int64  `json:"size"`
}

// buildSettingsRecord is the build settings a run found right after a Unity build, keyed to that
// build.
type buildSettingsRecord struct {
	FormatVersion int           `json:"formatVersion"`
	Stamp         buildStamp    `json:"stamp"`
	Settings      buildSettings `json:"settings"`
}

// BuildSettingsReport says what the build-settings comparison of a run found.
type BuildSettingsReport struct {
	StampFound bool   // a build-trace file of the last Unity build exists
	Record     string // "new", "replaced", "kept", or "" when nothing was recorded or compared
	// RefusedPart names the setting the run was refused for: a setting that moved since the recorded
	// build, or a scripting define the last build lacks, which is checked before the record is read.
	RefusedPart string
	// DefinesChecked says the run looked for a project define the build lacks, which it does only
	// when there is no build-trace file or ProjectSettings was saved after the last build.
	DefinesChecked bool
}

// BuildSettingsError carries the build-settings report of a run that stopped with an error after
// the comparison ran. It unwraps to that error, so the error still classifies as itself.
type BuildSettingsError struct {
	Report BuildSettingsReport
	Err    error
}

func (err BuildSettingsError) Error() string { return err.Err.Error() }

func (err BuildSettingsError) Unwrap() error { return err.Err }

// BuildSettingsReportOf returns the build-settings report a failed run's error carries.
func BuildSettingsReportOf(err error) (BuildSettingsReport, bool) {
	var carrier BuildSettingsError
	if !errors.As(err, &carrier) {
		return BuildSettingsReport{}, false
	}

	return carrier.Report, true
}

// withBuildSettingsReport attaches the build-settings report to an error, leaving nil as nil.
func withBuildSettingsReport(report BuildSettingsReport, err error) error {
	if err == nil {
		return nil
	}

	return BuildSettingsError{Report: report, Err: err}
}

// DetectBuildSettingsChange refuses a run whose build settings are not the ones the last Unity
// build used: a scripting define of the active group the response files lack, and - against a
// record keyed to the last build - any recorded setting that moved since.
// Why the one-way define check runs before the record and on every run: an added define should be
// seen by the first run as well, and a record written before the check would take the added define
// for part of the build.
// Why a different stamp never refuses: Unity built again, so the response files reflect the
// settings as they are now and there is nothing to compare them with; a setting removed afterwards
// shows against the record the next run reads.
func DetectBuildSettingsChange(
	projectRoot string, dagDir string, graph assemblyGraph,
	assemblyDefinitions map[string]AssemblyDefinition,
) (BuildSettingsReport, error) {
	report := BuildSettingsReport{}
	current, err := readBuildSettings(projectRoot, graph, assemblyDefinitions)
	if err != nil {
		return report, err
	}
	stamp, found := readBuildStamp(projectRoot)
	report.StampFound = found
	report.DefinesChecked = !found || projectSettingsSavedAfter(projectRoot, stamp)
	if report.DefinesChecked {
		if define := defineTheBuildLacks(current, graph); define != "" {
			report.RefusedPart = fmt.Sprintf(
				"scripting defines of %s (not in the last build: %s)", current.DefineGroup, define)
			return report, unityBuildRequired("scripting define %s is not in the last Unity build", define)
		}
	}
	if !found {
		return report, nil
	}

	recordPath := filepath.Join(projectRoot, filepath.FromSlash(compileCheckOutputRoot),
		filepath.Base(dagDir), buildSettingsRecordFileName)
	recorded, loaded := loadBuildSettingsRecord(recordPath)
	if loaded && recorded.Stamp == stamp {
		report.Record = buildSettingsRecordKept
		if part := firstDifference(recorded.Settings, current); part != "" {
			report.RefusedPart = part
			return report, unityBuildRequired("%s changed since the last Unity build", part)
		}
		return report, nil
	}

	report.Record = buildSettingsRecordNew
	if loaded {
		report.Record = buildSettingsRecordReplaced
	}
	record := buildSettingsRecord{
		FormatVersion: buildSettingsRecordFormatVersion, Stamp: stamp, Settings: current,
	}

	return report, writeBuildSettingsRecord(recordPath, record)
}

// projectSettingsSavedAfter reports whether ProjectSettings was saved after the build the stamp
// belongs to.
// Why an older ProjectSettings is not checked for defines the build lacks: its defines were already
// in force when Unity built, so one missing from the response files was left out for a reason this
// check cannot see - a Unity 6 build profile that overrides Player Settings is one - and refusing it
// would refuse every run, since building again writes the same response files.
func projectSettingsSavedAfter(projectRoot string, stamp buildStamp) bool {
	info, err := os.Stat(filepath.Join(projectRoot, filepath.FromSlash(projectSettingsRelativePath)))
	if err != nil {
		return false
	}

	return info.ModTime().UnixNano() > stamp.ModTime
}

// defineTheBuildLacks names a scripting define of the active group that some response file lacks.
// Unity adds the project's defines to every assembly, so one missing anywhere was added after the
// build.
// Why csc.rsp defines are not checked here: Unity reads the csc.rsp beside an .asmdef instead of
// Assets/csc.rsp and copies its text without splitting it, so reading defines back out of the
// response files would refuse projects Unity builds; csc.rsp is compared against the record only.
func defineTheBuildLacks(current buildSettings, graph assemblyGraph) string {
	if current.DefineGroup == "" {
		return ""
	}
	for _, define := range current.ProjectDefines {
		for _, name := range graph.names {
			if !slices.Contains(graph.byName[name].Defines, define) {
				return define
			}
		}
	}

	return ""
}

// readBuildStamp finds the newest build-trace file Bee left. false means none exists.
func readBuildStamp(projectRoot string) (buildStamp, bool) {
	newest := buildStamp{}
	found := false
	for _, relativePath := range buildStampRelativePaths {
		info, err := os.Stat(filepath.Join(projectRoot, filepath.FromSlash(relativePath)))
		if err != nil {
			continue
		}
		modTime := info.ModTime().UnixNano()
		if found && modTime <= newest.ModTime {
			continue
		}
		newest = buildStamp{Path: relativePath, ModTime: modTime, Size: info.Size()}
		found = true
	}

	return newest, found
}

// loadBuildSettingsRecord reads the record. false means there is none this version can use.
func loadBuildSettingsRecord(path string) (buildSettingsRecord, bool) {
	record := buildSettingsRecord{}
	content, err := os.ReadFile(path)
	if err != nil {
		return record, false
	}
	if json.Unmarshal(content, &record) != nil || record.FormatVersion != buildSettingsRecordFormatVersion {
		return buildSettingsRecord{}, false
	}

	return record, true
}

// writeBuildSettingsRecord writes the record through a temporary file, so a reader never sees half
// of it.
func writeBuildSettingsRecord(path string, record buildSettingsRecord) error {
	content, err := json.Marshal(record)
	if err != nil {
		return fmt.Errorf("failed to encode %s: %w", path, err)
	}
	directory := filepath.Dir(path)
	if err := os.MkdirAll(directory, 0o755); err != nil {
		return fmt.Errorf("failed to create %s: %w", directory, err)
	}

	return resultstore.WriteFileAtomically(directory, path, content)
}

// firstDifference names the first recorded setting that differs now, or "" when none does.
func firstDifference(recorded buildSettings, current buildSettings) string {
	if part := defineDifference(recorded, current); part != "" {
		return part
	}
	if path := firstDifferentKey(recorded.ResponseFileDigests, current.ResponseFileDigests); path != "" {
		return path
	}
	if path := firstDifferentKey(recorded.AssemblyConstraints, current.AssemblyConstraints); path != "" {
		return "defineConstraints or versionDefines of " + path
	}
	if recorded.PackageLockDigest != current.PackageLockDigest {
		return packageLockRelativePath
	}
	if recorded.ManifestDigest != current.ManifestDigest {
		return packageManifestRelativePath
	}

	return ""
}

// defineDifference describes how the scripting defines moved, or "" when they did not.
func defineDifference(recorded buildSettings, current buildSettings) string {
	if recorded.DefineGroup != current.DefineGroup {
		return fmt.Sprintf("scripting define group (%q to %q)", recorded.DefineGroup, current.DefineGroup)
	}
	added := missingFrom(current.ProjectDefines, recorded.ProjectDefines)
	removed := missingFrom(recorded.ProjectDefines, current.ProjectDefines)
	if len(added) == 0 && len(removed) == 0 {
		return ""
	}
	changes := []string{}
	if len(added) > 0 {
		changes = append(changes, "added: "+strings.Join(added, ", "))
	}
	if len(removed) > 0 {
		changes = append(changes, "removed: "+strings.Join(removed, ", "))
	}

	return fmt.Sprintf("scripting defines of %s (%s)", current.DefineGroup, strings.Join(changes, "; "))
}

// missingFrom lists the values of from that values lacks, in the order of from.
func missingFrom(from []string, values []string) []string {
	missing := []string{}
	for _, value := range from {
		if !slices.Contains(values, value) {
			missing = append(missing, value)
		}
	}

	return missing
}

// firstDifferentKey names, in sorted order, the first key whose value differs or that only one map
// has.
func firstDifferentKey(recorded map[string]string, current map[string]string) string {
	keys := map[string]bool{}
	for key := range recorded {
		keys[key] = true
	}
	for key := range current {
		keys[key] = true
	}
	sorted := make([]string, 0, len(keys))
	for key := range keys {
		sorted = append(sorted, key)
	}
	sort.Strings(sorted)
	for _, key := range sorted {
		recordedValue, inRecorded := recorded[key]
		currentValue, inCurrent := current[key]
		if inRecorded != inCurrent || recordedValue != currentValue {
			return key
		}
	}

	return ""
}
