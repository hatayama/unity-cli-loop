package projectverify

import (
	"bytes"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

const (
	metaFileExtension = ".meta"
	metaGUIDKey       = "guid:"
	guidLength        = 32
)

// scanDirectory pairs every entry of dir with its .meta file and descends into the folders Unity
// imports file by file. display names dir in findings.
func (v *verifier) scanDirectory(dir string, display string) error {
	entries, err := os.ReadDir(dir)
	if err != nil {
		return fmt.Errorf("read %s: %w", dir, err)
	}
	byName := map[string]fs.DirEntry{}
	for _, entry := range entries {
		byName[entry.Name()] = entry
	}

	for _, entry := range entries {
		name := entry.Name()
		if isUnityHiddenName(name, entry.IsDir()) {
			continue
		}
		entryPath := filepath.Join(dir, name)
		entryDisplay := display + "/" + name
		if strings.HasSuffix(name, metaFileExtension) && !entry.IsDir() {
			if err := v.checkMetaFile(entry, entryPath, entryDisplay, byName,
				strings.TrimSuffix(name, metaFileExtension)); err != nil {
				return err
			}
			continue
		}
		if _, ok := byName[name+metaFileExtension]; !ok {
			v.addFinding(CheckMetaMissing, entryDisplay, 0, metaMissingMessage(entryDisplay))
		}
		if err := v.scanAsset(entry, entryPath, entryDisplay); err != nil {
			return err
		}
	}
	return nil
}

// scanAsset looks inside one asset that already has its .meta file checked.
func (v *verifier) scanAsset(entry fs.DirEntry, path string, display string) error {
	switch {
	case scanDescendsInto(entry.Name(), entry.Type()):
		return v.scanDirectory(path, display)
	case entry.Type().IsRegular():
		return v.scanFileForConflicts(path, display)
	default:
		// Links, plugin folders, sockets, and devices have nothing to scan inside.
		return nil
	}
}

// scanDescendsInto reports whether the .meta scan walks into the entry named name with the given
// mode. It is the only place that rule lives: the scan uses it to decide where to go, and the root
// list uses it to decide which nested roots the scan already covers, so the two cannot drift into
// scanning a folder twice or not at all.
func scanDescendsInto(name string, mode fs.FileMode) bool {
	// Unity imports a link itself; following it can leave the project or loop. IsDir is false
	// for a link, so this also stops at linked folders.
	if !mode.IsDir() {
		return false
	}
	// Unity skips hidden folders, and imports plugin folders as one plugin whose files may or may
	// not have .meta files depending on how the plugin was built.
	return !isUnityHiddenName(name, true) && !isOpaquePluginFolder(name)
}

// checkMetaFile pairs one .meta file with its asset, then checks its GUID and contents.
func (v *verifier) checkMetaFile(
	metaEntry fs.DirEntry,
	path string,
	display string,
	byName map[string]fs.DirEntry,
	target string,
) error {
	v.metaFileCount++
	targetEntry, ok := byName[target]
	if !ok || isUnityHiddenName(target, targetEntry.IsDir()) {
		v.addFinding(CheckMetaOrphan, display, 0, metaOrphanMessage(display))
		// Unity deletes an orphan .meta file, so its GUID and contents no longer matter.
		return nil
	}
	// A linked .meta file is not read: reading it would follow the link out of the scanned roots.
	if !metaEntry.Type().IsRegular() {
		return nil
	}

	content, err := os.ReadFile(path)
	if err != nil {
		return fmt.Errorf("read %s: %w", path, err)
	}
	guid, valid := parseMetaGUID(content)
	if valid {
		v.guidPaths[guid] = append(v.guidPaths[guid], display)
	} else {
		v.addFinding(CheckGUIDInvalid, display, 0, guidInvalidMessage(display))
	}

	line, found, err := findConflictBlock(bytes.NewReader(content))
	if err != nil {
		return fmt.Errorf("read %s: %w", path, err)
	}
	if found {
		v.addFinding(CheckConflictMarker, display, line, conflictMarkerMessage(display, line))
	}
	return nil
}

// parseMetaGUID returns the lower-cased value of the first column-0 "guid:" line and whether it
// is a GUID Unity accepts.
func parseMetaGUID(content []byte) (string, bool) {
	text := string(bytes.TrimPrefix(content, []byte(utf8ByteOrderMark)))
	for _, rawLine := range strings.Split(text, "\n") {
		line := strings.TrimSuffix(rawLine, "\r")
		if !strings.HasPrefix(line, metaGUIDKey) {
			continue
		}
		// Only the first guid line counts, the way Unity reads the file.
		value := strings.TrimSpace(strings.TrimPrefix(line, metaGUIDKey))
		return strings.ToLower(value), isValidGUID(value)
	}
	return "", false
}

// isValidGUID reports whether value is 32 hexadecimal characters that are not all zero.
func isValidGUID(value string) bool {
	if len(value) != guidLength {
		return false
	}
	allZero := true
	for _, character := range value {
		if !isHexDigit(character) {
			return false
		}
		if character != '0' {
			allZero = false
		}
	}
	return !allZero
}

func isHexDigit(character rune) bool {
	return ('0' <= character && character <= '9') ||
		('a' <= character && character <= 'f') ||
		('A' <= character && character <= 'F')
}

// isOpaquePluginFolder reports whether Unity imports a folder as one plugin instead of file by file.
func isOpaquePluginFolder(name string) bool {
	extension := filepath.Ext(name)
	for _, pluginExtension := range []string{".bundle", ".framework", ".xcframework", ".plugin", ".androidlib"} {
		if strings.EqualFold(extension, pluginExtension) {
			return true
		}
	}
	return false
}

// addDuplicateGUIDFindings reports each GUID that more than one .meta file declares, once.
func (v *verifier) addDuplicateGUIDFindings() {
	for guid, paths := range v.guidPaths {
		if len(paths) < 2 {
			continue
		}
		sorted := append([]string{}, paths...)
		sort.Strings(sorted)
		v.findings = append(v.findings, Finding{
			Check:        CheckGUIDDuplicate,
			Path:         sorted[0],
			GUID:         guid,
			RelatedPaths: sorted[1:],
			Message:      guidDuplicateMessage(sorted[0], guid, sorted[1:]),
		})
	}
}

func metaMissingMessage(path string) string {
	return path + " has no .meta file. Unity will create one with a new GUID, which breaks every reference " +
		"to the old one. If the file came from another branch or folder, bring its .meta file along; " +
		"if you just created it, let Unity import it (for example with uloop compile) and commit the " +
		"generated .meta file."
}

func metaOrphanMessage(path string) string {
	return path + " has no matching asset. Unity deletes it on the next import. If the asset was moved or " +
		"renamed, move the .meta file with it to keep the GUID; if the asset was deleted, delete this .meta " +
		"file too. An empty folder that git does not track also leaves its .meta file behind."
}

func guidInvalidMessage(path string) string {
	return path + ` has no valid guid line (expected "guid: " followed by 32 hexadecimal characters, ` +
		"not all zero). Unity will assign a new GUID, which breaks references to this asset."
}

func guidDuplicateMessage(path string, guid string, relatedPaths []string) string {
	return fmt.Sprintf("%s shares GUID %s with %s. Unity keeps the GUID for one of them and gives the "+
		"others new GUIDs, so references can end up on the wrong asset. Keep the .meta file of the "+
		"original asset and delete the copies' .meta files so Unity assigns them new GUIDs.",
		path, guid, strings.Join(relatedPaths, ", "))
}
