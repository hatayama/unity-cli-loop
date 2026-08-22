package dispatcher

// --output-dir mode syncs skills flat into a caller-chosen directory (for example an
// external skill-package store such as APM). Unlike target installs, the
// destination may hold files uloop does not own (package manifests placed next
// to SKILL.md), so every operation here touches only entries that exist in the
// skill source directory.
//
// Deliberate omissions compared to target installs: disabled-tool filtering and
// deprecated-skill cleanup do not run here. An external store is not scoped to
// one Unity project, so one project's tool settings must not hide skills from
// it, and deleting directories whose names uloop merely used in the past would
// break the guarantee that only source-owned entries are ever removed. The same
// trade-off applies within a skill: ownership derives from the current source
// and no manifest is written into the store, so a top-level entry that a newer
// skill version dropped or renamed is left behind rather than cleaned up.

import (
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/skillscan"
)

func runSkillsDirInstall(absDir string, skills []skillDefinition, stdout io.Writer, stderr io.Writer) int {
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Installing uloop skills (directory)...")
	clicore.WriteLine(stdout, "")
	result := skillInstallResult{}
	for _, skill := range skills {
		if err := installSkillIntoDir(absDir, skill, &result); err != nil {
			clierrors.WriteClassifiedError(stderr, err, skillsDirErrorContext())
			return 1
		}
	}
	clicore.WriteLine(stdout, "Directory:")
	clicore.WriteFormat(stdout, "  Installed: %d\n", result.installed)
	clicore.WriteFormat(stdout, "  Updated: %d\n", result.updated)
	clicore.WriteFormat(stdout, "  Skipped: %d\n", result.skipped)
	clicore.WriteFormat(stdout, "  Location: %s\n\n", absDir)
	return 0
}

func runSkillsDirUninstall(absDir string, skills []skillDefinition, stdout io.Writer, stderr io.Writer) int {
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Uninstalling uloop skills (directory)...")
	clicore.WriteLine(stdout, "")
	removed := 0
	notFound := 0
	for _, skill := range skills {
		wasInstalled, err := uninstallSkillFromDir(absDir, skill)
		if err != nil {
			clierrors.WriteClassifiedError(stderr, err, skillsDirErrorContext())
			return 1
		}
		if wasInstalled {
			removed++
			continue
		}
		notFound++
	}
	clicore.WriteLine(stdout, "Directory:")
	clicore.WriteFormat(stdout, "  Removed: %d\n", removed)
	clicore.WriteFormat(stdout, "  Not found: %d\n", notFound)
	clicore.WriteFormat(stdout, "  Location: %s\n\n", absDir)
	return 0
}

func runSkillsDirList(absDir string, skills []skillDefinition, stdout io.Writer, stderr io.Writer) int {
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "uloop Skills Status:")
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Directory:")
	clicore.WriteFormat(stdout, "Location: %s\n", absDir)
	clicore.WriteLine(stdout, strings.Repeat("=", 50))
	for _, skill := range skills {
		status, err := getDirSkillStatus(absDir, skill)
		if err != nil {
			clierrors.WriteClassifiedError(stderr, err, skillsDirErrorContext())
			return 1
		}
		clicore.WriteFormat(stdout, "  %s %s (%s)\n", statusIcon(status), skill.name, statusText(status))
	}
	clicore.WriteLine(stdout, "")
	clicore.WriteFormat(stdout, "Total: %d skills\n", len(skills))
	return 0
}

func installSkillIntoDir(baseDir string, skill skillDefinition, result *skillInstallResult) error {
	// Status first: its Lstat guard rejects a symlink or file occupying the
	// skill's name before artifact cleanup runs, whose ReadDir would follow the
	// symlink and delete matching entries outside the store (and surface a raw
	// platform-divergent error for a plain file).
	status, err := getDirSkillStatus(baseDir, skill)
	if err != nil {
		return err
	}
	ownedNames, err := sourceOwnedEntryNames(skill.sourceDirectory)
	if err != nil {
		return err
	}
	if _, err := removeStaleSyncArtifacts(filepath.Join(baseDir, skill.name), ownedNames); err != nil {
		return err
	}
	if status == "installed" {
		result.skipped++
		return nil
	}
	if err := syncSkillDirectoryPreservingForeignFiles(skill.sourceDirectory, filepath.Join(baseDir, skill.name)); err != nil {
		return err
	}
	if status == "outdated" {
		result.updated++
		return nil
	}
	result.installed++
	return nil
}

