package automation

import (
	"fmt"
	"io"
	"io/fs"
	"os"
	"path/filepath"
	"sort"
)

// MaxSkillFileBytes mirrors Codex's MAX_SKILL_PROMPT_BYTES, the strictest
// skill-injection byte cap in the Codex source (whole file, frontmatter
// included; enforced on its agent-plugin and extension injection paths).
// Released Codex builds have also been observed to silently truncate large
// skill bodies at a token-based limit, so staying under the strictest
// documented cap keeps skills intact across Codex versions and paths.
const MaxSkillFileBytes = 8000

// skillFileRoots are the trees that contain SKILL.md files: the two source
// trees, plus the generated .claude/.agents copies so a stale oversized copy
// fails the same gate as its source.
var skillFileRoots = []string{
	"Packages/src/Editor/FirstPartyTools",
	"Packages/src/Editor/CliOnlyTools~",
	".claude/skills",
	".agents/skills",
}

// SkillSizeFinding is one SKILL.md whose full file size exceeds the byte limit.
type SkillSizeFinding struct {
	Path  string
	Bytes int
}

// SkillSizeCheckOptions configures RunSkillSizeCheck.
type SkillSizeCheckOptions struct {
	Root     string
	MaxBytes int
}

// ScanSkillFileSizes walks the skill roots under root and returns every file
// named SKILL.md whose size in bytes is greater than maxBytes. Paths use
// forward slashes so reports stay stable on Windows.
func ScanSkillFileSizes(root string, maxBytes int) ([]SkillSizeFinding, error) {
	findings := []SkillSizeFinding{}
	scannedRoots := 0
	for _, skillRoot := range skillFileRoots {
		rootFindings, scanned, err := scanSkillRoot(root, skillRoot, maxBytes)
		if err != nil {
			return nil, err
		}
		if scanned {
			scannedRoots++
		}
		findings = append(findings, rootFindings...)
	}
	// A run that scanned nothing must not pass: with the default --root of ".",
	// running from the wrong directory would otherwise report success without
	// having looked at a single SKILL.md.
	if scannedRoots == 0 {
		return nil, fmt.Errorf("no skill roots found under %s; pass --root <repository root>", root)
	}
	sort.Slice(findings, func(left int, right int) bool {
		return findings[left].Path < findings[right].Path
	})
	return findings, nil
}

// RunSkillSizeCheck prints findings and returns the process exit code. Any
// oversized SKILL.md fails the check; there is no warning mode, because an
// oversized skill is silently truncated for Codex agents.
func RunSkillSizeCheck(stdout io.Writer, stderr io.Writer, options SkillSizeCheckOptions) int {
	maxBytes := options.MaxBytes
	if maxBytes <= 0 {
		maxBytes = MaxSkillFileBytes
	}
	findings, err := ScanSkillFileSizes(options.Root, maxBytes)
	if err != nil {
		_, _ = fmt.Fprintln(stderr, "check-skill-size:", err)
		return 1
	}
	_, _ = fmt.Fprintf(stdout, "=== SKILL.md size (bytes, max %d) ===\n", maxBytes)
	if len(findings) == 0 {
		_, _ = fmt.Fprintln(stdout, "No SKILL.md exceeded the byte limit.")
		return 0
	}
	for _, finding := range findings {
		_, _ = fmt.Fprintf(stdout, "%s: %d bytes (limit %d)\n", finding.Path, finding.Bytes, maxBytes)
	}
	_, _ = fmt.Fprintf(stdout, "%d SKILL.md files exceeded the %d-byte limit; Codex skill injection can silently truncate oversized files — move detail into references/ files.\n", len(findings), maxBytes)
	return 1
}

func scanSkillRoot(repoRoot string, relativeDir string, maxBytes int) ([]SkillSizeFinding, bool, error) {
	absoluteRoot := filepath.Join(repoRoot, filepath.FromSlash(relativeDir))
	fileInfo, err := os.Stat(absoluteRoot)
	if err != nil {
		if os.IsNotExist(err) {
			return nil, false, nil
		}
		return nil, false, err
	}
	if !fileInfo.IsDir() {
		return nil, false, fmt.Errorf("skill root %s is not a directory", relativeDir)
	}

	findings := []SkillSizeFinding{}
	walkErr := filepath.WalkDir(absoluteRoot, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if entry.IsDir() || entry.Name() != "SKILL.md" {
			return nil
		}
		info, err := entry.Info()
		if err != nil {
			return err
		}
		if info.Size() <= int64(maxBytes) {
			return nil
		}
		relativePath, err := filepath.Rel(repoRoot, path)
		if err != nil {
			return err
		}
		findings = append(findings, SkillSizeFinding{
			Path:  filepath.ToSlash(relativePath),
			Bytes: int(info.Size()),
		})
		return nil
	})
	if walkErr != nil {
		return nil, true, walkErr
	}
	return findings, true, nil
}
