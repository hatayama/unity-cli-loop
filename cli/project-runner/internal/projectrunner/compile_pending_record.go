package projectrunner

import (
	"encoding/json"
	"os"
	"path/filepath"
	"time"

	"github.com/hatayama/unity-cli-loop/common/tools"
)

const (
	compilePendingRecordFileName = "pending-compile-request.json"
	// Why match C# CompileResultLifetime (20m): a timed-out CLI can only recover the
	// Unity-stored result while that retention window still covers TimedOutAtUtc.
	compilePendingRecordLifetime = 20 * time.Minute
)

// compilePendingRecord remembers which compile a COMPILE_WAIT_TIMEOUT left in flight
// so a later uloop compile can reattach instead of starting a new request.
type compilePendingRecord struct {
	RequestID     string    `json:"requestId"`
	TimedOutAtUtc time.Time `json:"timedOutAtUtc"`
}

func compilePendingRecordPath(projectRoot string) string {
	return filepath.Join(projectRoot, tools.CacheDirectoryName, compilePendingRecordFileName)
}

func writeCompilePendingRecord(projectRoot string, record compilePendingRecord) error {
	if record.RequestID == "" || record.TimedOutAtUtc.IsZero() {
		return os.ErrInvalid
	}

	directory := filepath.Join(projectRoot, tools.CacheDirectoryName)
	if err := os.MkdirAll(directory, 0o755); err != nil {
		return err
	}

	payload, err := json.Marshal(record)
	if err != nil {
		return err
	}
	// Why UTF-8 without BOM: Windows readers must see the same bytes as macOS/Linux.
	return os.WriteFile(compilePendingRecordPath(projectRoot), payload, 0o644)
}

func readCompilePendingRecord(projectRoot string) (compilePendingRecord, bool) {
	path := compilePendingRecordPath(projectRoot)
	content, err := os.ReadFile(path)
	if err != nil {
		return compilePendingRecord{}, false
	}

	var record compilePendingRecord
	if json.Unmarshal(content, &record) != nil || record.RequestID == "" || record.TimedOutAtUtc.IsZero() {
		_ = os.Remove(path)
		return compilePendingRecord{}, false
	}

	if time.Since(record.TimedOutAtUtc) > compilePendingRecordLifetime {
		_ = os.Remove(path)
		return compilePendingRecord{}, false
	}

	return record, true
}

func clearCompilePendingRecord(projectRoot string) {
	_ = os.Remove(compilePendingRecordPath(projectRoot))
}
