package vibelog

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"reflect"
	"regexp"
	"strconv"
	"strings"
	"testing"
	"time"
)

// Verifies CLI Vibe logs are skipped unless ULOOP_DEBUG is enabled.
func TestWriteCLIVibeLogSkipsWhenDebugDisabled(t *testing.T) {
	t.Setenv(CLIVibeLogEnvName, "")
	projectRoot := t.TempDir()

	err := WriteCLIVibeLog(projectRoot, CLIVibeLogEntry{
		Level:     "INFO",
		Operation: "test_operation",
		Message:   "test message",
	})
	if err != nil {
		t.Fatalf("WriteCLIVibeLog should skip without error: %v", err)
	}
	logFiles, err := filepath.Glob(filepath.Join(projectRoot, CLIVibeLogDirectory, CLIVibeLogPrefix+"_*.json"))
	if err != nil {
		t.Fatalf("failed to glob CLI Vibe logs: %v", err)
	}
	if len(logFiles) != 0 {
		t.Fatalf("expected no CLI Vibe logs, got %d: %#v", len(logFiles), logFiles)
	}
}

// Reads every CLI Vibe log line written under projectRoot and decodes it.
func readCLIVibeLogEntries(t *testing.T, projectRoot string) []CLIVibeLogEntry {
	t.Helper()
	logFiles, err := filepath.Glob(filepath.Join(projectRoot, CLIVibeLogDirectory, CLIVibeLogPrefix+"_*.json"))
	if err != nil {
		t.Fatalf("failed to glob CLI Vibe logs: %v", err)
	}
	if len(logFiles) != 1 {
		t.Fatalf("expected one CLI Vibe log file, got %#v", logFiles)
	}
	if !cliVibeLogFileNamePattern.MatchString(filepath.Base(logFiles[0])) {
		t.Fatalf("unexpected CLI Vibe log file name: %s", filepath.Base(logFiles[0]))
	}
	content, err := os.ReadFile(logFiles[0])
	if err != nil {
		t.Fatalf("failed to read CLI Vibe log: %v", err)
	}
	lines := strings.Split(strings.TrimSuffix(string(content), "\n"), "\n")
	entries := make([]CLIVibeLogEntry, 0, len(lines))
	for _, line := range lines {
		var entry CLIVibeLogEntry
		if err := json.Unmarshal([]byte(line), &entry); err != nil {
			t.Fatalf("CLI Vibe log line is not JSON: %q: %v", line, err)
		}
		entries = append(entries, entry)
	}
	return entries
}

var (
	cliVibeLogFileNamePattern  = regexp.MustCompile(`^cli_vibe_\d{8}\.json$`)
	cliVibeCorrelationIDFormat = regexp.MustCompile(`^cli_\d+_(\d+)$`)
)

// Verifies a correlation ID embeds a nanosecond timestamp and the current process ID.
func TestNewCLIVibeCorrelationIDIncludesProcessID(t *testing.T) {
	id := NewCLIVibeCorrelationID()

	match := cliVibeCorrelationIDFormat.FindStringSubmatch(id)
	if match == nil {
		t.Fatalf("unexpected correlation ID format: %q", id)
	}
	if match[1] != strconv.Itoa(os.Getpid()) {
		t.Fatalf("correlation ID %q does not end with pid %d", id, os.Getpid())
	}
}

// Verifies an enabled log with no project root is a silent no-op.
func TestWriteCLIVibeLogSkipsWithoutProjectRoot(t *testing.T) {
	t.Setenv(CLIVibeLogEnvName, "1")

	if err := WriteCLIVibeLog("", CLIVibeLogEntry{Operation: "test_operation"}); err != nil {
		t.Fatalf("WriteCLIVibeLog without project root should skip without error: %v", err)
	}
}

// Verifies an enabled log fills in timestamp, correlation ID, and source defaults
// and appends one JSON line per entry to the dated log file.
func TestWriteCLIVibeLogAppendsEntriesWithDefaults(t *testing.T) {
	t.Setenv(CLIVibeLogEnvName, "true")
	projectRoot := t.TempDir()

	first := CLIVibeLogEntry{Level: "INFO", Operation: "first_operation", Message: "first"}
	second := CLIVibeLogEntry{
		Timestamp:     "2026-01-02T03:04:05.000+00:00",
		Level:         "WARNING",
		Operation:     "second_operation",
		Message:       "second",
		CorrelationID: "fixed_id",
		Source:        "Runner",
		Context:       map[string]any{"command": "compile"},
	}
	for _, entry := range []CLIVibeLogEntry{first, second} {
		if err := WriteCLIVibeLog(projectRoot, entry); err != nil {
			t.Fatalf("WriteCLIVibeLog failed: %v", err)
		}
	}

	entries := readCLIVibeLogEntries(t, projectRoot)
	if len(entries) != 2 {
		t.Fatalf("expected two appended entries, got %#v", entries)
	}
	assertDefaultedCLIVibeEntry(t, entries[0])
	if !reflect.DeepEqual(entries[1], second) {
		t.Fatalf("explicit fields were not preserved:\n got: %#v\nwant: %#v", entries[1], second)
	}
}