// uninstallSkillFromDir deletes only entries that exist in the skill source, so
// foreign files survive; the skill directory itself is removed only once empty.
// Presence is keyed on the owned entries rather than SKILL.md alone, so a
// partially removed install (references/ left behind without SKILL.md) is still
// cleaned up instead of being reported as not installed.
func uninstallSkillFromDir(baseDir string, skill skillDefinition) (bool, error) {
	skillDir := filepath.Join(baseDir, skill.name)
	// Lstat, not Stat: a symlink occupying the skill's name must not be
	// followed, or the removals below would destroy data outside the store.
	info, err := os.Lstat(skillDir)
	if err != nil {
		if os.IsNotExist(err) {
			return false, nil
		}
		return false, err
	}
	// A foreign top-level file or symlink occupying the skill's name is not an
	// install of ours, so it is preserved and the skill reported as not found.
	if !info.IsDir() {
		return false, nil
	}
	entryNames, err := sourceOwnedEntryNames(skill.sourceDirectory)
	if err != nil {
		return false, err
	}
	artifactsRemoved, err := removeStaleSyncArtifacts(skillDir, entryNames)
	if err != nil {
		return false, err
	}
	removedAny := false
	for _, entryName := range entryNames {
		entryPath := filepath.Join(skillDir, entryName)
		// Lstat, not Stat: a dangling symlink at an owned entry name must still
		// be detected and removed instead of being skipped as missing.
		if _, err := os.Lstat(entryPath); err != nil {
			if os.IsNotExist(err) {
				continue
			}
			return false, err
		}
		if err := os.RemoveAll(entryPath); err != nil {
			return false, err
		}
		removedAny = true
	}
	// Nothing of ours was found: leave the directory alone, even when empty —
	// a user-created scaffold directory bearing a skill name is foreign.
	if !removedAny && !artifactsRemoved {
		return false, nil
	}
	return removedAny, removeEmptyDir(skillDir)
}

// getDirSkillStatus reports install status for the --output-dir layout. It compares
// only source-owned paths, so foreign files kept next to SKILL.md never mark
// the skill outdated; files inside source-owned directories (references/) are
// compared both ways because syncing replaces those directories wholly.
func getDirSkillStatus(baseDir string, skill skillDefinition) (string, error) {
	skillDir := filepath.Join(baseDir, skill.name)
	// Lstat, not Stat: a symlink occupying the skill's name must not be
	// followed, or install would write through it into a directory outside the
	// store that uninstall would then delete from.
	info, err := os.Lstat(skillDir)
	if err != nil {
		if os.IsNotExist(err) {
			return "not_installed", nil
		}
		return "", err
	}
	// Checked explicitly because ReadFile below a file path fails with ENOTDIR
	// on Unix but maps to IsNotExist on Windows — the platforms would otherwise
	// diverge between an aborted run and a bogus install attempt.
	if !info.IsDir() {
		return "", fmt.Errorf("cannot manage skill %q: %s exists but is not a directory", skill.name, skillDir)
	}
	matches, err := installedSkillFileMatches(skillDir, skill)
	if err != nil {
		if os.IsNotExist(err) {
			return dirSkillStatusWithoutSkillFile(skillDir, skill)
		}
		// Wrapped so an unreadable SKILL.md (permissions, or a directory with
		// that name) surfaces with the affected skill instead of a bare OS
		// error; dir mode surfaces such states rather than rewriting an
		// external store whose files cannot even be read.
		return "", fmt.Errorf("cannot read %s for skill %q: %w", skillscan.SkillFileName, skill.name, err)
	}
	if !matches {
		return "outdated", nil
	}
	filesOutdated, err := dirSkillFilesOutdated(skillDir, skill)
	if err != nil {
		return "", err
	}
	if filesOutdated {
		return "outdated", nil
	}
	return "installed", nil
}

