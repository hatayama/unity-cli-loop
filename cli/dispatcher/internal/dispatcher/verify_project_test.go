package dispatcher

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"os"
	"path/filepath"
	"strings"
	"testing"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/projectverify"
)

// createVerifyProjectFixture creates a Unity project verify-project finds nothing in. The manifest
// is written because a missing one is a finding.
func createVerifyProjectFixture(t *testing.T) string {
	t.Helper()
	projectRoot := createDispatcherUnityProject(t)
	writeVerifyProjectFile(t, filepath.Join(projectRoot, "Packages", "manifest.json"), `{"dependencies":{}}`)
	return projectRoot
}

func writeVerifyProjectFile(t *testing.T, path string, content string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatalf("failed to create %s: %v", filepath.Dir(path), err)
	}
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write %s: %v", path, err)
	}
}

// verifyProjectCall is what one tryHandleVerifyProjectRequest call returned and wrote.
type verifyProjectCall struct {
	handled bool
	code    int
	stdout  string
	stderr  string
}

func callVerifyProject(args []string, startPath string, projectPath string) verifyProjectCall {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	handled, code := tryHandleVerifyProjectRequest(args, startPath, projectPath, &stdout, &stderr)
	return verifyProjectCall{handled: handled, code: code, stdout: stdout.String(), stderr: stderr.String()}
}

// requireVerifyProjectReport checks the call printed a report and nothing on stderr, then decodes it.
func requireVerifyProjectReport(t *testing.T, call verifyProjectCall, wantCode int) projectverify.Report {
	t.Helper()
	if !call.handled || call.code != wantCode || call.stderr != "" {
		t.Fatalf("handled=%v code=%d stderr=%s, want handled with code %d and no stderr",
			call.handled, call.code, call.stderr, wantCode)
	}
	var report projectverify.Report
	if err := json.Unmarshal([]byte(call.stdout), &report); err != nil {
		t.Fatalf("stdout is not a report: %v\n%s", err, call.stdout)
	}
	return report
}

// requireVerifyProjectError checks the call failed with an error envelope and empty stdout, then
// returns the error.
func requireVerifyProjectError(t *testing.T, call verifyProjectCall) clierrors.CLIError {
	t.Helper()
	if !call.handled || call.code != 1 || call.stdout != "" {
		t.Fatalf("handled=%v code=%d stdout=%s, want handled with code 1 and no stdout",
			call.handled, call.code, call.stdout)
	}
	var envelope clierrors.CLIErrorEnvelope
	if err := json.Unmarshal([]byte(call.stderr), &envelope); err != nil {
		t.Fatalf("stderr is not an error envelope: %v\n%s", err, call.stderr)
	}
	return envelope.Error
}

// Verifies a clean project prints a successful report and exits 0.
func TestVerifyProjectReportsCleanProject(t *testing.T) {
	projectRoot := createVerifyProjectFixture(t)

	report := requireVerifyProjectReport(t, callVerifyProject([]string{"verify-project"}, projectRoot, ""), 0)

	if !report.Success || report.ProjectRoot != projectRoot {
		t.Fatalf("Success = %v, ProjectRoot = %s, want true and %s", report.Success, report.ProjectRoot, projectRoot)
	}
}

// Verifies a project with a finding still prints its report on stdout but exits 1.
func TestVerifyProjectExitsOneWhenFindingsExist(t *testing.T) {
	projectRoot := createVerifyProjectFixture(t)
	writeVerifyProjectFile(t, filepath.Join(projectRoot, "Assets", "NoMeta.cs"), "class NoMeta {}")

	report := requireVerifyProjectReport(t, callVerifyProject([]string{"verify-project"}, projectRoot, ""), 1)

	if report.FindingCount != 1 || report.Findings[0].Check != projectverify.CheckMetaMissing {
		t.Fatalf("FindingCount = %d, Findings = %+v, want one META_MISSING", report.FindingCount, report.Findings)
	}
}

// Verifies --project-path selects the project instead of the working directory.
func TestVerifyProjectUsesExplicitProjectPath(t *testing.T) {
	projectRoot := createVerifyProjectFixture(t)

	report := requireVerifyProjectReport(t,
		callVerifyProject([]string{"verify-project"}, t.TempDir(), projectRoot), 0)

	if report.ProjectRoot != projectRoot {
		t.Fatalf("ProjectRoot = %s, want %s", report.ProjectRoot, projectRoot)
	}
}

