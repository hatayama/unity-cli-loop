package dispatcher

// --output-dir mode syncs skills flat into a caller-chosen directory (for example an
// external skill-package store such as APM). Unlike target installs, the
// destination may hold files uloop does not own (package manifests placed next
// to SKILL.md), so every operation here touches only entries that exist in the
// skill source directory — and only inside skill directories that carry
// evidence of a uloop install (see dirSkillHasInstallEvidence): a name match
// alone never authorizes replacing or deleting anything. A skill whose name is
// occupied by content uloop cannot manage is reported as a per-skill conflict
// and the run continues, so one occupied name cannot leave the rest of the
// store half-synced with no summary.
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
	blocked := 0
	for _, skill := range skills {
		conflictReason, err := installSkillIntoDir(absDir, skill, &result)
		if err != nil {
			clierrors.WriteClassifiedError(stderr, err, skillsDirErrorContext())
			return 1
		}
		// A conflict blocks only its own skill: the remaining skills still
		// sync, and the summary below still prints, so an interrupted-looking
		// half-synced store cannot result from one occupied name.
		if conflictReason != "" {
			blocked++
			clicore.WriteLine(stderr, conflictReason)
		}
	}
	clicore.WriteLine(stdout, "Directory:")
	clicore.WriteFormat(stdout, "  Installed: %d\n", result.installed)
	clicore.WriteFormat(stdout, "  Updated: %d\n", result.updated)
	clicore.WriteFormat(stdout, "  Skipped: %d\n", result.skipped)
	if blocked > 0 {
		clicore.WriteFormat(stdout, "  Blocked: %d\n", blocked)
	}
	clicore.WriteFormat(stdout, "  Location: %s\n\n", absDir)
	if blocked > 0 {
		return 1
	}
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
		state, err := getDirSkillState(absDir, skill)
		if err != nil {
			clierrors.WriteClassifiedError(stderr, err, skillsDirErrorContext())
			return 1
		}
		clicore.WriteFormat(stdout, "  %s %s (%s)\n", statusIcon(state.status), skill.name, statusText(state.status))
	}
	clicore.WriteLine(stdout, "")
	clicore.WriteFormat(stdout, "Total: %d skills\n", len(skills))
	return 0
}

// installSkillIntoDir syncs one skill and returns the conflict reason when the
// skill's name is occupied by content uloop cannot manage; the caller reports
// it and continues with the remaining skills.
func installSkillIntoDir(baseDir string, skill skillDefinition, result *skillInstallResult) (string, error) {
	// State first: its Lstat guard classifies a symlink or file occupying the
	// skill's name as a conflict before artifact cleanup runs, whose ReadDir
	// would follow the symlink and delete matching entries outside the store
	// (and surface a raw platform-divergent error for a plain file).
	state, err := getDirSkillState(baseDir, skill)
	if err != nil {
		return "", err
	}
	if state.status == "conflict" {
		return state.conflictReason, nil
	}
	if _, err := removeStaleSyncArtifacts(filepath.Join(baseDir, skill.name)); err != nil {
		return "", err
	}
	if state.status == "installed" {
		result.skipped++
		return "", nil
	}
	if err := syncSkillDirectoryPreservingForeignFiles(skill.sourceDirectory, filepath.Join(baseDir, skill.name)); err != nil {
		return "", err
	}
	if state.status == "outdated" {
		result.updated++
		return "", nil
	}
	result.installed++
	return "", nil
}

