package dispatcher

import (
	"bytes"
	"context"
	"errors"
	"os"
	"path/filepath"
	"runtime"
	"slices"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

// Verifies --log-file accepts both value forms, keeps spaces, and reads a relative path from the caller's
// directory, because the Editor must not be left to decide what a relative path means.
func TestParseLaunchOptionsReadsLogFile(t *testing.T) {
	workingDirectory, err := os.Getwd()
	if err != nil {
		t.Fatalf("Getwd failed: %v", err)
	}
	absolutePath := filepath.Join(t.TempDir(), "my logs", "editor.log")
	cases := []struct {
		name string
		args []string
		want string
	}{
		{name: "separate value", args: []string{"--log-file", absolutePath}, want: absolutePath},
		{name: "equals value", args: []string{"--log-file=" + absolutePath}, want: absolutePath},
		{name: "relative path", args: []string{"--log-file", "editor.log"}, want: filepath.Join(workingDirectory, "editor.log")},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			options, err := parseLaunchOptions(testCase.args, "")
			if err != nil {
				t.Fatalf("parseLaunchOptions failed: %v", err)
			}
			if options.logFile != testCase.want {
				t.Fatalf("log file = %q, want %q", options.logFile, testCase.want)
			}
		})
	}
}

// Verifies --log-file without a usable path is refused instead of silently using the shared log. "-" is
// refused because Unity reads it as stdout, which a detached Editor does not have.
func TestParseLaunchOptionsRejectsLogFileWithoutAPath(t *testing.T) {
	cases := []struct {
		name string
		args []string
	}{
		{name: "no value", args: []string{"--log-file"}},
		{name: "empty equals value", args: []string{"--log-file="}},
		{name: "next option as value", args: []string{"--log-file", "--restart"}},
		{name: "stdout as equals value", args: []string{"--log-file=-"}},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if _, err := parseLaunchOptions(testCase.args, ""); err == nil {
				t.Fatalf("expected %v to be refused", testCase.args)
			}
		})
	}
}

// Verifies the requested log reaches Unity as -logFile.
func TestBuildUnityLaunchArgsPassesLogFile(t *testing.T) {
	args := buildUnityLaunchArgs("/tmp/project", launchOptions{logFile: "/tmp/my logs/editor.log"})
	expectedArgs := []string{
		"-projectPath",
		"/tmp/project",
		"-logFile",
		"/tmp/my logs/editor.log",
		"-ignorecompilererrors",
	}

	if !slices.Equal(args, expectedArgs) {
		t.Fatalf("Unity launch args mismatch: got %#v want %#v", args, expectedArgs)
	}
}

// Verifies --log-file cannot be silently dropped by reusing an Editor that already writes its log elsewhere.
func TestRunLaunchRequiresRestartForLogFileWithExistingUnityProcess(t *testing.T) {
	deps := defaultLaunchDeps()
	readinessChecked := false
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 222}, nil
	}
	deps.waitForToolReadiness = func(context.Context, string, time.Duration) error {
		readinessChecked = true
		return nil
	}
	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(
		context.Background(),
		launchOptions{projectPath: projectRoot, logFile: "/tmp/editor.log"},
		projectRoot,
		&stdout,
		&stderr,
		deps,
	)

	if code != 1 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if readinessChecked {
		t.Fatal("launch should not report an existing process as ready when its log cannot be the requested one")
	}
	if !strings.Contains(stderr.String(), "`uloop launch --restart --log-file \\\"/tmp/editor.log\\\"`") {
		t.Fatalf("stderr should guide a restart with the requested log, quoted for paths with spaces:\n%s", stderr.String())
	}
}

