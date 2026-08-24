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

func skillsDirErrorContext() clierrors.ErrorContext {
	return clierrors.ErrorContext{Command: clicore.SkillsCommandName}
}