// uninstallSkillFromDir deletes only entries that exist in the skill source, so
// foreign files survive; the skill directory itself is removed only once
// nothing but ignorable OS/tool debris (.DS_Store, *.meta) remains in it.
// Removal requires install evidence (SKILL.md, or owned content matching the
// source): a partially removed install (references/ left behind without
// SKILL.md) is still cleaned up, while a hand-authored directory that merely
// uses a skill's name — possibly in a store uloop never installed into — is
// preserved and reported as not found. Entries are matched by their exact
// on-disk names, so a case-insensitive filesystem cannot make uloop delete a
// user's skill.md or References/ as if they were the source-owned entries.
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
	ownedEntries, err := sourceOwnedEntries(skill.sourceDirectory)
	if err != nil {
		return false, err
	}
	// Artifacts carry the uloop namespace marker, so they are uloop's own
	// debris by construction and are cleaned up regardless of install evidence.
	artifactsRemoved, err := removeStaleSyncArtifacts(skillDir)
	if err != nil {
		return false, err
	}
	dirEntries, err := os.ReadDir(skillDir)
	if err != nil {
		return false, err
	}
	evidence, err := dirSkillHasInstallEvidence(skillDir, skill, ownedEntries, dirEntries)
	if err != nil {
		return false, err
	}
	if !evidence {
		if !artifactsRemoved {
			return false, nil
		}
		return false, removeSkillDirWithIgnorableDebris(skillDir)
	}
	removedAny := false
	for _, owned := range ownedEntries {
		// Exact-name lookup from the ReadDir listing (dangling symlinks
		// included), never a path probe the filesystem could case-fold.
		if findExactDirEntry(dirEntries, owned.Name()) == nil {
			continue
		}
		if err := os.RemoveAll(filepath.Join(skillDir, owned.Name())); err != nil {
			return false, err
		}
		removedAny = true
	}
	if !removedAny && !artifactsRemoved {
		return false, nil
	}
	return removedAny, removeSkillDirWithIgnorableDebris(skillDir)
}

// removeSkillDirWithIgnorableDebris removes an uninstalled skill's directory
// when the only entries left are ignorable OS/tool debris. Leaving them would
// ghost the directory in the store forever — and orphaned .meta files make
// Unity warn — while removing them deletes nothing a skill install could have
// provided. Any other remaining entry is genuinely foreign and keeps the
// directory in place.
func removeSkillDirWithIgnorableDebris(skillDir string) error {
	entries, err := os.ReadDir(skillDir)
	if err != nil {
		if os.IsNotExist(err) {
			return nil
		}
		return err
	}
	for _, entry := range entries {
		if !isIgnorableStoreDebris(entry.Name()) {
			return nil
		}
	}
	return os.RemoveAll(skillDir)
}

// isIgnorableStoreDebris authorizes deleting a leftover entry along with its
// skill directory. Deliberately separate from shouldSkipSkillFile even though
// the patterns coincide today: that one filters what a sync copies, and
// widening a copy filter must never silently widen what uninstall may delete.
func isIgnorableStoreDebris(name string) bool {
	return name == ".DS_Store" || strings.HasSuffix(name, ".meta")
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
// the source-owned entry about to be written. Syncs only reach here for skill
// directories with install evidence, so the occupant is uloop's to replace —
// but the replace paths cannot do it themselves: replaceSkillDirectory
// resolves the occupant with os.Stat, which misclassifies a dangling symlink
// as absent and then fails the rename with a raw ENOTDIR (and would follow a
// live symlink), and a plain rename over a directory fails the same way for
// file entries.
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

// removeStaleSyncArtifacts deletes leftover temp and backup entries that an
// interrupted sync can leave behind (SKILL.md.uloop-tmp-*, ...). They carry
// the uloop namespace marker, so they are uloop's own artifacts by
// construction and removing them keeps the foreign-file guarantee intact;
// left alone they would be treated as foreign forever and keep the skill
// directory from ever being removed on uninstall.
func removeStaleSyncArtifacts(skillDir string) (bool, error) {
	entries, err := os.ReadDir(skillDir)
	if err != nil {
		if os.IsNotExist(err) {
			return false, nil
		}
		return false, err
	}
	removedAny := false
	for _, entry := range entries {
		if !isStaleSyncArtifactName(entry.Name()) {
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
// suffix constants is the whole safety argument — a user file would have to
// deliberately adopt uloop's namespace to be captured — so matching is not
// restricted to currently owned entry names: debris minted for an entry a
// newer skill version dropped or renamed must still be cleaned up.
func isStaleSyncArtifactName(name string) bool {
	return strings.Contains(name, skillSyncTempSuffix) ||
		strings.Contains(name, skillSyncBackupSuffix)
}

func skillsDirErrorContext() clierrors.ErrorContext {
	return clierrors.ErrorContext{Command: clicore.SkillsCommandName}
}
