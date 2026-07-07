// Package clitest provides shared test helpers used by the CLI-facing packages
// across the common, dispatcher, and project-runner modules. It must not depend
// on common/clicore, because clicore's own tests need to consume clitest and
// pulling clicore back in would form an import cycle.
package clitest

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"io/fs"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/version"
)

// WriteProjectFile writes content to projectRoot/relativePath, creating the
// parent directory if it does not exist. Test helpers use this to seed
// fixtures inside a t.TempDir() project layout.
func WriteProjectFile(t *testing.T, projectRoot string, relativePath string, content string) {
	t.Helper()

	targetPath := filepath.Join(projectRoot, filepath.FromSlash(relativePath))
	if err := os.MkdirAll(filepath.Dir(targetPath), 0o755); err != nil {
		t.Fatalf("failed to create directory for %s: %v", relativePath, err)
	}
	if err := os.WriteFile(targetPath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write %s: %v", relativePath, err)
	}
}

// WriteSkillFile seeds a skill file at projectRoot/relativeDir/fileName after
// normalizing CRLF line endings in content. The normalization exists so
// Windows checkouts with core.autocrlf do not change the frontmatter parser
// input that the skill loaders see at runtime. fileName is a parameter because
// clitest cannot import common/clicore (that would create an import cycle),
// so each caller supplies its own skill-file-name constant.
func WriteSkillFile(t *testing.T, projectRoot string, relativeDir string, fileName string, content string) {
	t.Helper()

	normalizedContent := strings.ReplaceAll(content, "\r\n", "\n")
	WriteProjectFile(t, projectRoot, filepath.Join(relativeDir, fileName), normalizedContent)
}

// RunVersionJSON invokes a CLI entrypoint with the shared "--version --json"
// flags, asserts exit code 0, and returns the JSON-decoded stdout payload.
// Individual field assertions remain in each caller so that each version
// contract stays visible in the test that owns it.
func RunVersionJSON(t *testing.T, run func(context.Context, []string, io.Writer, io.Writer) int) map[string]any {
	t.Helper()

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := run(context.Background(), []string{"--version", "--json"}, &stdout, &stderr)
	if code != 0 {
		t.Fatalf("version json command failed: code=%d stderr=%s", code, stderr.String())
	}
	payload := map[string]any{}
	if err := json.Unmarshal(stdout.Bytes(), &payload); err != nil {
		t.Fatalf("version json output is not JSON: %v\n%s", err, stdout.String())
	}
	return payload
}

// RequireValidContractVersion asserts that a contract version string is
// non-empty and parses as valid semver.
func RequireValidContractVersion(t *testing.T, label string, value string) {
	t.Helper()

	if value == "" {
		t.Fatalf("%s must not be empty", label)
	}
	if !version.IsValid(value) {
		t.Fatalf("%s must be valid semver: %s", label, value)
	}
}

// RequireContractFieldMap reads fileName from the given embedded filesystem
// and decodes it as a top-level JSON object. Callers pass their own embed.FS
// because the file itself is package-private to whichever module owns it.
func RequireContractFieldMap(t *testing.T, files fs.FS, fileName string) map[string]any {
	t.Helper()

	content, err := fs.ReadFile(files, fileName)
	if err != nil {
		t.Fatalf("failed to read %s: %v", fileName, err)
	}
	fields := map[string]any{}
	if err := json.Unmarshal(content, &fields); err != nil {
		t.Fatalf("%s is invalid JSON: %v", fileName, err)
	}
	return fields
}

// RequireContractFieldMissing asserts that a contract JSON object does not
// declare the given field.
func RequireContractFieldMissing(t *testing.T, fields map[string]any, fieldName string) {
	t.Helper()

	if _, ok := fields[fieldName]; ok {
		t.Fatalf("contract must not declare %s", fieldName)
	}
}
