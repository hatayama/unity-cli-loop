package projectrunner

import (
	"os"
	"path/filepath"
	"runtime"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/tools"
)

// Verifies a pending compile record round-trips through the project .uloop path.
func TestWriteReadCompilePendingRecordRoundTrip(t *testing.T) {
	projectRoot := t.TempDir()
	timedOutAt := time.Date(2026, 7, 28, 7, 0, 0, 0, time.UTC)
	record := compilePendingRecord{
		RequestID:     "compile_test_record",
		TimedOutAtUtc: timedOutAt,
	}

	if err := writeCompilePendingRecord(projectRoot, record); err != nil {
		t.Fatalf("writeCompilePendingRecord failed: %v", err)
	}

	path := compilePendingRecordPath(projectRoot)
	if filepath.Base(path) != compilePendingRecordFileName {
		t.Fatalf("unexpected file name: %s", path)
	}
	if filepath.Base(filepath.Dir(path)) != tools.CacheDirectoryName {
		t.Fatalf("record must live under %s: %s", tools.CacheDirectoryName, path)
	}

	got, ok := readCompilePendingRecord(projectRoot)
	if !ok {
		t.Fatal("expected pending record")
	}
	if got.RequestID != record.RequestID {
		t.Fatalf("request id mismatch: %#v", got)
	}
	if !got.TimedOutAtUtc.Equal(timedOutAt) {
		t.Fatalf("timed out at mismatch: %#v", got.TimedOutAtUtc)
	}
}

// Verifies invalid JSON is treated as no record and the corrupt file is deleted.
func TestReadCompilePendingRecordRejectsInvalidJSON(t *testing.T) {
	projectRoot := t.TempDir()
	path := compilePendingRecordPath(projectRoot)
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatalf("mkdir failed: %v", err)
	}
	if err := os.WriteFile(path, []byte(`{"requestId":`), 0o644); err != nil {
		t.Fatalf("write corrupt record failed: %v", err)
	}

	if _, ok := readCompilePendingRecord(projectRoot); ok {
		t.Fatal("invalid JSON must not yield a record")
	}
	if _, err := os.Stat(path); !os.IsNotExist(err) {
		t.Fatalf("invalid JSON file should be deleted: %v", err)
	}
}

// Verifies missing required fields are treated as no record and the file is deleted.
func TestReadCompilePendingRecordRejectsMissingFields(t *testing.T) {
	projectRoot := t.TempDir()
	path := compilePendingRecordPath(projectRoot)
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatalf("mkdir failed: %v", err)
	}
	if err := os.WriteFile(path, []byte(`{"requestId":""}`), 0o644); err != nil {
		t.Fatalf("write incomplete record failed: %v", err)
	}

	if _, ok := readCompilePendingRecord(projectRoot); ok {
		t.Fatal("missing fields must not yield a record")
	}
	if _, err := os.Stat(path); !os.IsNotExist(err) {
		t.Fatalf("incomplete record file should be deleted: %v", err)
	}
}

// Verifies stale records older than CompileResultLifetime are deleted on read.
func TestReadCompilePendingRecordRejectsStaleRecord(t *testing.T) {
	projectRoot := t.TempDir()
	record := compilePendingRecord{
		RequestID:     "compile_stale",
		TimedOutAtUtc: time.Now().UTC().Add(-(compilePendingRecordLifetime + time.Minute)),
	}
	if err := writeCompilePendingRecord(projectRoot, record); err != nil {
		t.Fatalf("writeCompilePendingRecord failed: %v", err)
	}

	if _, ok := readCompilePendingRecord(projectRoot); ok {
		t.Fatal("stale record must not be returned")
	}
	if _, err := os.Stat(compilePendingRecordPath(projectRoot)); !os.IsNotExist(err) {
		t.Fatalf("stale record file should be deleted: %v", err)
	}
}

// Verifies clearCompilePendingRecord removes an existing file and tolerates absence.
func TestClearCompilePendingRecord(t *testing.T) {
	projectRoot := t.TempDir()
	record := compilePendingRecord{
		RequestID:     "compile_clear",
		TimedOutAtUtc: time.Now().UTC(),
	}
	if err := writeCompilePendingRecord(projectRoot, record); err != nil {
		t.Fatalf("writeCompilePendingRecord failed: %v", err)
	}

	clearCompilePendingRecord(projectRoot)
	if _, err := os.Stat(compilePendingRecordPath(projectRoot)); !os.IsNotExist(err) {
		t.Fatalf("record should be cleared: %v", err)
	}

	clearCompilePendingRecord(projectRoot)
}

// Verifies pending-record paths use filepath.Join separators on every OS.
func TestCompilePendingRecordPathUsesOSSeparators(t *testing.T) {
	projectRoot := filepath.FromSlash("/tmp/MyProject")
	path := compilePendingRecordPath(projectRoot)
	expected := filepath.Join(projectRoot, tools.CacheDirectoryName, compilePendingRecordFileName)
	if path != expected {
		t.Fatalf("path mismatch: got %q want %q", path, expected)
	}
	if runtime.GOOS == "windows" {
		if filepath.Separator != '\\' {
			t.Fatal("expected Windows separator")
		}
	}
}
