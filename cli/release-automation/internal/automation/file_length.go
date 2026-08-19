package automation

import (
	"fmt"
	"io"
	"io/fs"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

// DefaultMaxFileLength is the repository-wide SLOC limit. Keep this equal to
// MAX_FILE_LENGTH in scripts/check-file-length.sh; those are the only two
// declaration sites for the threshold.
const DefaultMaxFileLength = 500

// productionSourceRoots are the only trees the file-length checker walks.
// Tests, Assets, and other trees stay out of scope by omission, not by a
// second exclusion list that could drift from this table.
var productionSourceRoots = []struct {
	relativeDir string
	extension   string
}{
	{relativeDir: "Packages/src", extension: ".cs"},
	{relativeDir: "cli", extension: ".go"},
	{relativeDir: "tools", extension: ".cs"},
}

// FileLengthFinding is one production source file whose SLOC exceeds the limit.
type FileLengthFinding struct {
	Path string
	SLOC int
}

// FileLengthCheckOptions configures RunFileLengthCheck.
type FileLengthCheckOptions struct {
	Root           string
	MaxLength      int
	FailOnExceeded bool
}

// ScanFileLengths walks production sources under root and returns every file
// whose SLOC is greater than maxLength. Paths use forward slashes so reports
// stay stable on Windows.
func ScanFileLengths(root string, maxLength int) ([]FileLengthFinding, error) {
	findings := []FileLengthFinding{}
	for _, sourceRoot := range productionSourceRoots {
		rootFindings, err := scanProductionRoot(root, sourceRoot.relativeDir, sourceRoot.extension, maxLength)
		if err != nil {
			return nil, err
		}
		findings = append(findings, rootFindings...)
	}
	sort.Slice(findings, func(left int, right int) bool {
		return findings[left].Path < findings[right].Path
	})
	return findings, nil
}

// RunFileLengthCheck prints findings and returns the process exit code.
func RunFileLengthCheck(stdout io.Writer, stderr io.Writer, options FileLengthCheckOptions) int {
	maxLength := options.MaxLength
	if maxLength <= 0 {
		maxLength = DefaultMaxFileLength
	}
	findings, err := ScanFileLengths(options.Root, maxLength)
	if err != nil {
		_, _ = fmt.Fprintln(stderr, "check-file-length:", err)
		return 1
	}
	_, _ = fmt.Fprintf(stdout, "=== File length (SLOC, max %d) ===\n", maxLength)
	if len(findings) == 0 {
		_, _ = fmt.Fprintln(stdout, "No files exceeded the file-length limit.")
		return 0
	}
	for _, finding := range findings {
		_, _ = fmt.Fprintf(stdout, "%s: %d SLOC (limit %d)\n", finding.Path, finding.SLOC, maxLength)
	}
	_, _ = fmt.Fprintf(stdout, "%d files exceeded the %d-line limit.\n", len(findings), maxLength)
	if options.FailOnExceeded {
		return 1
	}
	_, _ = fmt.Fprintln(stdout, "File-length findings were reported in warning mode; set CODE_FILE_LENGTH_FAIL_ON_EXCEEDED=true to fail on findings.")
	return 0
}

func scanProductionRoot(repoRoot string, relativeDir string, extension string, maxLength int) ([]FileLengthFinding, error) {
	absoluteRoot := filepath.Join(repoRoot, filepath.FromSlash(relativeDir))
	fileInfo, err := os.Stat(absoluteRoot)
	if err != nil {
		if os.IsNotExist(err) {
			return nil, nil
		}
		return nil, err
	}
	if !fileInfo.IsDir() {
		return nil, fmt.Errorf("production source root %s is not a directory", relativeDir)
	}

	findings := []FileLengthFinding{}
	walkErr := filepath.WalkDir(absoluteRoot, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		relativePath, err := filepath.Rel(repoRoot, path)
		if err != nil {
			return err
		}
		slashPath := filepath.ToSlash(relativePath)
		if entry.IsDir() {
			if shouldSkipFileLengthDirectory(slashPath, entry.Name()) {
				return fs.SkipDir
			}
			return nil
		}
		if !strings.HasSuffix(strings.ToLower(entry.Name()), extension) {
			return nil
		}
		if isExcludedFromFileLength(slashPath) {
			return nil
		}
		finding, exceeded, err := inspectFileLength(path, slashPath, maxLength)
		if err != nil {
			return err
		}
		if exceeded {
			findings = append(findings, finding)
		}
		return nil
	})
	if walkErr != nil {
		return nil, walkErr
	}
	return findings, nil
}

func inspectFileLength(absolutePath string, slashPath string, maxLength int) (FileLengthFinding, bool, error) {
	source, err := os.ReadFile(absolutePath)
	if err != nil {
		return FileLengthFinding{}, false, err
	}
	language, ok := languageForPath(slashPath)
	if !ok {
		return FileLengthFinding{}, false, nil
	}
	sloc := CountSLOC(source, language)
	if sloc <= maxLength {
		return FileLengthFinding{}, false, nil
	}
	return FileLengthFinding{Path: slashPath, SLOC: sloc}, true, nil
}

func languageForPath(slashPath string) (SourceLanguage, bool) {
	extension := strings.ToLower(filepath.Ext(slashPath))
	if extension == ".cs" {
		return LanguageCSharp, true
	}
	if extension == ".go" {
		return LanguageGo, true
	}
	return 0, false
}

func shouldSkipFileLengthDirectory(slashPath string, directoryName string) bool {
	if directoryName == "Tests" || directoryName == "testdata" {
		return true
	}
	return isExcludedFromFileLength(slashPath + "/")
}

// isExcludedFromFileLength is the single exclusion list for the checker.
// Keep this in step with docs/file-length.md.
func isExcludedFromFileLength(slashPath string) bool {
	if strings.Contains(slashPath, "/Tests/") || strings.HasPrefix(slashPath, "Tests/") {
		return true
	}
	if strings.Contains(slashPath, "/testdata/") || strings.HasPrefix(slashPath, "testdata/") {
		return true
	}
	return strings.HasSuffix(slashPath, "_test.go")
}
