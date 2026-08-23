package dispatcher

// Status classification for --output-dir mode. The rules here decide when a
// destination entry may be replaced or deleted, so they are deliberately
// conservative: uloop acts on a skill directory only when it holds evidence of
// a uloop install (see dirSkillHasInstallEvidence), and every name lookup —
// the skill directory itself included — is exact against ReadDir listings: on
// a case-insensitive filesystem a path probe would resolve a user's
// Uloop-Sample/ or skill.md as uloop's own names and claim foreign content.

import (
	"bytes"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"strings"

	"github.com/hatayama/unity-cli-loop/common/skillscan"
)

// dirSkillState is the per-skill outcome of a dir-mode status check. A
// conflict is a state, not an error: one skill whose name is occupied by
// foreign content must not abort the run for every other skill, so install
// and list report it per skill and keep going.
type dirSkillState struct {
	status         string
	conflictReason string
}

func conflictDirSkillState(reason string) dirSkillState {
	return dirSkillState{status: "conflict", conflictReason: reason}
}

func getDirSkillState(baseDir string, skill skillDefinition) (dirSkillState, error) {
	state, done, err := storeLevelDirSkillState(baseDir, skill)
	if err != nil || done {
		return state, err
	}
	skillDir := filepath.Join(baseDir, skill.name)
	ownedEntries, err := sourceOwnedEntries(skill.sourceDirectory)
	if err != nil {
		return dirSkillState{}, err
	}
	dirEntries, err := os.ReadDir(skillDir)
	if err != nil {
		return dirSkillState{}, err
	}
	if reason := dirSkillCaseConflictReason(skill, skillDir, ownedEntries, dirEntries); reason != "" {
		return conflictDirSkillState(reason), nil
	}
	skillFileEntry := findExactDirEntry(dirEntries, skillscan.SkillFileName)
	if skillFileEntry == nil || !skillFileEntry.Type().IsRegular() {
		return classifyDirSkillWithoutSkillFile(skillDir, skill, ownedEntries, dirEntries)
	}
	matches, err := installedSkillFileMatches(skillDir, skill)
	if err != nil {
		// An unreadable SKILL.md (permissions) is a conflict, not an abort: dir
		// mode surfaces the state per skill rather than rewriting an external
		// store whose files cannot even be read.
		return conflictDirSkillState(fmt.Sprintf("cannot read %s for skill %q: %v", skillscan.SkillFileName, skill.name, err)), nil
	}
	if !matches {
		return dirSkillState{status: "outdated"}, nil
	}
	filesOutdated, err := dirSkillFilesOutdated(skillDir, skill, ownedEntries, dirEntries)
	if err != nil {
		return dirSkillState{}, err
	}
	if filesOutdated {
		return dirSkillState{status: "outdated"}, nil
	}
	return dirSkillState{status: "installed"}, nil
}

// storeLevelDirSkillState classifies what occupies the skill's name in the
// store itself. done=false means a directory exists at the exact name and the
// caller must inspect its contents. The name is resolved from the ReadDir
// listing, not a path probe: on a case-insensitive filesystem Lstat would
// resolve a user's differently-cased directory as the skill's name and adopt
// it, so a case variant without the exact name is a conflict on every
// platform.
func storeLevelDirSkillState(baseDir string, skill skillDefinition) (dirSkillState, bool, error) {
	baseEntries, err := os.ReadDir(baseDir)
	if err != nil {
		if os.IsNotExist(err) {
			return dirSkillState{status: "not_installed"}, true, nil
		}
		return dirSkillState{}, true, err
	}
	skillDirEntry := findExactDirEntry(baseEntries, skill.name)
	if skillDirEntry == nil {
		if variant := findCaseVariantDirEntry(baseEntries, skill.name); variant != nil {
			return conflictDirSkillState(fmt.Sprintf(
				"cannot manage skill %q: the store contains %q, which matches the skill name only by letter case",
				skill.name, variant.Name())), true, nil
		}
		return dirSkillState{status: "not_installed"}, true, nil
	}
	skillDir := filepath.Join(baseDir, skill.name)
	if !skillDirEntry.IsDir() {
		// A symlink gets its own message: "not a directory" would mislead when
		// the link points at one — it is the refusal to follow that matters.
		if skillDirEntry.Type()&os.ModeSymlink != 0 {
			return conflictDirSkillState(fmt.Sprintf("cannot manage skill %q: %s is a symlink, which uloop never follows", skill.name, skillDir)), true, nil
		}
		return conflictDirSkillState(fmt.Sprintf("cannot manage skill %q: %s exists but is not a directory", skill.name, skillDir)), true, nil
	}
	return dirSkillState{}, false, nil
}

