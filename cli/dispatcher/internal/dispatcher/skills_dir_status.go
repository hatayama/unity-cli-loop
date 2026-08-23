package dispatcher

// Status classification for --output-dir mode. The rules here decide when a
// destination entry may be replaced or deleted, so they are deliberately
// conservative: uloop acts on a skill directory only when it holds evidence of
// a uloop install (see dirSkillHasInstallEvidence), and every name lookup is
// exact — on a case-insensitive filesystem a user's skill.md or References/
// must never be claimed as the source-owned SKILL.md or references/.

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
	skillDir := filepath.Join(baseDir, skill.name)
	// Lstat, not Stat: a symlink occupying the skill's name must not be
	// followed, or install would write through it into a directory outside the
	// store that uninstall would then delete from.
	info, err := os.Lstat(skillDir)
	if err != nil {
		if os.IsNotExist(err) {
			return dirSkillState{status: "not_installed"}, nil
		}
		return dirSkillState{}, err
	}
	if !info.IsDir() {
		// A symlink gets its own message: "not a directory" would mislead when
		// the link points at one — it is the refusal to follow that matters.
		if info.Mode()&os.ModeSymlink != 0 {
			return conflictDirSkillState(fmt.Sprintf("cannot manage skill %q: %s is a symlink, which uloop never follows", skill.name, skillDir)), nil
		}
		return conflictDirSkillState(fmt.Sprintf("cannot manage skill %q: %s exists but is not a directory", skill.name, skillDir)), nil
	}
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

// classifyDirSkillWithoutSkillFile classifies a skill directory that has no
// regular SKILL.md at its exact name. Owned entries backed by install evidence
// mean a partial install worth repairing (outdated); owned names occupied by
// content uloop never wrote are a conflict — replacing or deleting them would
// destroy user data in a store uloop may never have installed into.
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
		"cannot manage skill %q: %s holds entries at source-owned names (%s) that uloop did not install",
		skill.name, skillDir, strings.Join(occupied, ", "))), nil
}

// dirSkillHasInstallEvidence reports whether the skill directory shows signs
// of a uloop install: a regular SKILL.md at its exact name, or an owned entry
// whose content equals the current source (the orphan a partial removal
// leaves behind). Name presence alone is never evidence — a hand-authored
// directory that happens to use a skill's name must stay untouched.
func dirSkillHasInstallEvidence(skillDir string, skill skillDefinition, ownedEntries []os.DirEntry, dirEntries []os.DirEntry) (bool, error) {
	for _, owned := range ownedEntries {
		installed := findExactDirEntry(dirEntries, owned.Name())
		if installed == nil || installed.IsDir() != owned.IsDir() {
			continue
		}
		if !owned.IsDir() {
			if installed.Type().IsRegular() && owned.Name() == skillscan.SkillFileName {
				return true, nil
			}
			matches, err := installedOwnedFileMatchesSource(skillDir, skill.sourceDirectory, installed)
			if err != nil {
				return false, err
			}
			if matches {
				return true, nil
			}
			continue
		}
		expected, err := collectOwnedDirFiles(filepath.Join(skill.sourceDirectory, owned.Name()))
		if err != nil {
			return false, err
		}
		installedFiles, err := collectOwnedDirFiles(filepath.Join(skillDir, owned.Name()))
		// Unreadable content is not evidence of anything; the entry stays
		// foreign and therefore untouched.
		if err != nil {
			continue
		}
		if len(expected) == len(installedFiles) && comparableFilesMatch(expected, installedFiles) {
			return true, nil
		}
	}
	return false, nil
}

func installedOwnedFileMatchesSource(skillDir string, sourceDir string, installed os.DirEntry) (bool, error) {
	if !installed.Type().IsRegular() {
		return false, nil
	}
	sourceContent, err := os.ReadFile(filepath.Join(sourceDir, installed.Name()))
	if err != nil {
		return false, err
	}
	installedContent, err := os.ReadFile(filepath.Join(skillDir, installed.Name()))
	if err != nil {
		return false, nil
	}
	return bytes.Equal(
		normalizeSkillFileContent(installed.Name(), sourceContent),
		normalizeSkillFileContent(installed.Name(), installedContent),
	), nil
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
		installed := findExactDirEntry(dirEntries, owned.Name())
		if installed == nil || installed.IsDir() != owned.IsDir() {
			return true, nil
		}
		if !owned.IsDir() {
			matches, err := installedOwnedFileMatchesSource(skillDir, skill.sourceDirectory, installed)
			if err != nil {
				return false, err
			}
			if !matches {
				return true, nil
			}
			continue
		}
		expected, err := collectOwnedDirFiles(filepath.Join(skill.sourceDirectory, owned.Name()))
		if err != nil {
			return false, err
		}
		installedFiles, err := collectOwnedDirFiles(filepath.Join(skillDir, owned.Name()))
		if err != nil {
			return true, nil
		}
		if len(expected) != len(installedFiles) || !comparableFilesMatch(expected, installedFiles) {
			return true, nil
		}
	}
	return false, nil
}

// collectOwnedDirFiles is the error-surfacing counterpart of
// collectComparableSkillFiles, for content uloop owns: inside an owned
// directory an unreadable file or a non-regular entry (dangling symlink,
// FIFO) is a broken copy the caller must react to, not something to silently
// treat as matching.
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