// dirSkillStatusWithoutSkillFile classifies a skill directory whose SKILL.md is
// missing. Owned entries left behind mean a partial install worth repairing, so
// the state reads as outdated; list, install, and uninstall then agree on it
// instead of reporting not-installed, installed, and removed respectively.
func dirSkillStatusWithoutSkillFile(skillDir string, skill skillDefinition) (string, error) {
	entryNames, err := sourceOwnedEntryNames(skill.sourceDirectory)
	if err != nil {
		return "", err
	}
	for _, entryName := range entryNames {
		if _, err := os.Lstat(filepath.Join(skillDir, entryName)); err != nil {
			if os.IsNotExist(err) {
				continue
			}
			return "", err
		}
		return "outdated", nil
	}
	return "not_installed", nil
}

// dirSkillFilesOutdated reports whether the installed copies of source-owned
// files differ from the source. Extra installed files count as stale only when
// they sit inside a source-owned directory; top-level foreign files are the
// mode's design point and never mark the skill outdated.
func dirSkillFilesOutdated(skillDir string, skill skillDefinition) (bool, error) {
	expectedFiles := collectComparableSkillFiles(skill.sourceDirectory)
	installedFiles := collectComparableSkillFiles(skillDir)
	if !comparableFilesMatch(expectedFiles, installedFiles) {
		return true, nil
	}
	ownedEntries, err := sourceOwnedEntries(skill.sourceDirectory)
	if err != nil {
		return false, err
	}
	ownedDirs := map[string]bool{}
	for _, entry := range ownedEntries {
		if entry.IsDir() {
			ownedDirs[entry.Name()] = true
		}
	}
	for relativePath := range installedFiles {
		// Only files inside source-owned directories count as stale: a
		// top-level foreign file's own name never appears in ownedDirs, and a
		// file shadowing an owned directory name is already reported outdated
		// by the expected-files comparison above.
		if !ownedDirs[topLevelPathSegment(relativePath)] {
			continue
		}
		if _, ok := expectedFiles[relativePath]; !ok {
			return true, nil
		}
	}
	return false, nil
}

// syncSkillDirectoryPreservingForeignFiles copies every source-owned entry into
// destinationDir while leaving unknown files untouched. Source-owned
// directories are replaced wholly so stale files inside them do not linger.
// SKILL.md is written last: it is what the status check keys on, so a sync that
// fails partway leaves the old SKILL.md in place and the skill keeps reporting
// outdated instead of pairing new metadata with old files.
func syncSkillDirectoryPreservingForeignFiles(sourceDir string, destinationDir string) error {
	if err := os.MkdirAll(destinationDir, 0o755); err != nil {
		return err
	}
	entries, err := sourceOwnedEntries(sourceDir)
	if err != nil {
		return err
	}
	var skillFileEntry os.DirEntry
	for _, entry := range entries {
		if !entry.IsDir() && entry.Name() == skillscan.SkillFileName {
			skillFileEntry = entry
			continue
		}
		if err := syncSkillEntry(sourceDir, destinationDir, entry); err != nil {
			return err
		}
	}
	if skillFileEntry == nil {
		return nil
	}
	return syncSkillEntry(sourceDir, destinationDir, skillFileEntry)
}

func syncSkillEntry(sourceDir string, destinationDir string, entry os.DirEntry) error {
	sourcePath := filepath.Join(sourceDir, entry.Name())
	destinationPath := filepath.Join(destinationDir, entry.Name())
	if err := removeEntryOfWrongType(destinationPath, entry.IsDir()); err != nil {
		return err
	}
	if entry.IsDir() {
		// Replace through a temp copy plus rename so a mid-copy failure (disk
		// full, permission) cannot leave the installed directory half-deleted.
		return syncSkillDirectory(sourcePath, destinationPath)
	}
	content, err := os.ReadFile(sourcePath)
	if err != nil {
		return err
	}
	content = normalizeSkillFileContent(entry.Name(), content)
	return writeSkillFileAtomically(destinationPath, content)
}

