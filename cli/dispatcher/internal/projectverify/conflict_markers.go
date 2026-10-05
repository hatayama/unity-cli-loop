package projectverify

import (
	"bufio"
	"bytes"
	"errors"
	"fmt"
	"io"
	"io/fs"
	"os"
	"path/filepath"
)

const (
	// binaryProbeLength is how far git looks for a NUL byte before it calls a file binary.
	binaryProbeLength      = 8000
	conflictReadBufferSize = 64 * 1024
	conflictMarkerLength   = 7
	conflictSeparator      = "======="
)

// findConflictBlock returns the 1-based line of the opening marker of the first complete
// conflict block, or found=false. Files with a NUL byte in the first 8000 bytes are binary and
// are not read further.
func findConflictBlock(r io.Reader) (line int, found bool, err error) {
	reader := bufio.NewReaderSize(r, conflictReadBufferSize)
	binary, err := looksBinary(reader)
	if err != nil || binary {
		return 0, false, err
	}

	block := conflictBlockState{}
	lineNumber, atLineStart := 0, true
	for {
		// ReadLine drops the \n or \r\n, and hands a line longer than the buffer over in pieces.
		chunk, isPrefix, readErr := reader.ReadLine()
		if errors.Is(readErr, io.EOF) {
			return 0, false, nil
		}
		if readErr != nil {
			return 0, false, readErr
		}
		// Only the first piece of a long line starts a line, so markers are matched at line starts only.
		if atLineStart {
			lineNumber++
			if block.observeLine(chunk, lineNumber) {
				return block.openLine, true, nil
			}
		}
		atLineStart = !isPrefix
	}
}

// looksBinary peeks at the first 8000 bytes without consuming them and reports whether they
// contain a NUL byte, the same test git uses to call a file binary.
func looksBinary(reader *bufio.Reader) (bool, error) {
	// A file shorter than 8000 bytes yields what it has together with io.EOF.
	head, err := reader.Peek(binaryProbeLength)
	if err != nil && !errors.Is(err, io.EOF) {
		return false, err
	}
	return bytes.IndexByte(head, 0) >= 0, nil
}

// conflictBlockState follows one file's lines through opening marker, separator, and closing marker.
type conflictBlockState struct {
	openLine     int
	sawSeparator bool
}

// observeLine takes the start of one line and reports whether it closes a complete block.
func (s *conflictBlockState) observeLine(line []byte, lineNumber int) bool {
	switch {
	case isConflictMarker(line, '<'):
		// The previous opening marker never closed, so the block starts again here.
		s.openLine, s.sawSeparator = lineNumber, false
	case s.openLine > 0 && string(line) == conflictSeparator:
		s.sawSeparator = true
	case s.openLine > 0 && s.sawSeparator && isConflictMarker(line, '>'):
		return true
	}
	return false
}

// isConflictMarker reports whether the line starts with seven marker characters followed by the
// end of the line or a space, which is how git writes them.
func isConflictMarker(line []byte, marker byte) bool {
	if len(line) < conflictMarkerLength {
		return false
	}
	for _, character := range line[:conflictMarkerLength] {
		if character != marker {
			return false
		}
	}
	return len(line) == conflictMarkerLength || line[conflictMarkerLength] == ' '
}

// scanFileForConflicts reports the first conflict block of one regular file.
func (v *verifier) scanFileForConflicts(path string, display string) error {
	file, err := os.Open(path)
	if err != nil {
		return fmt.Errorf("open %s: %w", path, err)
	}
	defer func() { _ = file.Close() }()

	line, found, err := findConflictBlock(file)
	if err != nil {
		return fmt.Errorf("read %s: %w", path, err)
	}
	if found {
		v.addFinding(CheckConflictMarker, display, line, conflictMarkerMessage(display, line))
	}
	return nil
}

// scanProjectSettings looks for conflict blocks only; ProjectSettings files have no .meta files.
func (v *verifier) scanProjectSettings() error {
	return v.scanTreeForConflicts(
		filepath.Join(v.projectRoot, projectSettingsDirectoryName), projectSettingsDirectoryName)
}

// scanTreeForConflicts searches every regular file under dir that Unity does not hide. Symbolic
// links are neither followed nor read.
func (v *verifier) scanTreeForConflicts(dir string, display string) error {
	entries, err := os.ReadDir(dir)
	if err != nil {
		return fmt.Errorf("read %s: %w", dir, err)
	}
	for _, entry := range entries {
		if isUnityHiddenName(entry.Name(), entry.IsDir()) {
			continue
		}
		entryPath := filepath.Join(dir, entry.Name())
		entryDisplay := display + "/" + entry.Name()
		if entry.IsDir() {
			err = v.scanTreeForConflicts(entryPath, entryDisplay)
		} else if entry.Type().IsRegular() {
			err = v.scanFileForConflicts(entryPath, entryDisplay)
		}
		if err != nil {
			return err
		}
	}
	return nil
}

// scanPackageManifestFiles searches Packages/manifest.json and Packages/packages-lock.json for
// conflict blocks; a merge leaves blocks in them as often as in assets.
func (v *verifier) scanPackageManifestFiles() error {
	for _, name := range []string{"manifest.json", "packages-lock.json"} {
		path := filepath.Join(v.projectRoot, packagesDirectoryName, name)
		info, err := os.Lstat(path)
		if errors.Is(err, fs.ErrNotExist) {
			continue
		}
		if err != nil {
			return fmt.Errorf("inspect %s: %w", path, err)
		}
		if !info.Mode().IsRegular() {
			continue
		}
		if err := v.scanFileForConflicts(path, packagesDirectoryName+"/"+name); err != nil {
			return err
		}
	}
	return nil
}

func conflictMarkerMessage(path string, line int) string {
	return fmt.Sprintf("%s still contains a merge conflict block starting at line %d. Resolve the conflict "+
		"and remove the markers; Unity cannot load or compile a file that contains them.", path, line)
}
