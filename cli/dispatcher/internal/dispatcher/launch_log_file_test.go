package dispatcher

import (
	"bytes"
	"context"
	"errors"
	"os"
	"path/filepath"
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
	if !strings.Contains(stderr.String(), "`uloop launch --restart --log-file /tmp/editor.log`") {
		t.Fatalf("stderr should guide a restart with the requested log:\n%s", stderr.String())
	}
}

// Verifies a log path Unity could not write to is refused before the running Editor is stopped, since the
// stop cannot be undone.
func TestRunLaunchWithUnusableLogFileLeavesTheRunningEditorAlone(t *testing.T) {
	directory := t.TempDir()
	regularFile := filepath.Join(directory, "regular-file")
	if err := os.WriteFile(regularFile, []byte("x"), 0o644); err != nil {
		t.Fatalf("writing %s failed: %v", regularFile, err)
	}
	cases := []struct {
		name    string
		logFile string
	}{
		{name: "parent is a file", logFile: filepath.Join(regularFile, "editor.log")},
		{name: "path is a directory", logFile: directory},
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
				launchOptions{projectPath: projectRoot, restart: true, logFile: testCase.logFile},
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
	if !strings.Contains(stderr.String(), "`uloop launch --restart --log-file /tmp/editor.log`") {
		t.Fatalf("stderr should guide a restart with the requested log:\n%s", stderr.String())
	}
}
