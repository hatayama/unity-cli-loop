//go:build !windows

package dispatcher

import (
	"bytes"
	"path/filepath"
	"strings"
	"syscall"
	"testing"
)

// Tests that dir-mode status never reads foreign file content: a FIFO placed
// next to SKILL.md would block any read forever, so the listing completing at
// all proves foreign bytes are not opened.
func TestRunSkillsDirListIgnoresForeignFifo(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	fifoPath := filepath.Join(destinationDir, "uloop-sample", "queue.pipe")
	if err := syscall.Mkfifo(fifoPath, 0o644); err != nil {
		t.Fatalf("failed to create fifo: %v", err)
	}

	stdout.Reset()
	code := runSkillsDirList(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir list failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "uloop-sample (installed)") {
		t.Fatalf("a foreign FIFO should not change the skill status:\n%s", stdout.String())
	}
}
