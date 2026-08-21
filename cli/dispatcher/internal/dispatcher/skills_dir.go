package dispatcher

// --output-dir mode syncs skills flat into a caller-chosen directory (for example an
// external skill-package store such as APM). Unlike target installs, the
// destination may hold files uloop does not own (package manifests placed next
// to SKILL.md), so every operation here touches only entries that exist in the
// skill source directory.

import (
	"bytes"
	"io"
	"os"
	"path/filepath"
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/skillscan"
)

func runSkillsDirInstall(directory string, skills []skillDefinition, stdout io.Writer, stderr io.Writer) int {
	absDir, err := filepath.Abs(directory)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, skillsDirErrorContext())
		return 1
	}
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

func runSkillsDirUninstall(directory string, skills []skillDefinition, stdout io.Writer, stderr io.Writer) int {
	absDir, err := filepath.Abs(directory)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, skillsDirErrorContext())
		return 1
	}
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

func runSkillsDirList(directory string, skills []skillDefinition, stdout io.Writer, stderr io.Writer) int {
	absDir, err := filepath.Abs(directory)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, skillsDirErrorContext())
		return 1
	}
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
func uninstallSkillFromDir(baseDir string, skill skillDefinition) (bool, error) {
	skillDir := filepath.Join(baseDir, skill.name)
	if _, err := os.Stat(filepath.Join(skillDir, skillscan.SkillFileName)); err != nil {
		if os.IsNotExist(err) {
			return false, nil
		}
		return false, err
	}
	entryNames, err := sourceOwnedEntryNames(skill.sourceDirectory)
	if err != nil {
		return false, err
	}
	for _, entryName := range entryNames {
		if err := os.RemoveAll(filepath.Join(skillDir, entryName)); err != nil {
			return false, err
		}
	}
	return true, removeEmptyDir(skillDir)
}

// getDirSkillStatus reports install status for the --output-dir layout. It compares
// only source-owned paths, so foreign files kept next to SKILL.md never mark
// the skill outdated; files inside source-owned directories (references/) are
// compared both ways because syncing replaces those directories wholly.
func getDirSkillStatus(baseDir string, skill skillDefinition) (string, error) {
	skillDir := filepath.Join(baseDir, skill.name)
	installedContent, err := os.ReadFile(filepath.Join(skillDir, skillscan.SkillFileName))
	if err != nil {
		if os.IsNotExist(err) {
			return "not_installed", nil
		}
		return "", err
	}
	installedContent = normalizeSkillFileContent(skillscan.SkillFileName, installedContent)
	expectedContent := normalizeSkillFileContent(skillscan.SkillFileName, skill.content)
	if !bytes.Equal(installedContent, expectedContent) {
		return "outdated", nil
	}
	if dirSkillFilesOutdated(skillDir, skill) {
		return "outdated", nil
	}
	return "installed", nil
}

func dirSkillFilesOutdated(skillDir string, skill skillDefinition) bool {
	expectedFiles := collectComparableSkillFiles(skill.sourceDirectory)
	installedFiles := collectComparableSkillFiles(skillDir)
	for relativePath, expectedContent := range expectedFiles {
		installedContent, ok := installedFiles[relativePath]
		if !ok || !bytes.Equal(expectedContent, installedContent) {
			return true
		}
	}
	ownedDirs := sourceOwnedDirNames(skill.sourceDirectory)
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
			return true
		}
	}
	return false
}

// syncSkillDirectoryPreservingForeignFiles copies every source-owned entry into
// destinationDir while leaving unknown files untouched. Source-owned
// directories are replaced wholly so stale files inside them do not linger.
func syncSkillDirectoryPreservingForeignFiles(sourceDir string, destinationDir string) error {
	if err := os.MkdirAll(destinationDir, 0o755); err != nil {
		return err
	}
	entries, err := os.ReadDir(sourceDir)
	if err != nil {
		return err
	}
	for _, entry := range entries {
		if shouldSkipSkillFile(entry.Name()) {
			continue
		}
		if err := syncSkillEntry(sourceDir, destinationDir, entry); err != nil {
			return err
		}
	}
	return nil
}

func syncSkillEntry(sourceDir string, destinationDir string, entry os.DirEntry) error {
	sourcePath := filepath.Join(sourceDir, entry.Name())
	destinationPath := filepath.Join(destinationDir, entry.Name())
	if entry.IsDir() {
		if err := os.RemoveAll(destinationPath); err != nil {
			return err
		}
		return copySkillDirectory(sourcePath, destinationPath)
	}
	content, err := os.ReadFile(sourcePath)
	if err != nil {
		return err
	}
	content = normalizeSkillFileContent(entry.Name(), content)
	return os.WriteFile(destinationPath, content, 0o644)
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

func sourceOwnedDirNames(sourceDir string) map[string]bool {
	ownedDirs := map[string]bool{}
	entries, err := os.ReadDir(sourceDir)
	if err != nil {
		return ownedDirs
	}
	for _, entry := range entries {
		if entry.IsDir() && !shouldSkipSkillFile(entry.Name()) {
			ownedDirs[entry.Name()] = true
		}
	}
	return ownedDirs
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
