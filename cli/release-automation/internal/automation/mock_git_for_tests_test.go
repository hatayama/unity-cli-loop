package automation

import (
	"os"
	"path/filepath"
	"sort"
	"strings"
	"testing"
)

// mockGitPathBehavior describes how the shared existence-probe mock git
// responds for a specific `<ref>:<path>` suffix in `cat-file -e` and `show`.
type mockGitPathBehavior struct {
	// exists is the response for `git cat-file -e <ref>:<path>`. When true
	// the mock exits 0 (present); when false the mock exits 1 (absent).
	exists bool
	// showOK controls the response for `git show <ref>:<path>`. When true
	// the mock emits showContent or the file at showContentPath and exits 0.
	// When false the mock emits showStderr on stderr and exits 1.
	showOK bool
	// showContent is emitted verbatim on stdout when showOK is true and
	// showContentPath is empty. Written with printf so no trailing newline is
	// appended.
	showContent string
	// showContentPath is `cat`'d to stdout when showOK is true. Callers use
	// this when the response content is already staged in a file.
	showContentPath string
	// showStderr is echoed on stderr when showOK is false.
	showStderr string
	// probeSleeps makes `cat-file -e` for this path block on `sleep 10` so a
	// caller with a short context timeout can force a probe execution failure
	// mid-chain. Other paths on the same fixture stay deterministic.
	probeSleeps bool
}

// mockGitExistenceFixture drives the shared existence-probe mock git script.
// It handles `cat-file -e`, `rev-parse --verify --quiet ^{commit}`, and an
// optional `show` per configured path. Unlisted paths produce an "unexpected"
// error so tests fail loudly if they invoke an unmodeled path.
type mockGitExistenceFixture struct {
	// refResolves controls `git rev-parse --verify --quiet <ref>^{commit}`:
	// true exits 0, false exits 1.
	refResolves bool
	// paths is keyed by the `<ref>:<path>` suffix used in cat-file/show
	// arguments; the emitted script matches with a `*:<key>` case label.
	paths map[string]mockGitPathBehavior
}

// setupMockGitBin creates a temp workspace containing a bin/ directory and
// prepends bin/ to PATH for the test. Returns the workspace directory (used
// as a repo root by callers) and the bin directory where mock executables
// should be installed.
func setupMockGitBin(t *testing.T) (string, string) {
	t.Helper()
	workDir := t.TempDir()
	binDir := filepath.Join(workDir, "bin")
	if err := os.MkdirAll(binDir, 0o755); err != nil {
		t.Fatalf("failed to create mock bin: %v", err)
	}
	t.Setenv("PATH", binDir+string(os.PathListSeparator)+os.Getenv("PATH"))
	return workDir, binDir
}

// writeExistenceMockGit installs the shared existence-probe mock git at
// binDir/git per fixture. This is the consolidation point for tests that
// previously each hand-wrote near-identical `cat-file -e`/`rev-parse
// --verify` mock scripts.
func writeExistenceMockGit(t *testing.T, binDir string, fixture mockGitExistenceFixture) {
	t.Helper()

	script := buildExistenceMockGitScript(fixture)
	path := filepath.Join(binDir, "git")
	writeFile(t, path, script)
	if err := os.Chmod(path, 0o755); err != nil {
		t.Fatalf("failed to chmod mock git: %v", err)
	}
}

func buildExistenceMockGitScript(fixture mockGitExistenceFixture) string {
	catFileCases := strings.Builder{}
	showCases := strings.Builder{}

	// Sort keys so the generated script is deterministic; iteration order of
	// Go maps is otherwise randomized.
	keys := make([]string, 0, len(fixture.paths))
	for key := range fixture.paths {
		keys = append(keys, key)
	}
	sort.Strings(keys)

	for _, key := range keys {
		behavior := fixture.paths[key]

		catFileCases.WriteString("    *:")
		catFileCases.WriteString(key)
		catFileCases.WriteString(")\n")
		if behavior.probeSleeps {
			// A long sleep lets a caller with a short context timeout kill
			// this specific probe, exercising the mid-chain wrap branch
			// without affecting other paths on the same fixture.
			catFileCases.WriteString("      sleep 10\n")
			catFileCases.WriteString("      exit 0\n")
		} else if behavior.exists {
			catFileCases.WriteString("      exit 0\n")
		} else {
			catFileCases.WriteString("      exit 1\n")
		}
		catFileCases.WriteString("      ;;\n")

		showCases.WriteString("    *:")
		showCases.WriteString(key)
		showCases.WriteString(")\n")
		if behavior.showOK {
			if behavior.showContentPath != "" {
				showCases.WriteString("      cat ")
				showCases.WriteString(shellSingleQuote(behavior.showContentPath))
				showCases.WriteString("\n")
			} else if behavior.showContent != "" {
				showCases.WriteString("      printf '%s' ")
				showCases.WriteString(shellSingleQuote(behavior.showContent))
				showCases.WriteString("\n")
			}
			showCases.WriteString("      exit 0\n")
		} else {
			if behavior.showStderr != "" {
				showCases.WriteString("      printf '%s\\n' ")
				showCases.WriteString(shellSingleQuote(behavior.showStderr))
				showCases.WriteString(" >&2\n")
			}
			showCases.WriteString("      exit 1\n")
		}
		showCases.WriteString("      ;;\n")
	}

	refExitCode := "1"
	if fixture.refResolves {
		refExitCode = "0"
	}

	builder := strings.Builder{}
	builder.WriteString("#!/bin/sh\nset -eu\n\n")
	builder.WriteString("if [ \"$1\" = \"-C\" ]; then\n")
	builder.WriteString("  shift 2\n")
	builder.WriteString("fi\n\n")
	builder.WriteString("case \"$1\" in\n")
	builder.WriteString("  cat-file)\n")
	builder.WriteString("    case \"$3\" in\n")
	builder.WriteString(catFileCases.String())
	builder.WriteString("    *)\n")
	builder.WriteString("      echo \"unexpected cat-file target: $3\" >&2\n")
	builder.WriteString("      exit 1\n")
	builder.WriteString("      ;;\n")
	builder.WriteString("    esac\n")
	builder.WriteString("    ;;\n")
	builder.WriteString("  rev-parse)\n")
	builder.WriteString("    if [ \"$2\" = \"--verify\" ]; then\n")
	builder.WriteString("      exit " + refExitCode + "\n")
	builder.WriteString("    fi\n")
	builder.WriteString("    echo \"unexpected rev-parse: $*\" >&2\n")
	builder.WriteString("    exit 1\n")
	builder.WriteString("    ;;\n")
	builder.WriteString("  show)\n")
	builder.WriteString("    case \"$2\" in\n")
	builder.WriteString(showCases.String())
	builder.WriteString("    *)\n")
	builder.WriteString("      echo \"unexpected git show ref: $2\" >&2\n")
	builder.WriteString("      exit 1\n")
	builder.WriteString("      ;;\n")
	builder.WriteString("    esac\n")
	builder.WriteString("    ;;\n")
	builder.WriteString("  *)\n")
	builder.WriteString("    echo \"unexpected git command: $*\" >&2\n")
	builder.WriteString("    exit 1\n")
	builder.WriteString("    ;;\n")
	builder.WriteString("esac\n")
	return builder.String()
}

// shellSingleQuote returns value as a POSIX-safe single-quoted literal.
// Embedded single quotes are escaped by closing the quoted section, emitting
// a backslash-escaped quote, and reopening it.
func shellSingleQuote(value string) string {
	return "'" + strings.ReplaceAll(value, "'", "'\\''") + "'"
}
