package automation

import (
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func writeSkillFile(t *testing.T, root string, relativePath string, size int) {
	t.Helper()
	absolutePath := filepath.Join(root, filepath.FromSlash(relativePath))
	if err := os.MkdirAll(filepath.Dir(absolutePath), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(absolutePath, bytes.Repeat([]byte("a"), size), 0o644); err != nil {
		t.Fatal(err)
	}
}

// ScanSkillFileSizes reports a SKILL.md whose full file size exceeds the byte limit
// and leaves compliant files out of the findings.
func TestScanSkillFileSizesReportsOnlyOversizedSkillFiles(t *testing.T) {
	root := t.TempDir()
	writeSkillFile(t, root, "Packages/src/Editor/FirstPartyTools/Big/Skill/SKILL.md", MaxSkillFileBytes+1)
	writeSkillFile(t, root, "Packages/src/Editor/FirstPartyTools/Small/Skill/SKILL.md", MaxSkillFileBytes)

	findings, err := ScanSkillFileSizes(root, MaxSkillFileBytes)
	if err != nil {
		t.Fatal(err)
	}
	if len(findings) != 1 {
		t.Fatalf("expected exactly one finding, got %d: %v", len(findings), findings)
	}
	if findings[0].Path != "Packages/src/Editor/FirstPartyTools/Big/Skill/SKILL.md" {
		t.Fatalf("unexpected finding path: %s", findings[0].Path)
	}
	if findings[0].Bytes != MaxSkillFileBytes+1 {
		t.Fatalf("unexpected finding size: %d", findings[0].Bytes)
	}
}

// ScanSkillFileSizes covers every skill root — CLI-only skill sources and the
// generated .claude/.agents copies — not just FirstPartyTools.
func TestScanSkillFileSizesCoversAllSkillRoots(t *testing.T) {
	root := t.TempDir()
	// Kept in path-sorted order because findings are reported path-sorted.
	oversized := []string{
		".agents/skills/uloop-pause-point/SKILL.md",
		".claude/skills/uloop-pause-point/SKILL.md",
		"Packages/src/Editor/CliOnlyTools~/PausePoint/Skill/SKILL.md",
	}
	for _, relativePath := range oversized {
		writeSkillFile(t, root, relativePath, MaxSkillFileBytes+100)
	}

	findings, err := ScanSkillFileSizes(root, MaxSkillFileBytes)
	if err != nil {
		t.Fatal(err)
	}
	if len(findings) != len(oversized) {
		t.Fatalf("expected %d findings, got %d: %v", len(oversized), len(findings), findings)
	}
	for index, relativePath := range oversized {
		if findings[index].Path != relativePath {
			t.Fatalf("expected finding %d to be %s, got %s", index, relativePath, findings[index].Path)
		}
	}
}

// ScanSkillFileSizes ignores files that are not named SKILL.md, including
// reference guides living beside a skill.
func TestScanSkillFileSizesIgnoresReferenceFiles(t *testing.T) {
	root := t.TempDir()
	writeSkillFile(t, root, "Packages/src/Editor/FirstPartyTools/Big/Skill/references/guide.md", MaxSkillFileBytes*3)

	findings, err := ScanSkillFileSizes(root, MaxSkillFileBytes)
	if err != nil {
		t.Fatal(err)
	}
	if len(findings) != 0 {
		t.Fatalf("expected no findings, got %v", findings)
	}
}

// RunSkillSizeCheck exits 1 and names each oversized file when any SKILL.md
// exceeds the limit.
func TestRunSkillSizeCheckFailsOnOversizedSkill(t *testing.T) {
	root := t.TempDir()
	writeSkillFile(t, root, "Packages/src/Editor/FirstPartyTools/Big/Skill/SKILL.md", MaxSkillFileBytes+42)

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := RunSkillSizeCheck(&stdout, &stderr, SkillSizeCheckOptions{Root: root})
	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", exitCode)
	}
	if !strings.Contains(stdout.String(), "Packages/src/Editor/FirstPartyTools/Big/Skill/SKILL.md: 8042 bytes (limit 8000)") {
		t.Fatalf("stdout did not name the oversized file: %q", stdout.String())
	}
}

// RunSkillSizeCheck exits 1 when the scanned root contains none of the skill
// roots, so running it from the wrong directory cannot pass as a silent no-op.
func TestRunSkillSizeCheckFailsWhenNoSkillRootExists(t *testing.T) {
	root := t.TempDir()

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := RunSkillSizeCheck(&stdout, &stderr, SkillSizeCheckOptions{Root: root})
	if exitCode != 1 {
		t.Fatalf("expected exit code 1 for a root without skill trees, got %d", exitCode)
	}
	if !strings.Contains(stderr.String(), "no skill roots found") {
		t.Fatalf("stderr did not explain the empty scan: %q", stderr.String())
	}
}

// RunSkillSizeCheck exits 0 when every SKILL.md fits within the limit.
func TestRunSkillSizeCheckPassesWhenAllSkillsFit(t *testing.T) {
	root := t.TempDir()
	writeSkillFile(t, root, "Packages/src/Editor/FirstPartyTools/Small/Skill/SKILL.md", 100)

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := RunSkillSizeCheck(&stdout, &stderr, SkillSizeCheckOptions{Root: root})
	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d (stderr: %s)", exitCode, stderr.String())
	}
}
