package projectverify_test

import (
	"path/filepath"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/projectverify"
)

// mixedCaseGUID is a valid GUID with letters, so its upper-cased form differs from it.
const mixedCaseGUID = "abcdef0123456789abcdef0123456789"

// conflictBlock is a file whose lines 2 to 6 are one complete merge conflict block.
const conflictBlock = "line1\n<<<<<<< HEAD\na\n=======\nb\n>>>>>>> branch\n"

// Verifies files and folders with their .meta files pass, without asking for a .meta file of Assets itself.
func TestRunAcceptsAssetsWithMeta(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/A.txt":          "a",
		"Assets/A.txt.meta":     metaText(guidOf(1)),
		"Assets/Sub/":           "",
		"Assets/Sub.meta":       metaText(guidOf(2)),
		"Assets/Sub/B.txt":      "b",
		"Assets/Sub/B.txt.meta": metaText(guidOf(3)),
	})

	report := runProject(t, root)

	assertFindings(t, report)
	if report.MetaFileCount != 3 {
		t.Fatalf("MetaFileCount = %d, want 3", report.MetaFileCount)
	}
}

// Verifies a file without a .meta file is reported with the full message.
func TestRunReportsMissingMeta(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{"Assets/NoMeta.cs": "class NoMeta {}"})

	report := runProject(t, root)

	assertFindings(t, report, metaMissing("Assets/NoMeta.cs"))
	assertMessagesAt(t, report, projectverify.CheckMetaMissing, "Assets/NoMeta.cs",
		"Assets/NoMeta.cs has no .meta file. Unity will create one with a new GUID, which breaks every"+
			" reference to the old one. If the file came from another branch or folder, bring its .meta file"+
			" along; if you just created it, let Unity import it (for example with uloop compile) and commit"+
			" the generated .meta file.")
}

// Verifies an empty folder without a .meta file is reported.
func TestRunReportsFolderWithoutMeta(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{"Assets/Folder/": ""})

	assertFindings(t, runProject(t, root), metaMissing("Assets/Folder"))
}

// Verifies a .meta file without its asset is reported as an orphan only, without reading its GUID.
func TestRunReportsOrphanMetaOnly(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{"Assets/Gone.png.meta": metaText("nothex")})

	report := runProject(t, root)

	assertFindings(t, report, metaOrphan("Assets/Gone.png.meta"))
	assertMessagesAt(t, report, projectverify.CheckMetaOrphan, "Assets/Gone.png.meta",
		"Assets/Gone.png.meta has no matching asset. Unity deletes it on the next import. If the asset was"+
			" moved or renamed, move the .meta file with it to keep the GUID; if the asset was deleted, delete"+
			" this .meta file too. An empty folder that git does not track also leaves its .meta file behind.")
	if report.MetaFileCount != 1 {
		t.Fatalf("MetaFileCount = %d, want 1", report.MetaFileCount)
	}
}

// Verifies the .meta file of a hidden folder is an orphan, since Unity does not import the folder.
func TestRunReportsMetaOfHiddenItemAsOrphan(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/Samples~/Sample.cs": "class Sample {}",
		"Assets/Samples~.meta":      metaText(guidOf(1)),
	})

	assertFindings(t, runProject(t, root), metaOrphan("Assets/Samples~.meta"))
}

// Verifies every kind of name Unity hides is skipped, along with everything inside hidden folders.
func TestRunSkipsUnityHiddenItems(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/.hidden/x.cs": "class X {}",
		"Assets/.DS_Store":    "",
		"Assets/Docs~/y.cs":   "class Y {}",
		"Assets/CVS/z.cs":     "class Z {}",
		"Assets/notes.tmp":    "",
		"Assets/Other.TMP":    "",
		"Assets/.meta":        metaText(guidOf(1)),
	})

	report := runProject(t, root)

	assertFindings(t, report)
	if report.MetaFileCount != 0 {
		t.Fatalf("MetaFileCount = %d, want 0", report.MetaFileCount)
	}
}

// Verifies a folder named like a .tmp file is an asset, since only files with that extension are hidden.
func TestRunTreatsTmpFolderAsAsset(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{"Assets/cache.tmp/": ""})

	assertFindings(t, runProject(t, root), metaMissing("Assets/cache.tmp"))
}

// Verifies an asset and a .meta file whose names differ only in case are not paired.
func TestRunMatchesMetaNamesExactly(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/Foo.png":      "png",
		"Assets/foo.png.meta": metaText(guidOf(1)),
	})

	assertFindings(t, runProject(t, root), metaMissing("Assets/Foo.png"), metaOrphan("Assets/foo.png.meta"))
}

