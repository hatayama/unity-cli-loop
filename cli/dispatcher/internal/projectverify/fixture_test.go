package projectverify_test

import (
	"encoding/hex"
	"encoding/json"
	"fmt"
	"hash/fnv"
	"os"
	"path/filepath"
	"reflect"
	"runtime"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/projectverify"
)

// writeProject creates <t.TempDir()>/Project with Assets/ and ProjectSettings/, then writes each
// entry of files (slash-separated relative paths). A key ending in "/" creates a directory.
// The project sits one level down so a test can put local packages next to it in
// filepath.Dir(root) without touching another test's directory.
func writeProject(t *testing.T, files map[string]string) string {
	t.Helper()
	root := filepath.Join(t.TempDir(), "Project")
	makeDirectory(t, filepath.Join(root, "Assets"))
	makeDirectory(t, filepath.Join(root, "ProjectSettings"))
	writeFiles(t, root, files)
	return root
}

// writeProjectWithManifest does the same and also writes Packages/manifest.json as
// {"dependencies":{}} unless files already has that key.
func writeProjectWithManifest(t *testing.T, files map[string]string) string {
	t.Helper()
	withManifest := map[string]string{"Packages/manifest.json": `{"dependencies":{}}`}
	for relativePath, content := range files {
		withManifest[relativePath] = content
	}
	return writeProject(t, withManifest)
}

// writeFiles writes each entry of files under directory. A key ending in "/" creates a directory.
func writeFiles(t *testing.T, directory string, files map[string]string) {
	t.Helper()
	for relativePath, content := range files {
		path := filepath.Join(directory, filepath.FromSlash(relativePath))
		if strings.HasSuffix(relativePath, "/") {
			makeDirectory(t, path)
			continue
		}
		writeFileAt(t, path, content)
	}
}

// writeFileAt writes content to path, creating the parent directories.
func writeFileAt(t *testing.T, path string, content string) {
	t.Helper()
	makeDirectory(t, filepath.Dir(path))
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write %s: %v", path, err)
	}
}

func makeDirectory(t *testing.T, path string) {
	t.Helper()
	if err := os.MkdirAll(path, 0o755); err != nil {
		t.Fatalf("failed to create %s: %v", path, err)
	}
}

// metaText returns .meta content whose guid line has the given value.
func metaText(guid string) string {
	return "fileFormatVersion: 2\nguid: " + guid + "\n"
}

// guidOf returns a distinct valid GUID for n.
func guidOf(n int) string {
	return fmt.Sprintf("%032x", n+1)
}

// writePackage creates a package folder at dir: package.json, the .meta file Unity gives it, and
// files. The .meta GUID is derived from dir so two packages in one test never share it.
func writePackage(t *testing.T, dir string, files map[string]string) {
	t.Helper()
	hash := fnv.New128a()
	_, _ = hash.Write([]byte(dir))
	writeFileAt(t, filepath.Join(dir, "package.json"), `{"name":"com.example.package"}`)
	writeFileAt(t, filepath.Join(dir, "package.json.meta"), metaText(hex.EncodeToString(hash.Sum(nil))))
	writeFiles(t, dir, files)
}

// manifestJSON returns Packages/manifest.json content with the given dependencies.
func manifestJSON(t *testing.T, dependencies map[string]any) string {
	t.Helper()
	content, err := json.Marshal(map[string]any{"dependencies": dependencies})
	if err != nil {
		t.Fatalf("failed to encode the manifest: %v", err)
	}
	return string(content)
}

// makeSymlink links link to target, skipping the test on Windows, where creating a symbolic link
// needs a privilege CI does not grant.
func makeSymlink(t *testing.T, target string, link string) {
	t.Helper()
	if runtime.GOOS == "windows" {
		t.Skip("creating symbolic links needs extra privileges on Windows.")
	}
	makeDirectory(t, filepath.Dir(link))
	if err := os.Symlink(target, link); err != nil {
		t.Fatalf("failed to link %s to %s: %v", link, target, err)
	}
}

