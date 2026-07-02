package clicore

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
)

const (
	CLIVibeLogDirectory = ".uloop/outputs/VibeLogs"
	CLIVibeLogPrefix    = "cli_vibe"
	CLIVibeLogEnvName   = "ULOOP_DEBUG"

	cliProjectIdentityHashLength = 16
)

type CLIVibeLogEntry struct {
	Timestamp     string         `json:"timestamp"`
	Level         string         `json:"level"`
	Operation     string         `json:"operation"`
	Message       string         `json:"message"`
	Context       map[string]any `json:"context"`
	CorrelationID string         `json:"correlation_id"`
	Source        string         `json:"source"`
	HumanNote     string         `json:"human_note"`
	AITodo        string         `json:"ai_todo"`
	StackTrace    *string        `json:"stack_trace"`
	Environment   map[string]any `json:"environment"`
}

func NewCLIVibeCorrelationID() string {
	return fmt.Sprintf("cli_%d_%d", time.Now().UnixNano(), os.Getpid())
}

func WriteCLIVibeLog(projectRoot string, entry CLIVibeLogEntry) error {
	if !IsCLIVibeLogEnabled() {
		return nil
	}

	if projectRoot == "" {
		return nil
	}

	if entry.Timestamp == "" {
		entry.Timestamp = time.Now().Format("2006-01-02T15:04:05.000-07:00")
	}
	if entry.CorrelationID == "" {
		entry.CorrelationID = NewCLIVibeCorrelationID()
	}
	if entry.Source == "" {
		entry.Source = "CLI"
	}

	logDirectory := filepath.Join(projectRoot, CLIVibeLogDirectory)
	if err := os.MkdirAll(logDirectory, 0o755); err != nil {
		return err
	}

	logPath := filepath.Join(logDirectory, fmt.Sprintf("%s_%s.json", CLIVibeLogPrefix, time.Now().UTC().Format("20060102")))
	file, err := os.OpenFile(logPath, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0o644)
	if err != nil {
		return err
	}
	defer func() {
		_ = file.Close()
	}()

	payload, err := json.Marshal(entry)
	if err != nil {
		return err
	}
	_, err = file.Write(append(payload, '\n'))
	return err
}

func IsCLIVibeLogEnabled() bool {
	value := strings.TrimSpace(os.Getenv(CLIVibeLogEnvName))
	if value == "" || value == "0" {
		return false
	}
	return !strings.EqualFold(value, "false")
}

func ProjectIdentity(projectRoot string) string {
	if projectRoot == "" {
		return ""
	}

	canonicalProjectRoot, err := filepath.EvalSymlinks(projectRoot)
	if err != nil {
		canonicalProjectRoot = projectRoot
	}
	sum := sha256.Sum256([]byte(canonicalProjectRoot))
	return "project_" + hex.EncodeToString(sum[:])[:cliProjectIdentityHashLength]
}