// classifyDirSkillWithoutSkillFile classifies a skill directory that has no
// regular SKILL.md at its exact name. Owned entries backed by install evidence
// mean a partial install worth repairing (outdated); owned names occupied by
// content uloop cannot confirm it installed are a conflict — replacing or
// deleting them could destroy user data in a store uloop may never have
// installed into. Accepted limitation of the no-manifest design: an orphan of
// a partial removal whose source has since been updated no longer matches and
// lands here too, so the message tells the user how to resolve either case.
func classifyDirSkillWithoutSkillFile(skillDir string, skill skillDefinition, ownedEntries []os.DirEntry, dirEntries []os.DirEntry) (dirSkillState, error) {
	occupied := ownedNamesPresent(ownedEntries, dirEntries)
	if len(occupied) == 0 {
		return dirSkillState{status: "not_installed"}, nil
	}
	evidence, err := dirSkillHasInstallEvidence(skillDir, skill, ownedEntries, dirEntries)
	if err != nil {
		return dirSkillState{}, err
	}
	if evidence {
		return dirSkillState{status: "outdated"}, nil
	}
	return conflictDirSkillState(fmt.Sprintf(
		"cannot manage skill %q: %s holds entries at source-owned names (%s) that uloop cannot confirm it installed; remove or rename them to let uloop manage this skill",
		skill.name, skillDir, strings.Join(occupied, ", "))), nil
}

// dirSkillHasInstallEvidence reports whether the skill directory shows signs
// of a uloop install: a regular SKILL.md at its exact name, or an owned entry
// whose content equals the current source (the orphan a partial removal
// leaves behind). Name presence alone is never evidence — a hand-authored
// directory that happens to use a skill's name must stay untouched.
func dirSkillHasInstallEvidence(skillDir string, skill skillDefinition, ownedEntries []os.DirEntry, dirEntries []os.DirEntry) (bool, error) {
	for _, owned := range ownedEntries {
		if !owned.IsDir() && owned.Name() == skillscan.SkillFileName {
			installed := findExactDirEntry(dirEntries, owned.Name())
			if installed != nil && installed.Type().IsRegular() {
				return true, nil
			}
			continue
		}
		comparison, err := compareOwnedEntry(skillDir, skill.sourceDirectory, owned, dirEntries)
		if err != nil {
			return false, err
		}
		// Only a full content match proves the entry came from uloop; an
		// empty match (ownedEntryEqualEmpty) proves nothing, and unreadable
		// content is not evidence of anything — the entry stays foreign and
		// therefore untouched.
		if comparison == ownedEntryEqual {
			return true, nil
		}
	}
	return false, nil
}

// dirSkillCaseConflictReason reports a conflict when an entry matches a
// source-owned name only by letter case and the exact name is absent. On a
// case-insensitive filesystem (macOS, Windows) writing the owned name would
// silently replace that foreign entry; the refusal applies on every platform
// so the store behaves the same regardless of where it is synced.
func dirSkillCaseConflictReason(skill skillDefinition, skillDir string, ownedEntries []os.DirEntry, dirEntries []os.DirEntry) string {
	for _, owned := range ownedEntries {
		if findExactDirEntry(dirEntries, owned.Name()) != nil {
			continue
		}
		variant := findCaseVariantDirEntry(dirEntries, owned.Name())
		if variant == nil {
			continue
		}
		return fmt.Sprintf("cannot manage skill %q: %s contains %q, which matches the source-owned %q only by letter case",
			skill.name, skillDir, variant.Name(), owned.Name())
	}
	return ""
}

// dirSkillFilesOutdated compares owned entries against the source. It walks
// only source-owned content, never foreign files: a multi-gigabyte artifact or
// a FIFO placed next to SKILL.md must not slow down or block the check. An
// unreadable or wrong-type entry inside an owned directory marks the skill
// outdated so the next sync repairs it instead of reporting it as installed.
func dirSkillFilesOutdated(skillDir string, skill skillDefinition, ownedEntries []os.DirEntry, dirEntries []os.DirEntry) (bool, error) {
	for _, owned := range ownedEntries {
		if owned.Name() == skillscan.SkillFileName {
			continue
		}
		comparison, err := compareOwnedEntry(skillDir, skill.sourceDirectory, owned, dirEntries)
		if err != nil {
			return false, err
		}
		if comparison != ownedEntryEqual && comparison != ownedEntryEqualEmpty {
			return true, nil
		}
	}
	return false, nil
}

// ownedEntryComparison is the outcome of comparing one installed owned entry
// against its source counterpart.
type ownedEntryComparison int

const (
	// ownedEntryAbsent: no entry at the exact owned name.
	ownedEntryAbsent ownedEntryComparison = iota
	// ownedEntryEqual: the installed content fully matches the source.
	ownedEntryEqual
	// ownedEntryEqualEmpty: both sides hold no comparable files, so the match
	// is vacuous — it must not count as install evidence.
	ownedEntryEqualEmpty
	// ownedEntryDiffers: an entry exists but its type or content differs.
	ownedEntryDiffers
	// ownedEntryUnreadable: the installed side cannot be read or contains a
	// non-regular entry (dangling symlink, FIFO).
	ownedEntryUnreadable
)

