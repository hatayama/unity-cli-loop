// Package projectverify checks a Unity project's files the way Unity reads them on its next
// import, without starting Unity, and reports what Unity would silently change or fail on.
package projectverify

import (
	"errors"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

// The checks a Finding can carry.
const (
	// CheckConflictMarker reports a merge conflict block left in a file.
	CheckConflictMarker = "CONFLICT_MARKER"
	// CheckManifestInvalid reports a Packages/manifest.json Unity cannot resolve packages from.
	CheckManifestInvalid = "MANIFEST_INVALID"
	// CheckGUIDDuplicate reports two or more .meta files that declare the same GUID.
	CheckGUIDDuplicate = "GUID_DUPLICATE"
	// CheckGUIDInvalid reports a .meta file without a guid line Unity accepts.
	CheckGUIDInvalid = "GUID_INVALID"
	// CheckMetaMissing reports an asset without its .meta file.
	CheckMetaMissing = "META_MISSING"
	// CheckMetaOrphan reports a .meta file whose asset is gone.
	CheckMetaOrphan = "META_ORPHAN"
)

// maxFindingsPerCheck caps the findings listed per check, so one mass problem (a folder copied
// without its .meta files) cannot bury the other checks.
const maxFindingsPerCheck = 100

const (
	assetsDirectoryName          = "Assets"
	projectSettingsDirectoryName = "ProjectSettings"
	packagesDirectoryName        = "Packages"
)

// Report is everything one Run found, shaped as the JSON uloop verify-project prints.
type Report struct {
	Success       bool           `json:"Success"`
	ProjectRoot   string         `json:"ProjectRoot"`
	FindingCount  int            `json:"FindingCount"`
	CountsByCheck map[string]int `json:"CountsByCheck"`
	Findings      []Finding      `json:"Findings"`
	Truncated     bool           `json:"Truncated"`
	ScannedRoots  []string       `json:"ScannedRoots"`
	MetaFileCount int            `json:"MetaFileCount"`
	Message       string         `json:"Message"`
}

// Finding is one problem Unity would act on at the next import.
type Finding struct {
	Check        string   `json:"Check"`
	Path         string   `json:"Path"`
	Line         int      `json:"Line,omitempty"`
	GUID         string   `json:"GUID,omitempty"`
	RelatedPaths []string `json:"RelatedPaths,omitempty"`
	Message      string   `json:"Message"`
}

// Run reads projectRoot the way Unity would on its next import and reports what Unity would
// silently change or fail on. It never writes. projectRoot must be absolute, its Assets and
// ProjectSettings entries must be directories, and its Packages entry, if present, must be a
// directory. A missing manifest or local package is a finding; any other file system error stops
// the run and is returned.
func Run(projectRoot string) (Report, error) {
	if err := requireProjectLayout(projectRoot); err != nil {
		return Report{}, err
	}

	v := &verifier{projectRoot: projectRoot, guidPaths: map[string][]string{}}
	roots, err := v.collectRoots()
	if err != nil {
		return Report{}, err
	}
	for _, root := range roots {
		if err := v.scanDirectory(root.path, root.display); err != nil {
			return Report{}, err
		}
	}
	if err := v.scanProjectSettings(); err != nil {
		return Report{}, err
	}
	if err := v.scanPackageManifestFiles(); err != nil {
		return Report{}, err
	}
	v.addDuplicateGUIDFindings()
	return v.buildReport(roots), nil
}

// requireProjectLayout enforces Run's precondition: an absolute root whose Assets and
// ProjectSettings are directories, and whose Packages, if present, is a directory.
func requireProjectLayout(projectRoot string) error {
	if !filepath.IsAbs(projectRoot) {
		return fmt.Errorf("project root must be an absolute path: %s", projectRoot)
	}
	for _, name := range []string{assetsDirectoryName, projectSettingsDirectoryName} {
		path := filepath.Join(projectRoot, name)
		info, err := os.Stat(path)
		if err != nil {
			return fmt.Errorf("inspect %s: %w", path, err)
		}
		if !info.IsDir() {
			return fmt.Errorf("%s is not a directory", path)
		}
	}
	return requireDirectoryIfPresent(filepath.Join(projectRoot, packagesDirectoryName))
}

// requireDirectoryIfPresent fails when path exists but is not a directory, so that every reader of
// Packages can rely on it being absent or a folder. Their own errors cannot be relied on: on
// Windows, a regular file read as a folder, and a path below it, can read as empty or missing
// instead of failing, which would turn the broken layout into a missing-manifest finding there only.
// Stat follows a symbolic link, so a linked Packages folder is still accepted.
func requireDirectoryIfPresent(path string) error {
	info, err := os.Stat(path)
	if errors.Is(err, fs.ErrNotExist) {
		return nil
	}
	if err != nil {
		return fmt.Errorf("inspect %s: %w", path, err)
	}
	if !info.IsDir() {
		return fmt.Errorf("%s is not a directory", path)
	}
	return nil
}

// verifier collects the findings of one Run.
type verifier struct {
	projectRoot string
	findings    []Finding
	// guidPaths maps each lower-cased GUID to the display paths of the .meta files declaring it.
	guidPaths     map[string][]string
	metaFileCount int
}

// scanRoot is one folder whose .meta files Unity manages.
type scanRoot struct {
	path    string
	display string
}

func (v *verifier) addFinding(check string, path string, line int, message string) {
	v.findings = append(v.findings, Finding{Check: check, Path: path, Line: line, Message: message})
}

// buildReport puts the findings in report order and lists at most maxFindingsPerCheck of each check.
func (v *verifier) buildReport(roots []scanRoot) Report {
	counts := map[string]int{}
	for _, check := range orderedChecks() {
		counts[check] = 0
	}
	for _, finding := range v.findings {
		counts[finding.Check]++
	}

	sort.SliceStable(v.findings, func(i, j int) bool { return lessFinding(v.findings[i], v.findings[j]) })
	// An empty list, not nil, so the JSON shows "Findings": [] rather than null.
	listed := []Finding{}
	listedPerCheck := map[string]int{}
	for _, finding := range v.findings {
		if listedPerCheck[finding.Check] >= maxFindingsPerCheck {
			continue
		}
		listed = append(listed, finding)
		listedPerCheck[finding.Check]++
	}

	displays := make([]string, 0, len(roots))
	for _, root := range roots {
		displays = append(displays, root.display)
	}
	truncated := len(listed) < len(v.findings)
	return Report{
		Success:       len(v.findings) == 0,
		ProjectRoot:   v.projectRoot,
		FindingCount:  len(v.findings),
		CountsByCheck: counts,
		Findings:      listed,
		Truncated:     truncated,
		ScannedRoots:  displays,
		MetaFileCount: v.metaFileCount,
		Message:       summaryMessage(counts, len(v.findings), truncated, v.metaFileCount),
	}
}

// orderedChecks lists the checks in report order. A function, not a package variable, so no
// caller can change the order for everyone else.
func orderedChecks() []string {
	return []string{
		CheckConflictMarker, CheckManifestInvalid, CheckGUIDDuplicate,
		CheckGUIDInvalid, CheckMetaMissing, CheckMetaOrphan,
	}
}

// checkRank is the position of check in orderedChecks: the order to fix things in, from files
// Unity cannot read, through package resolution and references that can land on the wrong asset,
// to GUIDs Unity reassigns.
func checkRank(check string) int {
	switch check {
	case CheckConflictMarker:
		return 0
	case CheckManifestInvalid:
		return 1
	case CheckGUIDDuplicate:
		return 2
	case CheckGUIDInvalid:
		return 3
	case CheckMetaMissing:
		return 4
	default:
		return 5
	}
}

// lessFinding orders findings by check, then path, then line. It is a separate function because
// cyclop counts the branches of a closure toward the function that contains it.
func lessFinding(a Finding, b Finding) bool {
	if checkRank(a.Check) != checkRank(b.Check) {
		return checkRank(a.Check) < checkRank(b.Check)
	}
	if a.Path != b.Path {
		return a.Path < b.Path
	}
	return a.Line < b.Line
}

// summaryMessage is the one-line verdict of a report.
func summaryMessage(counts map[string]int, findingCount int, truncated bool, metaFileCount int) string {
	if findingCount == 0 {
		return fmt.Sprintf("No problems found in %d %s.",
			metaFileCount, pluralize(metaFileCount, ".meta file", ".meta files"))
	}

	parts := []string{}
	for _, check := range orderedChecks() {
		if counts[check] > 0 {
			parts = append(parts, fmt.Sprintf("%d %s", counts[check], check))
		}
	}
	message := fmt.Sprintf("Found %d %s: %s.",
		findingCount, pluralize(findingCount, "problem", "problems"), strings.Join(parts, ", "))
	if truncated {
		message += fmt.Sprintf(" Only the first %d findings of each check are listed.", maxFindingsPerCheck)
	}
	return message
}

func pluralize(count int, singular string, plural string) string {
	if count == 1 {
		return singular
	}
	return plural
}
