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
// break the guarantee that only source-owned entries are ever removed.

import (
	"bytes"
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
	status, err := getDirSkillStatus(baseDir, skill)
	if err != nil {
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
	info, err := os.Stat(skillDir)
	if err != nil {
		if os.IsNotExist(err) {
			return false, nil
		}
		return false, err
	}
	// A foreign top-level file occupying the skill's name is not an install of
	// ours, so it is preserved and the skill reported as not found.
	if !info.IsDir() {
		return false, nil
	}
	entryNames, err := sourceOwnedEntryNames(skill.sourceDirectory)
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
	return removedAny, removeEmptyDir(skillDir)
}

// getDirSkillStatus reports install status for the --output-dir layout. It compares
// only source-owned paths, so foreign files kept next to SKILL.md never mark
// the skill outdated; files inside source-owned directories (references/) are
// compared both ways because syncing replaces those directories wholly.
func getDirSkillStatus(baseDir string, skill skillDefinition) (string, error) {
	skillDir := filepath.Join(baseDir, skill.name)
	info, err := os.Stat(skillDir)
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
		return "", err
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

func dirSkillFilesOutdated(skillDir string, skill skillDefinition) (bool, error) {
	expectedFiles := collectComparableSkillFiles(skill.sourceDirectory)
	installedFiles := collectComparableSkillFiles(skillDir)
	for relativePath, expectedContent := range expectedFiles {
		installedContent, ok := installedFiles[relativePath]
		if !ok || !bytes.Equal(expectedContent, installedContent) {
			return true, nil
		}
	}
	ownedDirs, err := sourceOwnedDirNames(skill.sourceDirectory)
	if err != nil {
		return false, err
	}
	for relativePath := range installedFiles {
		topLevel := topLevelPathSegment(relativePath)
		// Top-level files absent from the source are foreign files and stay
		// untouched, so they must not flag the skill as outdated.
		if topLevel == relativePath {
			continue
		}
		if !ownedDirs[topLevel] {
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
	entries, err := os.ReadDir(sourceDir)
	if err != nil {
		return err
	}
	var skillFileEntry os.DirEntry
	for _, entry := range entries {
		if shouldSkipSkillFile(entry.Name()) {
			continue
		}
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

// writeSkillFileAtomically writes through a temp file plus rename so an
// interrupted write cannot leave a truncated file at the destination.
func writeSkillFileAtomically(destinationPath string, content []byte) error {
	tempFile, err := os.CreateTemp(filepath.Dir(destinationPath), filepath.Base(destinationPath)+".tmp-")
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

// sourceOwnedEntryNames lists the top-level entries of a skill source, which is
// exactly the set of paths uloop owns inside an installed skill directory.
func sourceOwnedEntryNames(sourceDir string) ([]string, error) {
	entries, err := os.ReadDir(sourceDir)
	if err != nil {
		return nil, err
	}
	names := []string{}
	for _, entry := range entries {
		if shouldSkipSkillFile(entry.Name()) {
			continue
		}
		names = append(names, entry.Name())
	}
	return names, nil
}

func sourceOwnedDirNames(sourceDir string) (map[string]bool, error) {
	entries, err := os.ReadDir(sourceDir)
	if err != nil {
		return nil, err
	}
	ownedDirs := map[string]bool{}
	for _, entry := range entries {
		if entry.IsDir() && !shouldSkipSkillFile(entry.Name()) {
			ownedDirs[entry.Name()] = true
		}
	}
	return ownedDirs, nil
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
