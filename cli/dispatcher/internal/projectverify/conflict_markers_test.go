package projectverify_test

import (
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/projectverify"
)

// withMetaFiles returns files plus a .meta file with a distinct valid GUID for each of them, so
// the only findings left are the ones a test is about.
func withMetaFiles(files map[string]string) map[string]string {
	withMeta := map[string]string{}
	n := 0
	for path, content := range files {
		withMeta[path] = content
		withMeta[path+".meta"] = metaText(guidOf(n))
		n++
	}
	return withMeta
}

// Verifies a complete conflict block is reported at its opening line with the full message.
func TestRunReportsConflictBlock(t *testing.T) {
	root := writeProjectWithManifest(t, withMetaFiles(map[string]string{"Assets/Conflict.txt": conflictBlock}))

	report := runProject(t, root)

	assertFindings(t, report, conflictMarker("Assets/Conflict.txt", 2))
	assertMessagesAt(t, report, projectverify.CheckConflictMarker, "Assets/Conflict.txt",
		"Assets/Conflict.txt still contains a merge conflict block starting at line 2. Resolve the conflict"+
			" and remove the markers; Unity cannot load or compile a file that contains them.")
}

// Verifies markers that do not form opening, separator, and closing in order are not reported.
func TestRunIgnoresIncompleteConflictMarkers(t *testing.T) {
	root := writeProjectWithManifest(t, withMetaFiles(map[string]string{
		"Assets/OpenOnly.txt":          "a\n<<<<<<< HEAD\nb\n",
		"Assets/OpenAndSeparator.txt":  "<<<<<<< HEAD\na\n=======\nb\n",
		"Assets/SeparatorAndClose.txt": "a\n=======\nb\n>>>>>>> branch\n",
		"Assets/CloseOnly.txt":         "a\n>>>>>>> branch\n",
	}))

	assertFindings(t, runProject(t, root))
}

// Verifies only markers shaped the way git writes them count: seven characters followed by the end
// of the line or a space, at the start of the line, and a separator line with nothing else on it.
func TestRunRecognizesConflictMarkerShapes(t *testing.T) {
	root := writeProjectWithManifest(t, withMetaFiles(map[string]string{
		"Assets/EightOpen.txt":       strings.Replace(conflictBlock, "<<<<<<< HEAD", "<<<<<<<<", 1),
		"Assets/SevenOpen.txt":       strings.Replace(conflictBlock, "<<<<<<< HEAD", "<<<<<<<", 1),
		"Assets/IndentedOpen.txt":    strings.Replace(conflictBlock, "<<<<<<< HEAD", "  <<<<<<< HEAD", 1),
		"Assets/SpacedSeparator.txt": strings.Replace(conflictBlock, "=======", "======= ", 1),
		"Assets/EightSeparator.txt":  strings.Replace(conflictBlock, "=======", "========", 1),
	}))

	assertFindings(t, runProject(t, root), conflictMarker("Assets/SevenOpen.txt", 2))
}

// Verifies a conflict block in a file with CRLF line endings is reported.
func TestRunReportsConflictBlockWithCRLF(t *testing.T) {
	root := writeProjectWithManifest(t, withMetaFiles(map[string]string{
		"Assets/Conflict.txt": strings.ReplaceAll(conflictBlock, "\n", "\r\n"),
	}))

	assertFindings(t, runProject(t, root), conflictMarker("Assets/Conflict.txt", 2))
}

// Verifies a file with a NUL byte near its start is treated as binary and not searched.
func TestRunSkipsBinaryFiles(t *testing.T) {
	root := writeProjectWithManifest(t, withMetaFiles(map[string]string{
		"Assets/Image.png": "\x89PNG\r\n\x1a\n\x00\x00\x00\x0dIHDR\n" + conflictBlock,
	}))

	assertFindings(t, runProject(t, root))
}

// Verifies a line longer than the read buffer counts as one line.
func TestRunCountsLinesAcrossLongLines(t *testing.T) {
	root := writeProjectWithManifest(t, withMetaFiles(map[string]string{
		"Assets/Long.txt": strings.Repeat("x", 100000) + "\n" + conflictBlock,
	}))

	assertFindings(t, runProject(t, root), conflictMarker("Assets/Long.txt", 3))
}

// Verifies an opening marker at the start of a line longer than the read buffer is recognized.
func TestRunRecognizesOpeningMarkerOnLongLine(t *testing.T) {
	root := writeProjectWithManifest(t, withMetaFiles(map[string]string{
		"Assets/Long.txt": "<<<<<<< " + strings.Repeat("x", 100000) + "\n=======\n>>>>>>> b\n",
	}))

	assertFindings(t, runProject(t, root), conflictMarker("Assets/Long.txt", 1))
}

// Verifies only the first conflict block of a file is reported.
func TestRunReportsFirstConflictBlockOnly(t *testing.T) {
	root := writeProjectWithManifest(t, withMetaFiles(map[string]string{
		"Assets/Twice.txt": conflictBlock + "c\nd\ne\n" + "<<<<<<< HEAD\nx\n=======\ny\n>>>>>>> branch\n",
	}))

	assertFindings(t, runProject(t, root), conflictMarker("Assets/Twice.txt", 2))
}

// Verifies a second opening marker before the block closes starts the block again.
func TestRunRestartsConflictBlockAtNewOpeningMarker(t *testing.T) {
	root := writeProjectWithManifest(t, withMetaFiles(map[string]string{
		"Assets/Restart.txt": "line1\n<<<<<<< a\n<<<<<<< b\n=======\n>>>>>>> c\n",
	}))

	assertFindings(t, runProject(t, root), conflictMarker("Assets/Restart.txt", 3))
}

// Verifies a conflict block inside a .meta file is reported under the .meta file's path.
func TestRunReportsConflictBlockInMeta(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"Assets/A.txt":      "a",
		"Assets/A.txt.meta": metaText(guidOf(1)) + "<<<<<<< HEAD\nx\n=======\ny\n>>>>>>> branch\n",
	})

	assertFindings(t, runProject(t, root), conflictMarker("Assets/A.txt.meta", 3))
}

// Verifies ProjectSettings files are searched for conflict blocks, skipping hidden ones, without
// asking for .meta files.
func TestRunScansProjectSettingsForConflicts(t *testing.T) {
	root := writeProjectWithManifest(t, map[string]string{
		"ProjectSettings/ProjectSettings.asset": conflictBlock,
		"ProjectSettings/.hidden":               conflictBlock,
		"ProjectSettings/Sub/Other.asset":       conflictBlock,
	})

	assertFindings(t, runProject(t, root),
		conflictMarker("ProjectSettings/ProjectSettings.asset", 2),
		conflictMarker("ProjectSettings/Sub/Other.asset", 2))
}

// Verifies the package manifest and lock file are searched for conflict blocks.
func TestRunReportsConflictsInPackageManifests(t *testing.T) {
	root := writeProject(t, map[string]string{
		"Packages/manifest.json":      conflictBlock,
		"Packages/packages-lock.json": conflictBlock,
	})

	assertFindings(t, runProject(t, root),
		conflictMarker("Packages/manifest.json", 2),
		conflictMarker("Packages/packages-lock.json", 2),
		manifestInvalid())
}

// Verifies an empty file is accepted.
func TestRunAcceptsEmptyFile(t *testing.T) {
	root := writeProjectWithManifest(t, withMetaFiles(map[string]string{"Assets/Empty.txt": ""}))

	assertFindings(t, runProject(t, root))
}