// Verifies a log path Unity could not write to is refused before the running Editor is stopped, since the
// stop cannot be undone.
func TestRunLaunchWithUnusableLogFileLeavesTheRunningEditorAlone(t *testing.T) {
	cases := []struct {
		name    string
		logFile func(t *testing.T) string
	}{
		{name: "parent is a file", logFile: func(t *testing.T) string {
			regularFile := filepath.Join(t.TempDir(), "regular-file")
			writeLaunchLogFileForTest(t, regularFile, "x")
			return filepath.Join(regularFile, "editor.log")
		}},
		{name: "path is a directory", logFile: func(t *testing.T) string {
			return t.TempDir()
		}},
		{name: "file is read-only", logFile: func(t *testing.T) string {
			skipLaunchLogFilePermissionTestAsRoot(t)
			logFile := filepath.Join(t.TempDir(), "editor.log")
			writeLaunchLogFileForTest(t, logFile, "x")
			chmodLaunchLogFileForTest(t, logFile, 0o444)
			return logFile
		}},
		{name: "directory cannot be written", logFile: func(t *testing.T) string {
			skipLaunchLogFileDirectoryPermissionTest(t)
			directory := t.TempDir()
			chmodLaunchLogFileForTest(t, directory, 0o555)
			return filepath.Join(directory, "editor.log")
		}},
		{name: "directory cannot be searched", logFile: func(t *testing.T) string {
			skipLaunchLogFileDirectoryPermissionTest(t)
			directory := t.TempDir()
			chmodLaunchLogFileForTest(t, directory, 0o000)
			return filepath.Join(directory, "editor.log")
		}},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			deps := defaultLaunchDeps()
			stopAttempted := false
			deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
				return &clicore.UnityProcess{Pid: 222}, nil
			}
			deps.requestUnityQuit = func(context.Context, string) bool {
				stopAttempted = true
				return false
			}
			deps.killUnityProcess = func(int) error {
				stopAttempted = true
				return nil
			}
			projectRoot := createLaunchTestProject(t)
			var stdout bytes.Buffer
			var stderr bytes.Buffer

			code := runLaunchWithDeps(
				context.Background(),
				launchOptions{projectPath: projectRoot, restart: true, logFile: testCase.logFile(t)},
				projectRoot,
				&stdout,
				&stderr,
				deps,
			)

			if code != 1 {
				t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
			}
			if stopAttempted {
				t.Fatal("the running Editor must not be stopped for a log path that cannot be used")
			}
			if !strings.Contains(stderr.String(), "--log-file") {
				t.Fatalf("stderr should name the option:\n%s", stderr.String())
			}
		})
	}
}

// Verifies a quit starts no Editor, so a log path that could not be used does not get in its way.
func TestRunLaunchQuitIgnoresUnusableLogFile(t *testing.T) {
	regularFile := filepath.Join(t.TempDir(), "regular-file")
	if err := os.WriteFile(regularFile, []byte("x"), 0o644); err != nil {
		t.Fatalf("writing %s failed: %v", regularFile, err)
	}
	deps := defaultLaunchDeps()
	killedPid := 0
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 333}, nil
	}
	deps.findRunningUnityProcessDefinitively = deps.findRunningUnityProcess
	deps.requestUnityQuit = func(context.Context, string) bool {
		return false
	}
	deps.killUnityProcess = func(pid int) error {
		killedPid = pid
		return nil
	}
	deps.waitForUnityProcessExit = func(context.Context, string, int, time.Duration, time.Duration) error {
		return nil
	}
	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(
		context.Background(),
		launchOptions{projectPath: projectRoot, quit: true, logFile: filepath.Join(regularFile, "editor.log")},
		projectRoot,
		&stdout,
		&stderr,
		deps,
	)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	if killedPid != 333 {
		t.Fatalf("quit stopped pid %d, want 333", killedPid)
	}
}

// Verifies a launch creates the log's missing directories and says where the log goes, and that a launch
// without the option says nothing about a log, because the shared log stays the default.
func TestRunLaunchReportsTheRequestedLogFile(t *testing.T) {
	logFile := filepath.Join(t.TempDir(), "new directory", "editor.log")
	cases := []struct {
		name     string
		logFile  string
		wantLine bool
	}{
		{name: "with the option", logFile: logFile, wantLine: true},
		{name: "without the option"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			deps := defaultLaunchDeps()
			deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
				return nil, nil
			}
			fakeUnityPath := fakeUnityExecutablePath(t)
			deps.resolveUnityExecutablePath = func(string) (string, error) {
				return fakeUnityPath, nil
			}
			deps.waitForUnityStartupMarker = func(context.Context, string, time.Duration, time.Duration) error {
				return nil
			}
			deps.waitForToolReadiness = func(context.Context, string, time.Duration) error {
				return nil
			}
			deps.focusUnityProcess = func(context.Context, int) error {
				return nil
			}
			projectRoot := createLaunchTestProject(t)
			var stdout bytes.Buffer
			var stderr bytes.Buffer

			code := runLaunchWithDeps(
				context.Background(),
				launchOptions{projectPath: projectRoot, editorVersion: "6000.0.0f1", logFile: testCase.logFile},
				projectRoot,
				&stdout,
				&stderr,
				deps,
			)

			if code != 0 {
				t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
			}
			if !testCase.wantLine {
				if strings.Contains(stdout.String(), "Editor Log:") {
					t.Fatalf("stdout should not mention a log:\n%s", stdout.String())
				}
				return
			}
			if !strings.Contains(stdout.String(), "Editor Log: "+logFile+"\n") {
				t.Fatalf("stdout should name the log:\n%s", stdout.String())
			}
			if _, err := os.Stat(filepath.Dir(logFile)); err != nil {
				t.Fatalf("the log's directory should exist: %v", err)
			}
		})
	}
}