// Verifies symbolic links need a .meta file but are neither followed nor read.
func TestRunDoesNotFollowSymlinks(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{"Assets/Link.txt.meta": metaText(guidOf(1))})
	outside := filepath.Dir(root)
	writeFileAt(t, filepath.Join(outside, "Conflict.txt"), conflictBlock)
	writeFileAt(t, filepath.Join(outside, "LinkedFolder", "Inner.cs"), "class Inner {}")
	makeSymlink(t, filepath.Join(outside, "Conflict.txt"), filepath.Join(root, "Assets", "Link.txt"))
	makeSymlink(t, filepath.Join(outside, "LinkedFolder"), filepath.Join(root, "Assets", "Link2"))

	assertFindings(t, runProject(t, root), metaMissing("Assets/Link2"))
}

// Verifies plugin folders need their own .meta file while nothing inside them is checked or counted,
// whether the files inside have .meta files or not.
func TestRunTreatsPluginFoldersAsSingleAssets(t *testing.T) {
	sharedGUID := guidOf(1)
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/Plugin.bundle/Contents/Info.plist":         "plist",
		"Assets/Plugin.bundle.meta":                        metaText(guidOf(2)),
		"Assets/Lib.ANDROIDLIB/AndroidManifest.xml":        "<manifest />",
		"Assets/Lib.ANDROIDLIB.meta":                       metaText(guidOf(3)),
		"Assets/Other.framework/Headers/Other.h":           "",
		"Assets/WithMetas.bundle.meta":                     metaText(guidOf(4)),
		"Assets/WithMetas.bundle/Contents/":                "",
		"Assets/WithMetas.bundle/Contents.meta":            metaText(sharedGUID),
		"Assets/WithMetas.bundle/Contents/Info.plist":      "plist",
		"Assets/WithMetas.bundle/Contents/Info.plist.meta": metaText(sharedGUID),
		"Assets/Other.txt":                                 "other",
		"Assets/Other.txt.meta":                            metaText(sharedGUID),
	})

	report := runProject(t, root)

	assertFindings(t, report, metaMissing("Assets/Other.framework"))
	if report.MetaFileCount != 4 {
		t.Fatalf("MetaFileCount = %d, want 4", report.MetaFileCount)
	}
}

// Verifies a GUID written in upper-case hexadecimal is valid.
func TestRunAcceptsUppercaseGuid(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/A.txt":      "a",
		"Assets/A.txt.meta": metaText(strings.ToUpper(mixedCaseGUID)),
	})

	assertFindings(t, runProject(t, root))
}

// Verifies each way a guid line can be wrong is reported, including 32 characters that are not
// hexadecimal and a guid line that is not at the start of the line.
func TestRunReportsInvalidGuids(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/NoGuid.txt":        "a",
		"Assets/NoGuid.txt.meta":   "fileFormatVersion: 2\n",
		"Assets/NotHex.txt":        "b",
		"Assets/NotHex.txt.meta":   metaText("nothex" + strings.Repeat("a", 26)),
		"Assets/Short.txt":         "c",
		"Assets/Short.txt.meta":    metaText(strings.Repeat("a", 31)),
		"Assets/Long.txt":          "d",
		"Assets/Long.txt.meta":     metaText(strings.Repeat("a", 33)),
		"Assets/Zero.txt":          "e",
		"Assets/Zero.txt.meta":     metaText(strings.Repeat("0", 32)),
		"Assets/Indented.txt":      "f",
		"Assets/Indented.txt.meta": "fileFormatVersion: 2\n  guid: " + guidOf(1) + "\n",
	})

	report := runProject(t, root)

	assertFindings(t, report,
		guidInvalid("Assets/Indented.txt.meta"),
		guidInvalid("Assets/Long.txt.meta"),
		guidInvalid("Assets/NoGuid.txt.meta"),
		guidInvalid("Assets/NotHex.txt.meta"),
		guidInvalid("Assets/Short.txt.meta"),
		guidInvalid("Assets/Zero.txt.meta"))
	assertMessagesAt(t, report, projectverify.CheckGUIDInvalid, "Assets/NotHex.txt.meta",
		`Assets/NotHex.txt.meta has no valid guid line (expected "guid: " followed by 32 hexadecimal`+
			` characters, not all zero). Unity will assign a new GUID, which breaks references to this asset.`)
}

// Verifies a .meta file with a byte order mark right before its guid line and CRLF line endings is read.
func TestRunReadsMetaWithByteOrderMarkAndCRLF(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/A.txt":      "a",
		"Assets/A.txt.meta": "\xEF\xBB\xBFguid: " + guidOf(1) + "\r\nfileFormatVersion: 2\r\n",
	})

	assertFindings(t, runProject(t, root))
}

