package projectverify_test

import (
	"encoding/json"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/projectverify"
)

// Verifies a clean project reports success, zero counts for every check, and an empty Findings array.
func TestRunReportsCleanProject(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/A.txt":          "a",
		"Assets/A.txt.meta":     metaText(guidOf(1)),
		"Assets/Sub/":           "",
		"Assets/Sub.meta":       metaText(guidOf(2)),
		"Assets/Sub/B.txt":      "b",
		"Assets/Sub/B.txt.meta": metaText(guidOf(3)),
	})

	report := runProject(t, root)

	if !report.Success || report.FindingCount != 0 || report.Truncated {
		t.Fatalf("Success = %v, FindingCount = %d, Truncated = %v, want true, 0, false",
			report.Success, report.FindingCount, report.Truncated)
	}
	wantCounts := map[string]int{
		"CONFLICT_MARKER": 0, "GUID_DUPLICATE": 0, "GUID_INVALID": 0,
		"MANIFEST_INVALID": 0, "META_MISSING": 0, "META_ORPHAN": 0,
	}
	if !reflect.DeepEqual(report.CountsByCheck, wantCounts) {
		t.Fatalf("CountsByCheck = %v, want %v", report.CountsByCheck, wantCounts)
	}
	encoded, err := json.Marshal(report)
	if err != nil {
		t.Fatalf("failed to encode the report: %v", err)
	}
	if !strings.Contains(string(encoded), `"Findings":[]`) {
		t.Fatalf("encoded report %s does not contain an empty Findings array", encoded)
	}
	if report.Message != "No problems found in 3 .meta files." {
		t.Fatalf("Message = %q", report.Message)
	}
}

// Verifies findings are listed by check order, then by path, and summarized per check. The local
// package outside the project is scanned after Assets but sorts first by its absolute path, and
// the conflict block is found after both .cs files, so the order is the sort's, not the scan's.
func TestRunSortsFindingsAndSummarizes(t *testing.T) {
	root := writeProject(t, map[string]string{
		"Assets/b.cs":       "class B {}",
		"Assets/a.cs":       "class A {}",
		"Assets/c.txt":      conflictBlock,
		"Assets/c.txt.meta": metaText(guidOf(1)),
	})
	packageDir := filepath.Join(filepath.Dir(root), "OutsidePkg")
	writePackage(t, packageDir, map[string]string{"Runtime.cs": "class Runtime {}"})
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"),
		manifestJSON(t, map[string]any{"com.example.outside": "file:../../OutsidePkg"}))

	report := runProject(t, root)

	assertFindings(t, report,
		conflictMarker("Assets/c.txt", 2),
		metaMissing(filepath.ToSlash(packageDir)+"/Runtime.cs"),
		metaMissing("Assets/a.cs"),
		metaMissing("Assets/b.cs"))
	if report.Message != "Found 4 problems: 1 CONFLICT_MARKER, 3 META_MISSING." {
		t.Fatalf("Message = %q", report.Message)
	}
}

// Verifies a single finding is summarized as one problem.
func TestRunUsesSingularForOneProblem(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{"Assets/NoMeta.cs": "class NoMeta {}"})

	report := runProject(t, root)

	if report.Message != "Found 1 problem: 1 META_MISSING." {
		t.Fatalf("Message = %q", report.Message)
	}
}

// Verifies a clean project with one .meta file is summarized in the singular.
func TestRunUsesSingularForOneMetaFile(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/A.txt":      "a",
		"Assets/A.txt.meta": metaText(guidOf(1)),
	})

	report := runProject(t, root)

	if report.Message != "No problems found in 1 .meta file." {
		t.Fatalf("Message = %q", report.Message)
	}
}

// Verifies each check lists at most its first 100 findings while the counts keep the total.
func TestRunTruncatesEachCheckAt100(t *testing.T) {
	files := map[string]string{}
	for i := 0; i < 101; i++ {
		files[fmt.Sprintf("Assets/File%03d.cs", i)] = "class File {}"
	}
	root := writeProjectWithManifest(t, files)

	report := runProject(t, root)

	if report.FindingCount != 101 || report.CountsByCheck[projectverify.CheckMetaMissing] != 101 {
		t.Fatalf("FindingCount = %d, META_MISSING count = %d, want 101 and 101",
			report.FindingCount, report.CountsByCheck[projectverify.CheckMetaMissing])
	}
	if len(report.Findings) != 100 || !report.Truncated {
		t.Fatalf("len(Findings) = %d, Truncated = %v, want 100 and true", len(report.Findings), report.Truncated)
	}
	if report.Findings[99].Path != "Assets/File099.cs" {
		t.Fatalf("last listed finding = %s, want Assets/File099.cs", report.Findings[99].Path)
	}
	want := "Found 101 problems: 101 META_MISSING. Only the first 100 findings of each check are listed."
	if report.Message != want {
		t.Fatalf("Message = %q, want %q", report.Message, want)
	}
}

// fileState is what TestRunDoesNotModifyProject compares for each entry.
type fileState struct {
	mode    fs.FileMode
	modTime time.Time
	content string
}

// snapshotTree records the type, modification time, and file contents of everything under root.
func snapshotTree(t *testing.T, root string) map[string]fileState {
	t.Helper()
	states := map[string]fileState{}
	err := filepath.WalkDir(root, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		info, err := entry.Info()
		if err != nil {
			return err
		}
		state := fileState{mode: info.Mode(), modTime: info.ModTime()}
		if info.Mode().IsRegular() {
			content, err := os.ReadFile(path)
			if err != nil {
				return err
			}
			state.content = string(content)
		}
		states[path] = state
		return nil
	})
	if err != nil {
		t.Fatalf("failed to snapshot %s: %v", root, err)
	}
	return states
}

// Verifies a run over a project full of problems creates, changes, and deletes nothing.
func TestRunDoesNotModifyProject(t *testing.T) {
	root := writeProject(t, map[string]string{
		"Assets/NoMeta.cs":                      "class NoMeta {}",
		"Assets/Gone.png.meta":                  metaText(guidOf(1)),
		"Assets/Bad.txt":                        "bad",
		"Assets/Bad.txt.meta":                   metaText("nothex"),
		"Assets/Copy1.txt":                      "copy",
		"Assets/Copy1.txt.meta":                 metaText(guidOf(2)),
		"Assets/Copy2.txt":                      "copy",
		"Assets/Copy2.txt.meta":                 metaText(guidOf(2)),
		"Assets/Scene.unity":                    conflictBlock,
		"Assets/Scene.unity.meta":               metaText(guidOf(3)),
		"ProjectSettings/ProjectSettings.asset": conflictBlock,
	})
	writePackage(t, filepath.Join(root, "Packages", "com.example.embedded"),
		map[string]string{"Editor.cs": "class Editor {}"})
	writePackage(t, filepath.Join(filepath.Dir(root), "LocalPkg"),
		map[string]string{"Runtime.cs": "class Runtime {}"})
	writeFileAt(t, filepath.Join(root, "Packages", "manifest.json"), manifestJSON(t, map[string]any{
		"com.example.local": "file:../../LocalPkg",
		"com.example.gone":  "file:../NoSuchPackage",
	}))
	before := snapshotTree(t, filepath.Dir(root))

	report := runProject(t, root)

	for check, count := range report.CountsByCheck {
		if count == 0 {
			t.Fatalf("the fixture did not trigger %s: %v", check, report.CountsByCheck)
		}
	}
	after := snapshotTree(t, filepath.Dir(root))
	if !reflect.DeepEqual(before, after) {
		t.Fatalf("Run changed the project:\nbefore %v\nafter  %v", before, after)
	}
}