// Verifies --log-file is refused when only the project IPC shows Unity is running, because that path also
// reuses the running Editor, whose log cannot be the requested one.
func TestRunLaunchRefusesLogFileWhenOnlyTheIpcProbeFindsUnity(t *testing.T) {
	deps := defaultLaunchDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return nil, errors.New("failed to retrieve Unity process list: /bin/ps: operation not permitted")
	}
	deps.probeProjectIpcFallback = func(context.Context, string) error {
		return nil
	}
	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(
		context.Background(),
		launchOptions{projectPath: projectRoot, logFile: "/tmp/editor.log"},
		projectRoot,
		&stdout,
		&stderr,
		deps,
	)

	if code != 1 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if !strings.Contains(stderr.String(), "operation not permitted") {
		t.Fatalf("stderr should name why the scan failed:\n%s", stderr.String())
	}
	if !strings.Contains(stderr.String(), "where the process list can be read") {
		t.Fatalf("stderr should say a restart needs the process list, since it fails here too:\n%s", stderr.String())
	}
}

// Verifies a log path may contain the words that mark a batch mode run or an import worker. The process scan
// compares whole arguments, so such a path cannot hide the Editor.
func TestParseLaunchOptionsAcceptsLogFileContainingProcessScanWords(t *testing.T) {
	for _, value := range []string{"/tmp/nightly-batchmode.log", "/tmp/nightly -batchmode dir/editor.log", "/tmp/AssetImportWorker.log"} {
		t.Run(value, func(t *testing.T) {
			options, err := parseLaunchOptions([]string{"--log-file", value}, "")
			if err != nil {
				t.Fatalf("expected %q to be accepted: %v", value, err)
			}
			if !filepath.IsAbs(options.logFile) {
				t.Fatalf("log file = %q, want an absolute path", options.logFile)
			}
		})
	}
}

// Verifies preparing an existing log leaves it as it is, because the next start is what replaces it.
func TestPrepareLaunchLogFileKeepsAnExistingFile(t *testing.T) {
	logFile := filepath.Join(t.TempDir(), "editor.log")
	writeLaunchLogFileForTest(t, logFile, "earlier run")

	if err := prepareLaunchLogFile(logFile); err != nil {
		t.Fatalf("prepareLaunchLogFile failed: %v", err)
	}

	content, err := os.ReadFile(logFile)
	if err != nil {
		t.Fatalf("reading %s failed: %v", logFile, err)
	}
	if string(content) != "earlier run" {
		t.Fatalf("existing log changed: %q", content)
	}
}

// Verifies a new log is created, which is what proves Unity can write there before an Editor is stopped.
func TestPrepareLaunchLogFileCreatesAMissingFile(t *testing.T) {
	logFile := filepath.Join(t.TempDir(), "new directory", "editor.log")

	if err := prepareLaunchLogFile(logFile); err != nil {
		t.Fatalf("prepareLaunchLogFile failed: %v", err)
	}

	if _, err := os.Stat(logFile); err != nil {
		t.Fatalf("the log should exist: %v", err)
	}
}

func writeLaunchLogFileForTest(t *testing.T, path string, content string) {
	t.Helper()
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatalf("writing %s failed: %v", path, err)
	}
}

// chmodLaunchLogFileForTest restores the mode afterwards, because t.TempDir cannot clean up otherwise.
func chmodLaunchLogFileForTest(t *testing.T, path string, mode os.FileMode) {
	t.Helper()
	info, err := os.Stat(path)
	if err != nil {
		t.Fatalf("stat %s failed: %v", path, err)
	}
	if err := os.Chmod(path, mode); err != nil {
		t.Fatalf("chmod %s failed: %v", path, err)
	}
	t.Cleanup(func() {
		if err := os.Chmod(path, info.Mode().Perm()); err != nil {
			t.Errorf("restoring the mode of %s failed: %v", path, err)
		}
	})
}

// skipLaunchLogFilePermissionTestAsRoot skips because root ignores the permission bits under test.
func skipLaunchLogFilePermissionTestAsRoot(t *testing.T) {
	t.Helper()
	if os.Geteuid() == 0 {
		t.Skip("root ignores file permissions")
	}
}

// skipLaunchLogFileDirectoryPermissionTest also skips on Windows, where a directory's mode does not limit access.
func skipLaunchLogFileDirectoryPermissionTest(t *testing.T) {
	t.Helper()
	skipLaunchLogFilePermissionTestAsRoot(t)
	if runtime.GOOS == "windows" {
		t.Skip("a directory's mode does not limit access on Windows")
	}
}
