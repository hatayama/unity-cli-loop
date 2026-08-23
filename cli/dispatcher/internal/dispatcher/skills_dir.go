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
	blockedReasons := []string{}
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
			blockedReasons = append(blockedReasons, conflictReason)
		}
	}
	clicore.WriteLine(stdout, "Directory:")
	clicore.WriteFormat(stdout, "  Installed: %d\n", result.installed)
	clicore.WriteFormat(stdout, "  Updated: %d\n", result.updated)
	clicore.WriteFormat(stdout, "  Skipped: %d\n", result.skipped)
	if len(blockedReasons) > 0 {
		clicore.WriteFormat(stdout, "  Blocked: %d\n", len(blockedReasons))
	}
	clicore.WriteFormat(stdout, "  Location: %s\n\n", absDir)
	if len(blockedReasons) > 0 {
		writeSkillStoreConflictError(stderr, blockedReasons)
		return 1
	}
	return 0
}

// writeSkillStoreConflictError reports blocked skills through the classified
// error envelope every other dispatcher exit-1 path uses, so callers parsing
// stderr as JSON see the reasons instead of bare text.
func writeSkillStoreConflictError(stderr io.Writer, blockedReasons []string) {
	clierrors.WriteErrorEnvelope(stderr, clierrors.CLIError{
		// Declared inline rather than in the shared error-code table: the code
		// is dispatcher-local, and cli/common is a shared release input whose
		// changes require a release-trigger update.
		ErrorCode:   "SKILL_STORE_CONFLICT",
		Phase:       clierrors.ErrorPhaseExecution,
		Message:     strings.Join(blockedReasons, "; "),
		Retryable:   false,
		SafeToRetry: true,
		Command:     clicore.SkillsCommandName,
		NextActions: []string{"Remove or rename the conflicting entries in the store, then rerun the command."},
		Details:     map[string]any{"BlockedCount": len(blockedReasons)},
	})
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
		// The reason is part of the listing: without it the only way to learn
		// why a skill conflicts would be running install, a mutating command.
		if state.conflictReason != "" {
			clicore.WriteFormat(stdout, "      %s\n", state.conflictReason)
		}
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
	ownedEntries, err := sourceOwnedEntries(skill.sourceDirectory)
	if err != nil {
		return "", err
	}
	if _, err := removeStaleSyncArtifacts(filepath.Join(baseDir, skill.name), ownedEntries); err != nil {
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
// nothing but ignorable OS/tool debris remains in it. Removal requires install
// evidence (SKILL.md, or owned content matching the current source): a
// partially removed install (references/ left behind without SKILL.md) is
// cleaned up while its content still matches the current source, whereas a
// hand-authored directory that merely uses a skill's name — possibly in a
// store uloop never installed into — is preserved and reported as not found.
// Names are matched by their exact on-disk spelling, the skill directory
// itself included, so a case-insensitive filesystem cannot make uloop delete a
// user's Uloop-Sample/, skill.md, or References/ as if they were uloop's.
func uninstallSkillFromDir(baseDir string, skill skillDefinition) (bool, error) {
	baseEntries, err := os.ReadDir(baseDir)
	if err != nil {
		if os.IsNotExist(err) {
			return false, nil
		}
		return false, err
	}
	// A missing exact name (a case variant is a different, foreign directory),
	// a foreign file, or a symlink occupying the skill's name is not an
	// install of ours, so it is preserved and the skill reported as not found.
	skillDirEntry := findExactDirEntry(baseEntries, skill.name)
	if skillDirEntry == nil || !skillDirEntry.IsDir() {
		return false, nil
	}
	skillDir := filepath.Join(baseDir, skill.name)
	ownedEntries, err := sourceOwnedEntries(skill.sourceDirectory)
	if err != nil {
		return false, err
	}
	// Artifacts carry the uloop namespace marker, so they are uloop's own
	// debris by construction and are cleaned up regardless of install evidence.
	artifactsRemoved, err := removeStaleSyncArtifacts(skillDir, ownedEntries)
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
		// Without evidence even "ignorable" leftovers may be user data, so the
		// directory goes only when removing uloop's artifacts emptied it.
		return false, removeSkillDirIfEmpty(skillDir)
	}
	removedAny, err := removeOwnedEntries(skillDir, ownedEntries, dirEntries)
	if err != nil {
		return false, err
	}
	if !removedAny && !artifactsRemoved {
		return false, nil
	}
	return removedAny, removeSkillDirWithIgnorableDebris(skillDir, ownedEntries)
}

// removeOwnedEntries deletes the owned entries present in the skill directory
// by exact-name lookup from the ReadDir listing (dangling symlinks included),
// never a path probe the filesystem could case-fold.
func removeOwnedEntries(skillDir string, ownedEntries []os.DirEntry, dirEntries []os.DirEntry) (bool, error) {
	removedAny := false
	for _, owned := range ownedEntries {
		if findExactDirEntry(dirEntries, owned.Name()) == nil {
			continue
		}
		if err := os.RemoveAll(filepath.Join(skillDir, owned.Name())); err != nil {
			return false, err
		}
		removedAny = true
	}
	return removedAny, nil
}

// removeSkillDirWithIgnorableDebris removes an uninstalled skill's directory
// when the only entries left are ignorable OS/tool debris. Leaving them would
// ghost the directory in the store forever — and orphaned .meta files make
// Unity warn — while removing them deletes nothing a skill install could have
// provided. Any other remaining entry is genuinely foreign and keeps the
// directory in place.
func removeSkillDirWithIgnorableDebris(skillDir string, ownedEntries []os.DirEntry) error {
	entries, err := os.ReadDir(skillDir)
	if err != nil {
		if os.IsNotExist(err) {
			return nil
		}
		return err
	}
	for _, entry := range entries {
		if !isIgnorableStoreDebris(entry, ownedEntries) {
			return nil
		}
	}
	return os.RemoveAll(skillDir)
}

// isIgnorableStoreDebris authorizes deleting a leftover entry along with its
// skill directory: OS junk, plus the Unity .meta stubs minted for entries
// uloop itself installed. Only regular files qualify — a directory bearing a
// debris name is user data whose contents uloop never wrote. A user's own
// .meta data file (dataset.meta) keeps the directory alive. Deliberately
// separate from shouldSkipSkillFile even though the patterns overlap: that
// one filters what a sync copies, and widening a copy filter must never
// silently widen what uninstall may delete.
func isIgnorableStoreDebris(entry os.DirEntry, ownedEntries []os.DirEntry) bool {
	if !entry.Type().IsRegular() {
		return false
	}
	name := entry.Name()
	if name == ".DS_Store" || name == "Thumbs.db" || name == "desktop.ini" {
		return true
	}
	stem, found := strings.CutSuffix(name, ".meta")
	if !found {
		return false
	}
	return findExactDirEntry(ownedEntries, stem) != nil
}

// removeSkillDirIfEmpty drops the skill directory only when nothing remains at
// all — the state removing uloop's own sync artifacts leaves behind when they
// were the directory's sole content.
func removeSkillDirIfEmpty(skillDir string) error {
	entries, err := os.ReadDir(skillDir)
	if err != nil {
		if os.IsNotExist(err) {
			return nil
		}
		return err
	}
	if len(entries) > 0 {
		return nil
	}
	return os.Remove(skillDir)
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
// directories that carry install evidence or hold nothing at owned names, so
// the occupant is uloop's to replace — but the replace paths cannot do it
// themselves: replaceSkillDirectory
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
func removeStaleSyncArtifacts(skillDir string, ownedEntries []os.DirEntry) (bool, error) {
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
		// Current source-owned names are live managed content, not leftovers,
		// even when they resemble artifact names.
		if findExactDirEntry(ownedEntries, entry.Name()) != nil {
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
// for temp and backup copies of source-owned entries: the uloop marker
// followed by the digits os.CreateTemp and os.MkdirTemp append. The marker
// claims the namespace and the digit tail keeps a human-named file that
// merely embeds it (my-archive.uloop-tmp-keepme) out of reach. Matching is
// not restricted to currently owned entry names: debris minted for an entry a
// newer skill version dropped or renamed must still be cleaned up.
func isStaleSyncArtifactName(name string) bool {
	return hasSyncArtifactSuffix(name, skillSyncTempSuffix) ||
		hasSyncArtifactSuffix(name, skillSyncBackupSuffix)
}

func hasSyncArtifactSuffix(name string, marker string) bool {
	markerIndex := strings.LastIndex(name, marker)
	if markerIndex < 0 {
		return false
	}
	tail := name[markerIndex+len(marker):]
	if tail == "" {
		return false
	}
	for _, char := range tail {
		if char < '0' || char > '9' {
			return false
		}
	}
	return true
}

func skillsDirErrorContext() clierrors.ErrorContext {
	return clierrors.ErrorContext{Command: clicore.SkillsCommandName}
}
