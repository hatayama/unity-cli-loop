package dispatcher

// Deletion authorization and cleanup for dir-mode stores. These helpers
// decide what uninstall and interrupted-sync recovery may remove — source-owned
// entries, namespaced sync artifacts, and ignorable OS/tool debris — and why
// a name match alone never authorizes deleting foreign content.

import (
	"os"
	"path/filepath"
	"strings"
)

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