// Verifies the first guid line decides, so a later broken one is ignored.
func TestRunUsesFirstGuidLine(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/A.txt":      "a",
		"Assets/A.txt.meta": metaText(guidOf(1)) + "guid: nothex\n",
	})

	assertFindings(t, runProject(t, root))
}

// Verifies two .meta files with one GUID are reported once, under the first path by name.
func TestRunReportsDuplicateGuidOnce(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/B.txt":      "b",
		"Assets/B.txt.meta": metaText(guidOf(1)),
		"Assets/A.txt":      "a",
		"Assets/A.txt.meta": metaText(guidOf(1)),
	})

	report := runProject(t, root)

	assertFindings(t, report, findingKey{
		Check:        projectverify.CheckGUIDDuplicate,
		Path:         "Assets/A.txt.meta",
		GUID:         guidOf(1),
		RelatedPaths: []string{"Assets/B.txt.meta"},
	})
	assertMessagesAt(t, report, projectverify.CheckGUIDDuplicate, "Assets/A.txt.meta",
		"Assets/A.txt.meta shares GUID "+guidOf(1)+" with Assets/B.txt.meta. Unity keeps the GUID for one of"+
			" them and gives the others new GUIDs, so references can end up on the wrong asset. Keep the .meta"+
			" file of the original asset and delete the copies' .meta files so Unity assigns them new GUIDs.")
}

// Verifies one GUID shared across Assets and an embedded package, in different letter case, is one finding.
func TestRunGroupsDuplicateGuidsAcrossRootsAndCase(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/A.txt":      "a",
		"Assets/A.txt.meta": metaText(mixedCaseGUID),
		"Assets/B.txt":      "b",
		"Assets/B.txt.meta": metaText(strings.ToUpper(mixedCaseGUID)),
	})
	writePackage(t, filepath.Join(root, "Packages", "com.example.embedded"), map[string]string{
		"C.txt":      "c",
		"C.txt.meta": metaText(mixedCaseGUID),
	})

	report := runProject(t, root)

	assertFindings(t, report, findingKey{
		Check:        projectverify.CheckGUIDDuplicate,
		Path:         "Assets/A.txt.meta",
		GUID:         mixedCaseGUID,
		RelatedPaths: []string{"Assets/B.txt.meta", "Packages/com.example.embedded/C.txt.meta"},
	})
	assertMessagesAt(t, report, projectverify.CheckGUIDDuplicate, "Assets/A.txt.meta",
		"Assets/A.txt.meta shares GUID "+mixedCaseGUID+" with Assets/B.txt.meta,"+
			" Packages/com.example.embedded/C.txt.meta. Unity keeps the GUID for one of them and gives the"+
			" others new GUIDs, so references can end up on the wrong asset. Keep the .meta file of the"+
			" original asset and delete the copies' .meta files so Unity assigns them new GUIDs.")
}

// Verifies an orphan .meta file does not count toward duplicate GUIDs, since Unity deletes it.
func TestRunIgnoresOrphanMetaInDuplicateCheck(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/A.txt":         "a",
		"Assets/A.txt.meta":    metaText(guidOf(1)),
		"Assets/Gone.txt.meta": metaText(guidOf(1)),
	})

	assertFindings(t, runProject(t, root), metaOrphan("Assets/Gone.txt.meta"))
}

// Verifies a folder that cannot be read stops the run with its path.
func TestRunFailsWhenDirectoryCannotBeRead(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/Locked/A.txt": "a",
		"Assets/Locked.meta":  metaText(guidOf(1)),
	})
	lockDirectory(t, filepath.Join(root, "Assets", "Locked"), 0)

	assertRunFails(t, root, filepath.Join(root, "Assets", "Locked"))
}

// Verifies a linked .meta file pairs with its asset without being read.
func TestRunPairsSymlinkedMetaWithoutReadingIt(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{"Assets/Real.txt": "real"})
	outsideMeta := filepath.Join(filepath.Dir(root), "Outside.meta")
	writeFileAt(t, outsideMeta, metaText("nothex"))
	makeSymlink(t, outsideMeta, filepath.Join(root, "Assets", "Real.txt.meta"))
	makeSymlink(t, outsideMeta, filepath.Join(root, "Assets", "Lost.txt.meta"))

	report := runProject(t, root)

	assertFindings(t, report, metaOrphan("Assets/Lost.txt.meta"))
	if report.MetaFileCount != 2 {
		t.Fatalf("MetaFileCount = %d, want 2", report.MetaFileCount)
	}
}