// removeEntryOfWrongType clears a destination entry whose type does not match
// the source-owned entry about to be written. Ownership is name-scoped, so
// whatever occupies an owned name is uloop's to replace — but the replace paths
// cannot do it themselves: replaceSkillDirectory resolves the occupant with
// os.Stat, which misclassifies a dangling symlink as absent and then fails the
// rename with a raw ENOTDIR (and would follow a live symlink), and a plain
// rename over a directory fails the same way for file entries.
func removeEntryOfWrongType(destinationPath string, wantDir bool) error {
	// Lstat, not Stat: the occupant itself is what must be examined and
	// removed; a symlink must never be followed into data outside the store.
	info, err := os.Lstat(destinationPath)
	if err != nil {
		if os.IsNotExist(err) {
			return nil
		}
		return err
	}
	if info.IsDir() == wantDir {
		return nil
	}
	if info.IsDir() {
		return os.RemoveAll(destinationPath)
	}
	return os.Remove(destinationPath)
}

// writeSkillFileAtomically writes through a temp file plus rename so an
// interrupted write cannot leave a truncated file at the destination.
func writeSkillFileAtomically(destinationPath string, content []byte) error {
	tempFile, err := os.CreateTemp(filepath.Dir(destinationPath), filepath.Base(destinationPath)+skillSyncTempSuffix)
	if err != nil {
		return err
	}
	tempPath := tempFile.Name()
	renamed := false
	defer func() {
		_ = tempFile.Close()
		if !renamed {
			_ = os.Remove(tempPath)
		}
	}()
	if _, err := tempFile.Write(content); err != nil {
		return err
	}
	if err := tempFile.Chmod(0o644); err != nil {
		return err
	}
	if err := tempFile.Close(); err != nil {
		return err
	}
	if err := os.Rename(tempPath, destinationPath); err != nil {
		return err
	}
	renamed = true
	return nil
}

// sourceOwnedEntries lists the top-level entries of a skill source after the
// skip rules — exactly the set of entries uloop owns inside an installed skill
// directory. Every consumer of the ownership rule derives from this one
// listing so status, sync, and uninstall cannot disagree on what is owned.
func sourceOwnedEntries(sourceDir string) ([]os.DirEntry, error) {
	entries, err := os.ReadDir(sourceDir)
	if err != nil {
		return nil, err
	}
	owned := []os.DirEntry{}
	for _, entry := range entries {
		if shouldSkipSkillFile(entry.Name()) {
			continue
		}
		owned = append(owned, entry)
	}
	return owned, nil
}

func sourceOwnedEntryNames(sourceDir string) ([]string, error) {
	entries, err := sourceOwnedEntries(sourceDir)
	if err != nil {
		return nil, err
	}
	names := []string{}
	for _, entry := range entries {
		names = append(names, entry.Name())
	}
	return names, nil
}

// removeStaleSyncArtifacts deletes leftover temp and backup entries that an
// interrupted sync can leave behind (SKILL.md.uloop-tmp-*, ...). They carry
// the uloop namespace marker, so they are uloop's own artifacts by
// construction and removing them keeps the foreign-file guarantee intact;
// left alone they would be treated as foreign forever and keep the skill
// directory from ever being removed on uninstall.
func removeStaleSyncArtifacts(skillDir string, ownedNames []string) (bool, error) {
	entries, err := os.ReadDir(skillDir)
	if err != nil {
		if os.IsNotExist(err) {
			return false, nil
		}
		return false, err
	}
	removedAny := false
	for _, entry := range entries {
		if !isStaleSyncArtifactName(entry.Name(), ownedNames) {
			continue
		}
		if err := os.RemoveAll(filepath.Join(skillDir, entry.Name())); err != nil {
			return false, err
		}
		removedAny = true
	}
	return removedAny, nil
}

// isStaleSyncArtifactName matches the namespaced names the sync helpers mint
// for temp and backup copies of source-owned entries. The uloop marker in the
// suffix constants is what makes name-based matching safe: a user file would
// have to deliberately adopt uloop's namespace to be captured.
func isStaleSyncArtifactName(name string, ownedNames []string) bool {
	for _, ownedName := range ownedNames {
		if strings.HasPrefix(name, ownedName+skillSyncTempSuffix) ||
			strings.HasPrefix(name, ownedName+skillSyncBackupSuffix) {
			return true
		}
	}
	return false
}

func topLevelPathSegment(relativePath string) string {
	separatorIndex := strings.IndexRune(relativePath, filepath.Separator)
	if separatorIndex < 0 {
		return relativePath
	}
	return relativePath[:separatorIndex]
}

func skillsDirErrorContext() clierrors.ErrorContext {
	return clierrors.ErrorContext{Command: clicore.SkillsCommandName}
}