// compareOwnedEntry is the single definition of "the installed owned entry
// equals the source entry". Install evidence (may this directory be managed
// at all?) and staleness (does it need a sync?) both derive from it, so the
// two authorizations for destructive replacement and deletion cannot drift
// apart. Source-side read failures are real errors; installed-side failures
// are a state (ownedEntryUnreadable) for the caller to interpret.
func compareOwnedEntry(skillDir string, sourceDir string, owned os.DirEntry, dirEntries []os.DirEntry) (ownedEntryComparison, error) {
	installed := findExactDirEntry(dirEntries, owned.Name())
	if installed == nil {
		return ownedEntryAbsent, nil
	}
	if installed.IsDir() != owned.IsDir() {
		return ownedEntryDiffers, nil
	}
	if !owned.IsDir() {
		return compareOwnedFile(skillDir, sourceDir, owned.Name(), installed)
	}
	expected, err := collectSourceDirFiles(filepath.Join(sourceDir, owned.Name()))
	if err != nil {
		return ownedEntryAbsent, err
	}
	installedFiles, err := collectOwnedDirFiles(filepath.Join(skillDir, owned.Name()))
	if err != nil {
		return ownedEntryUnreadable, nil
	}
	if len(expected) != len(installedFiles) || !comparableFilesMatch(expected, installedFiles) {
		return ownedEntryDiffers, nil
	}
	if len(expected) == 0 {
		return ownedEntryEqualEmpty, nil
	}
	return ownedEntryEqual, nil
}

func compareOwnedFile(skillDir string, sourceDir string, name string, installed os.DirEntry) (ownedEntryComparison, error) {
	if !installed.Type().IsRegular() {
		return ownedEntryDiffers, nil
	}
	sourceContent, err := os.ReadFile(filepath.Join(sourceDir, name))
	if err != nil {
		return ownedEntryAbsent, err
	}
	installedContent, err := os.ReadFile(filepath.Join(skillDir, name))
	if err != nil {
		return ownedEntryUnreadable, nil
	}
	if bytes.Equal(
		normalizeSkillFileContent(name, sourceContent),
		normalizeSkillFileContent(name, installedContent),
	) {
		return ownedEntryEqual, nil
	}
	return ownedEntryDiffers, nil
}

// collectSourceDirFiles gathers comparable files from a source-owned
// directory. Unlike collectOwnedDirFiles it reads through symlinks, because
// copySkillDirectory does the same when installing: a working symlink in the
// source is copied as a regular file, so the comparison must see the same
// content on both sides or the skill would report outdated forever.
func collectSourceDirFiles(root string) (map[string][]byte, error) {
	files := map[string][]byte{}
	err := filepath.WalkDir(root, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if entry.IsDir() || shouldSkipSkillFile(entry.Name()) {
			return nil
		}
		relativePath, err := filepath.Rel(root, path)
		if err != nil {
			return err
		}
		content, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		files[relativePath] = normalizeSkillFileContent(relativePath, content)
		return nil
	})
	if err != nil {
		return nil, err
	}
	return files, nil
}

// collectOwnedDirFiles is the error-surfacing collector for installed content
// uloop owns: inside an owned directory an unreadable file or a non-regular
// entry (dangling symlink, FIFO) is a broken copy the caller must react to,
// not something to silently treat as matching.
func collectOwnedDirFiles(root string) (map[string][]byte, error) {
	files := map[string][]byte{}
	err := filepath.WalkDir(root, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if entry.IsDir() || shouldSkipSkillFile(entry.Name()) {
			return nil
		}
		if !entry.Type().IsRegular() {
			return fmt.Errorf("%s is not a regular file", path)
		}
		relativePath, err := filepath.Rel(root, path)
		if err != nil {
			return err
		}
		content, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		files[relativePath] = normalizeSkillFileContent(relativePath, content)
		return nil
	})
	if err != nil {
		return nil, err
	}
	return files, nil
}

func ownedNamesPresent(ownedEntries []os.DirEntry, dirEntries []os.DirEntry) []string {
	names := []string{}
	for _, owned := range ownedEntries {
		if findExactDirEntry(dirEntries, owned.Name()) != nil {
			names = append(names, owned.Name())
		}
	}
	return names
}

// findExactDirEntry matches by the byte-exact stored name. Probing with Lstat
// would let a case-insensitive filesystem resolve skill.md as SKILL.md and
// claim a foreign file; ReadDir returns true on-disk names, so exact
// comparison stays correct on every filesystem.
func findExactDirEntry(dirEntries []os.DirEntry, name string) os.DirEntry {
	for _, entry := range dirEntries {
		if entry.Name() == name {
			return entry
		}
	}
	return nil
}

func findCaseVariantDirEntry(dirEntries []os.DirEntry, name string) os.DirEntry {
	for _, entry := range dirEntries {
		if entry.Name() != name && strings.EqualFold(entry.Name(), name) {
			return entry
		}
	}
	return nil
}