// Verifies a --project-path that is not a Unity project fails as PROJECT_NOT_FOUND.
func TestVerifyProjectRejectsNonUnityProjectPath(t *testing.T) {
	call := callVerifyProject([]string{"verify-project"}, t.TempDir(), t.TempDir())

	if cliError := requireVerifyProjectError(t, call); cliError.ErrorCode != "PROJECT_NOT_FOUND" {
		t.Fatalf("ErrorCode = %s, want PROJECT_NOT_FOUND", cliError.ErrorCode)
	}
}

// Verifies the project is found from a folder inside it.
func TestVerifyProjectFindsEnclosingProjectFromSubdirectory(t *testing.T) {
	projectRoot := createVerifyProjectFixture(t)
	startPath := filepath.Join(projectRoot, "Assets", "Scripts")
	if err := os.MkdirAll(startPath, 0o755); err != nil {
		t.Fatalf("failed to create %s: %v", startPath, err)
	}

	call := callVerifyProject([]string{"verify-project"}, startPath, "")

	if !call.handled {
		t.Fatal("verify-project was not handled")
	}
	var report projectverify.Report
	if err := json.Unmarshal([]byte(call.stdout), &report); err != nil || report.ProjectRoot != projectRoot {
		t.Fatalf("ProjectRoot = %s (%v), want %s", report.ProjectRoot, err, projectRoot)
	}
}

// Verifies the enclosing project wins over a Unity-shaped test fixture below the working directory.
func TestVerifyProjectPrefersEnclosingProjectOverNestedFixture(t *testing.T) {
	projectRoot := createVerifyProjectFixture(t)
	startPath := filepath.Join(projectRoot, "Assets", "Tests")
	for _, name := range []string{"Assets", "ProjectSettings"} {
		if err := os.MkdirAll(filepath.Join(startPath, "Fixture", name), 0o755); err != nil {
			t.Fatalf("failed to create the nested fixture: %v", err)
		}
	}

	call := callVerifyProject([]string{"verify-project"}, startPath, "")

	if !call.handled {
		t.Fatal("verify-project was not handled")
	}
	var report projectverify.Report
	if err := json.Unmarshal([]byte(call.stdout), &report); err != nil || report.ProjectRoot != projectRoot {
		t.Fatalf("ProjectRoot = %s (%v), want %s", report.ProjectRoot, err, projectRoot)
	}
}

// Verifies running outside any Unity project fails as PROJECT_NOT_FOUND.
func TestVerifyProjectFailsWhenNoProjectFound(t *testing.T) {
	call := callVerifyProject([]string{"verify-project"}, t.TempDir(), "")

	if cliError := requireVerifyProjectError(t, call); cliError.ErrorCode != "PROJECT_NOT_FOUND" {
		t.Fatalf("ErrorCode = %s, want PROJECT_NOT_FOUND", cliError.ErrorCode)
	}
}

// assertUnknownVerifyProjectOption checks the call was refused as an argument error naming arg.
func assertUnknownVerifyProjectOption(t *testing.T, call verifyProjectCall, arg string) {
	t.Helper()
	cliError := requireVerifyProjectError(t, call)
	wantNextActions := []string{"Run `uloop verify-project --help` to inspect supported options."}
	if cliError.ErrorCode != "INVALID_ARGUMENT" ||
		cliError.Message != "Unknown verify-project option: "+arg ||
		strings.Join(cliError.NextActions, "\n") != strings.Join(wantNextActions, "\n") {
		t.Fatalf("error = %+v, want INVALID_ARGUMENT for %s", cliError, arg)
	}
}

// Verifies an unknown option is refused before anything is checked.
func TestVerifyProjectRejectsUnknownOption(t *testing.T) {
	projectRoot := createVerifyProjectFixture(t)

	assertUnknownVerifyProjectOption(t,
		callVerifyProject([]string{"verify-project", "--bogus"}, projectRoot, ""), "--bogus")
}

// Verifies a positional argument is refused before anything is checked.
func TestVerifyProjectRejectsPositionalArgument(t *testing.T) {
	projectRoot := createVerifyProjectFixture(t)

	assertUnknownVerifyProjectOption(t,
		callVerifyProject([]string{"verify-project", "extra"}, projectRoot, ""), "extra")
}