// lockDirectory removes every permission from directory and restores them when the test ends,
// so t.TempDir can clean up.
func lockDirectory(t *testing.T, directory string) {
	t.Helper()
	if runtime.GOOS == "windows" {
		t.Skip("POSIX directory permissions are required for this failure.")
	}
	if os.Geteuid() == 0 {
		t.Skip("root ignores file and directory permissions.")
	}
	if err := os.Chmod(directory, 0); err != nil {
		t.Fatalf("failed to chmod %s: %v", directory, err)
	}
	t.Cleanup(func() {
		_ = os.Chmod(directory, 0o755)
	})
}

// findingKey is the part of a Finding the tests compare. Message is compared only where a test
// says so.
type findingKey struct {
	Check        string
	Path         string
	Line         int
	GUID         string
	RelatedPaths []string
}

func manifestInvalid() findingKey {
	return findingKey{Check: projectverify.CheckManifestInvalid, Path: "Packages/manifest.json"}
}

func metaMissing(path string) findingKey {
	return findingKey{Check: projectverify.CheckMetaMissing, Path: path}
}

func metaOrphan(path string) findingKey {
	return findingKey{Check: projectverify.CheckMetaOrphan, Path: path}
}

func guidInvalid(path string) findingKey {
	return findingKey{Check: projectverify.CheckGUIDInvalid, Path: path}
}

func conflictMarker(path string, line int) findingKey {
	return findingKey{Check: projectverify.CheckConflictMarker, Path: path, Line: line}
}

// assertFindings checks the listed findings, in report order, against want.
func assertFindings(t *testing.T, report projectverify.Report, want ...findingKey) {
	t.Helper()
	got := make([]findingKey, 0, len(report.Findings))
	for _, finding := range report.Findings {
		got = append(got, findingKey{
			Check:        finding.Check,
			Path:         finding.Path,
			Line:         finding.Line,
			GUID:         finding.GUID,
			RelatedPaths: finding.RelatedPaths,
		})
	}
	if want == nil {
		want = []findingKey{}
	}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("findings = %+v, want %+v", got, want)
	}
}

// assertMessagesAt checks the messages of the listed findings with the given check and path, in
// report order, against want.
func assertMessagesAt(t *testing.T, report projectverify.Report, check string, path string, want ...string) {
	t.Helper()
	got := []string{}
	for _, finding := range report.Findings {
		if finding.Check == check && finding.Path == path {
			got = append(got, finding.Message)
		}
	}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("messages of %s at %s = %q, want %q", check, path, got, want)
	}
}

// assertManifestMessages checks the messages of the MANIFEST_INVALID findings, in report order.
func assertManifestMessages(t *testing.T, report projectverify.Report, want ...string) {
	t.Helper()
	assertMessagesAt(t, report, projectverify.CheckManifestInvalid, "Packages/manifest.json", want...)
}

// assertScannedRoots checks the report's ScannedRoots against want, in order.
func assertScannedRoots(t *testing.T, report projectverify.Report, want ...string) {
	t.Helper()
	if !reflect.DeepEqual(report.ScannedRoots, want) {
		t.Fatalf("ScannedRoots = %q, want %q", report.ScannedRoots, want)
	}
}

// runProject runs the check and fails the test when it cannot run.
func runProject(t *testing.T, root string) projectverify.Report {
	t.Helper()
	report, err := projectverify.Run(root)
	if err != nil {
		t.Fatalf("Run(%s) failed: %v", root, err)
	}
	return report
}

// assertRunFails checks that Run stops with an error that names wantInError and returns no report.
func assertRunFails(t *testing.T, root string, wantInError string) {
	t.Helper()
	report, err := projectverify.Run(root)
	if err == nil {
		t.Fatalf("Run(%s) succeeded with %+v, want an error", root, report)
	}
	if !strings.Contains(err.Error(), wantInError) {
		t.Fatalf("Run(%s) error = %q, want it to contain %q", root, err.Error(), wantInError)
	}
	if !reflect.DeepEqual(report, projectverify.Report{}) {
		t.Fatalf("Run(%s) returned a report with its error: %+v", root, report)
	}
}