// Asserts the defaulted fields of an entry written without timestamp, correlation ID, or source.
func assertDefaultedCLIVibeEntry(t *testing.T, entry CLIVibeLogEntry) {
	t.Helper()
	if entry.Operation != "first_operation" || entry.Message != "first" || entry.Level != "INFO" {
		t.Fatalf("unexpected entry fields: %#v", entry)
	}
	if _, err := time.Parse("2006-01-02T15:04:05.000-07:00", entry.Timestamp); err != nil {
		t.Fatalf("default timestamp %q does not match the log format: %v", entry.Timestamp, err)
	}
	if !cliVibeCorrelationIDFormat.MatchString(entry.CorrelationID) {
		t.Fatalf("default correlation ID has unexpected format: %q", entry.CorrelationID)
	}
	if entry.Source != "CLI" {
		t.Fatalf("expected default source CLI, got %q", entry.Source)
	}
}

// Verifies a log directory that cannot be created is reported as an error.
func TestWriteCLIVibeLogReportsDirectoryCreationFailure(t *testing.T) {
	t.Setenv(CLIVibeLogEnvName, "1")
	projectRoot := t.TempDir()
	blocker := filepath.Join(projectRoot, ".uloop")
	if err := os.WriteFile(blocker, []byte("not a directory"), 0o600); err != nil {
		t.Fatalf("failed to create blocking file: %v", err)
	}

	err := WriteCLIVibeLog(projectRoot, CLIVibeLogEntry{Operation: "test_operation"})

	assertPathErrorOp(t, err, "mkdir")
}

// Verifies a log path occupied by a directory is reported as an open error.
func TestWriteCLIVibeLogReportsOpenFailure(t *testing.T) {
	t.Setenv(CLIVibeLogEnvName, "1")
	projectRoot := t.TempDir()
	logPath := filepath.Join(projectRoot, CLIVibeLogDirectory, fmt.Sprintf("%s_%s.json", CLIVibeLogPrefix, time.Now().UTC().Format("20060102")))
	if err := os.MkdirAll(logPath, 0o755); err != nil {
		t.Fatalf("failed to create blocking directory: %v", err)
	}

	err := WriteCLIVibeLog(projectRoot, CLIVibeLogEntry{Operation: "test_operation"})

	assertPathErrorOp(t, err, "open")
}

// Verifies an entry whose context cannot be encoded as JSON is reported as an error.
func TestWriteCLIVibeLogReportsMarshalFailure(t *testing.T) {
	t.Setenv(CLIVibeLogEnvName, "1")
	projectRoot := t.TempDir()

	err := WriteCLIVibeLog(projectRoot, CLIVibeLogEntry{Context: map[string]any{"bad": make(chan int)}})

	var unsupportedTypeErr *json.UnsupportedTypeError
	if !errors.As(err, &unsupportedTypeErr) {
		t.Fatalf("expected a JSON unsupported type error, got %v", err)
	}
}

// Verifies ULOOP_DEBUG disables logging for empty, zero, and any-case "false" values
// and enables it for anything else.
func TestIsCLIVibeLogEnabled(t *testing.T) {
	cases := []struct {
		value string
		want  bool
	}{
		{value: "", want: false},
		{value: "  ", want: false},
		{value: "0", want: false},
		{value: "false", want: false},
		{value: "FALSE", want: false},
		{value: " False ", want: false},
		{value: "1", want: true},
		{value: "true", want: true},
	}

	for _, testCase := range cases {
		t.Run(fmt.Sprintf("%q", testCase.value), func(t *testing.T) {
			t.Setenv(CLIVibeLogEnvName, testCase.value)
			if got := IsCLIVibeLogEnabled(); got != testCase.want {
				t.Fatalf("expected %v for %q, got %v", testCase.want, testCase.value, got)
			}
		})
	}
}

// Verifies project identity is empty without a root, hashes the canonical path so
// symlinked roots share an identity, and falls back to the raw path when it does not exist.
func TestProjectIdentity(t *testing.T) {
	if got := ProjectIdentity(""); got != "" {
		t.Fatalf("expected empty identity for empty root, got %q", got)
	}

	realRoot, err := filepath.EvalSymlinks(t.TempDir())
	if err != nil {
		t.Fatalf("failed to resolve temp dir: %v", err)
	}
	linkRoot := filepath.Join(t.TempDir(), "link")
	if err := os.Symlink(realRoot, linkRoot); err != nil {
		t.Skipf("symlinks are not available: %v", err)
	}

	identity := ProjectIdentity(realRoot)
	if identity != expectedProjectIdentity(realRoot) {
		t.Fatalf("unexpected identity %q for %q", identity, realRoot)
	}
	if got := ProjectIdentity(linkRoot); got != identity {
		t.Fatalf("symlinked root identity %q differs from canonical %q", got, identity)
	}
	missingRoot := filepath.Join(realRoot, "missing")
	if got := ProjectIdentity(missingRoot); got != expectedProjectIdentity(missingRoot) {
		t.Fatalf("missing root should hash its raw path, got %q", got)
	}
}

// Computes the identity independently: "project_" plus the first 16 hex chars of SHA-256.
func expectedProjectIdentity(path string) string {
	sum := sha256.Sum256([]byte(path))
	return "project_" + hex.EncodeToString(sum[:])[:16]
}

func assertPathErrorOp(t *testing.T, err error, expectedOp string) {
	t.Helper()
	var pathErr *os.PathError
	if !errors.As(err, &pathErr) || pathErr.Op != expectedOp {
		t.Fatalf("expected a %s path error, got %v", expectedOp, err)
	}
}