// Verifies --help or -h anywhere after the command prints the usage, even next to an unknown option.
func TestVerifyProjectHelpPrintsUsage(t *testing.T) {
	for _, args := range [][]string{
		{"verify-project", "--help"},
		{"verify-project", "-h"},
		{"verify-project", "--bogus", "--help"},
	} {
		call := callVerifyProject(args, t.TempDir(), "")

		if !call.handled || call.code != 0 || call.stderr != "" {
			t.Fatalf("%v: handled=%v code=%d stderr=%s", args, call.handled, call.code, call.stderr)
		}
		for _, expected := range []string{"uloop verify-project", "Global options:"} {
			if !strings.Contains(call.stdout, expected) {
				t.Fatalf("%v: help output missing %q:\n%s", args, expected, call.stdout)
			}
		}
	}
}

// Verifies a project that passes resolution but cannot be read is reported as an internal error.
func TestVerifyProjectReportsRunErrorAsInternalError(t *testing.T) {
	projectRoot := t.TempDir()
	writeVerifyProjectFile(t, filepath.Join(projectRoot, "Assets"), "not a folder")
	if err := os.MkdirAll(filepath.Join(projectRoot, "ProjectSettings"), 0o755); err != nil {
		t.Fatalf("failed to create ProjectSettings: %v", err)
	}

	call := callVerifyProject([]string{"verify-project"}, t.TempDir(), projectRoot)

	if cliError := requireVerifyProjectError(t, call); cliError.ErrorCode != "INTERNAL_ERROR" {
		t.Fatalf("ErrorCode = %s, want INTERNAL_ERROR", cliError.ErrorCode)
	}
}

// createV2VerifyProjectFixture creates a clean project that the dispatcher detects as a V2 project.
func createV2VerifyProjectFixture(t *testing.T) string {
	t.Helper()
	projectRoot := createVerifyProjectFixture(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "abc123", "2.2.0")
	return projectRoot
}

// verifyProjectLocalDeps fails the test if a command is handed to the V2 CLI or a project runner.
func verifyProjectLocalDeps(t *testing.T) dispatcherRunDeps {
	t.Helper()
	deps := defaultDispatcherRunDeps()
	deps.runV2CLI = func(context.Context, string, []string, io.Writer, io.Writer) (int, error) {
		t.Fatal("verify-project must not be handed to the V2 CLI")
		return 0, nil
	}
	deps.runRealCLI = func(context.Context, string, []string, io.Writer, io.Writer) int {
		t.Fatal("verify-project must not be handed to a project runner")
		return 0
	}
	return deps
}

// Verifies verify-project runs in the dispatcher even in a V2 project, since it needs no package.
func TestRunDispatcherKeepsVerifyProjectForV2Project(t *testing.T) {
	projectRoot := createV2VerifyProjectFixture(t)
	t.Chdir(projectRoot)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runDispatcherWithDeps(context.Background(), []string{"verify-project"}, &stdout, &stderr,
		verifyProjectLocalDeps(t))

	report := requireVerifyProjectReport(t,
		verifyProjectCall{handled: true, code: code, stdout: stdout.String(), stderr: stderr.String()}, 0)
	if !report.Success {
		t.Fatalf("report = %+v, want a clean project", report)
	}
}

// Verifies verify-project --help is answered by the dispatcher even in a V2 project.
func TestRunDispatcherAnswersVerifyProjectHelpLocally(t *testing.T) {
	projectRoot := createV2VerifyProjectFixture(t)
	t.Chdir(projectRoot)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runDispatcherWithDeps(context.Background(), []string{"verify-project", "--help"}, &stdout, &stderr,
		verifyProjectLocalDeps(t))

	if code != 0 || stderr.String() != "" || !strings.Contains(stdout.String(), "uloop verify-project") {
		t.Fatalf("code=%d stdout=%s stderr=%s, want the usage", code, stdout.String(), stderr.String())
	}
}

// Verifies another command is left to the next handler without output.
func TestTryHandleVerifyProjectRequestIgnoresAnotherCommand(t *testing.T) {
	call := callVerifyProject([]string{"list"}, t.TempDir(), "")

	if call.handled || call.code != 0 || call.stdout != "" || call.stderr != "" {
		t.Fatalf("call = %+v, want an unhandled call without output", call)
	}
}
