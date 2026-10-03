package unityprocess

import (
	"encoding/base64"
	"encoding/binary"
	"errors"
	"fmt"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

// Verifies a Unity editor command without any -projectPath flag is not reported as a project's Editor.
func TestMatchMacUnityProcessRejectsCommandWithoutProjectPath(t *testing.T) {
	process, matched := matchMacUnityProcess(
		123,
		`/Applications/Unity/Hub/Editor/6000.0.0f1/Unity.app/Contents/MacOS/Unity -useHub -hubIPC`)

	if matched {
		t.Fatalf("expected no match without a project path, got %#v", process)
	}
}

// Verifies a procargs2 buffer whose last argv entry lacks its NUL terminator keeps only the complete entries.
func TestParseMacProcArgs2StopsAtUnterminatedArgument(t *testing.T) {
	buf := make([]byte, 4)
	binary.LittleEndian.PutUint32(buf, 2)
	buf = append(buf, []byte("/usr/bin/execpath")...)
	buf = append(buf, 0, 0)
	buf = append(buf, []byte("/usr/bin/execpath")...)
	buf = append(buf, 0)
	buf = append(buf, []byte("-truncated")...)

	args, err := parseMacProcArgs2(buf)
	if err != nil {
		t.Fatalf("expected no error, got: %v", err)
	}
	if len(args) != 1 || args[0] != "/usr/bin/execpath" {
		t.Fatalf("expected only the terminated argument, got %#v", args)
	}
}

// Verifies malformed Windows process-list lines are skipped individually while a valid line in the same output is kept.
func TestParseWindowsUnityProcessesSkipsMalformedLines(t *testing.T) {
	encode := func(commandLine string) string {
		return base64.StdEncoding.EncodeToString([]byte(commandLine))
	}
	validEditorCommand := encode(`C:\Editor\Unity.exe -projectPath "C:\Projects\Kept"`)
	cases := []struct {
		name string
		line string
	}{
		{"no delimiter", validEditorCommand},
		{"pid is not a number", "notapid|" + validEditorCommand},
		{"editor command without project path", "456|" + encode(`C:\Editor\Unity.exe -useHub`)},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			output := testCase.line + "\r\n" + "123|" + validEditorCommand + "\r\n"

			processes := parseWindowsUnityProcesses(output)

			if len(processes) != 1 || processes[0].Pid != 123 || processes[0].projectPath != `C:\Projects\Kept` {
				t.Fatalf("expected only the valid line, got %#v", processes)
			}
		})
	}
}

// Verifies project path extraction yields no path for commands whose flag is missing, empty, or has an unclosed quote.
func TestExtractProjectPathReturnsEmptyForUnusableFlag(t *testing.T) {
	cases := map[string]string{
		"no project path flag":    `Unity -useHub -hubIPC`,
		"flag with blank value":   "Unity -projectPath=   ",
		"double quote never ends": `Unity -projectPath "/Users/<USER_NAME>/Unclosed -useHub`,
		"single quote never ends": `Unity -projectPath '/Users/<USER_NAME>/Unclosed -useHub`,
	}
	for name, command := range cases {
		t.Run(name, func(t *testing.T) {
			if actual := extractProjectPath(command); actual != "" {
				t.Fatalf("expected no project path for %q, got %q", command, actual)
			}
		})
	}
}

// Verifies Windows project matching folds case so a differently cased path names the same project.
func TestNormalizeComparablePathFoldsCaseOnWindows(t *testing.T) {
	if runtime.GOOS != "windows" {
		t.Skip("Only Windows project matching is case-insensitive.")
	}

	root := t.TempDir()
	upper, err := normalizeComparablePath(filepath.Join(root, "CaseProject"))
	if err != nil {
		t.Fatalf("normalizeComparablePath failed: %v", err)
	}
	lower, err := normalizeComparablePath(filepath.Join(root, "caseproject"))
	if err != nil {
		t.Fatalf("normalizeComparablePath failed: %v", err)
	}
	if upper != lower {
		t.Fatalf("expected case-folded paths to match, got %q and %q", upper, lower)
	}
}

// Verifies the stderr captured in a wrapped *exec.ExitError is recovered for the error message.
func TestExitErrorStderrReadsWrappedExitError(t *testing.T) {
	err := fmt.Errorf("listing failed: %w", &exec.ExitError{Stderr: []byte("WMI is not available")})

	if stderr := exitErrorStderr(err); stderr != "WMI is not available" {
		t.Fatalf("expected the ExitError stderr, got %q", stderr)
	}
}

// Verifies an error that is not an *exec.ExitError contributes no stderr text.
func TestExitErrorStderrReturnsEmptyForOtherErrors(t *testing.T) {
	if stderr := exitErrorStderr(errors.New("executable file not found")); stderr != "" {
		t.Fatalf("expected no stderr, got %q", stderr)
	}
}

// Verifies a nil command error stays nil even when stderr text is present.
func TestCommandErrorWithStderrReturnsNilWithoutError(t *testing.T) {
	if err := commandErrorWithStderr(nil, "stray stderr output"); err != nil {
		t.Fatalf("expected nil, got %v", err)
	}
}

// Verifies the stderr text is appended after the original error so the cause stays first and unwrappable.
func TestCommandErrorWithStderrWrapsOriginalError(t *testing.T) {
	original := errors.New("exit status 1")

	err := commandErrorWithStderr(original, "  access denied\n")

	if !errors.Is(err, original) || !strings.HasSuffix(err.Error(), ": access denied") {
		t.Fatalf("expected the original error wrapped with trimmed stderr, got %v", err)
	}
}
