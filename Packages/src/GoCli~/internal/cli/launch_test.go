package cli

import (
	"bytes"
	"context"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func TestParseLaunchOptionsSupportsCoreFlags(t *testing.T) {
	options, err := parseLaunchOptions(
		[]string{
			"--restart",
			"--delete-recovery",
			"--platform", "Android",
			"--max-depth", "-1",
			"/tmp/project",
		},
		"",
	)
	if err != nil {
		t.Fatalf("parseLaunchOptions failed: %v", err)
	}

	if !options.restart {
		t.Fatal("restart flag was not parsed")
	}
	if !options.deleteRecovery {
		t.Fatal("delete recovery flag was not parsed")
	}
	if options.platform != "Android" {
		t.Fatalf("platform mismatch: %s", options.platform)
	}
	if options.maxDepth != -1 {
		t.Fatalf("max depth mismatch: %d", options.maxDepth)
	}
	if options.projectPath != "/tmp/project" {
		t.Fatalf("project path mismatch: %s", options.projectPath)
	}
}

func TestParseLaunchOptionsRejectsUnityHubRegistration(t *testing.T) {
	_, err := parseLaunchOptions([]string{"--add-unity-hub"}, "")
	if err == nil {
		t.Fatal("expected Unity Hub registration option error")
	}
}

func TestReadUnityEditorVersion(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	projectSettings := filepath.Join(projectRoot, "ProjectSettings")
	if err := os.WriteFile(
		filepath.Join(projectSettings, "ProjectVersion.txt"),
		[]byte("m_EditorVersion: 6000.0.1f1\n"),
		0o644,
	); err != nil {
		t.Fatalf("failed to write ProjectVersion.txt: %v", err)
	}

	version, err := readUnityEditorVersion(projectRoot)
	if err != nil {
		t.Fatalf("readUnityEditorVersion failed: %v", err)
	}
	if version != "6000.0.1f1" {
		t.Fatalf("version mismatch: %s", version)
	}
}

func TestResolveLaunchProjectRootAcceptsUnityProjectWithoutUloopSettings(t *testing.T) {
	projectRoot := createLaunchTestProject(t)

	resolved, err := resolveLaunchProjectRoot(projectRoot, launchOptions{})
	if err != nil {
		t.Fatalf("resolveLaunchProjectRoot failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

func TestRunLaunchQuitDoesNotLaunchWhenUnityIsNotRunning(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunch(
		context.Background(),
		launchOptions{quit: true, projectPath: projectRoot},
		projectRoot,
		&stdout,
		&stderr,
	)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "No Unity process is running") {
		t.Fatalf("stdout mismatch: %s", stdout.String())
	}
}

func TestNewUnityLaunchCommandIsNotContextCancelable(t *testing.T) {
	command := newUnityLaunchCommand("/bin/echo", []string{"hello"})

	if command.Cancel != nil {
		t.Fatal("Unity launch command must not be killed when the CLI context is canceled")
	}
}

func TestCleanStaleUnityTempDeletesTempWhenLockfileExists(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	tempPath := filepath.Join(projectRoot, launchTempDirectoryName)
	if err := os.MkdirAll(tempPath, 0o755); err != nil {
		t.Fatalf("failed to create Temp: %v", err)
	}
	if err := os.WriteFile(filepath.Join(tempPath, unityLockfileName), []byte{}, 0o644); err != nil {
		t.Fatalf("failed to create UnityLockfile: %v", err)
	}

	removed, err := cleanStaleUnityTemp(projectRoot)
	if err != nil {
		t.Fatalf("cleanStaleUnityTemp failed: %v", err)
	}
	if !removed {
		t.Fatal("expected stale Temp removal")
	}
	if _, err := os.Stat(tempPath); !os.IsNotExist(err) {
		t.Fatalf("Temp still exists after cleanup: %v", err)
	}
}

func TestWaitForUnityLockfileReturnsAfterLockfileAppears(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	lockfilePath := unityLockfilePath(projectRoot)
	errChan := make(chan error, 1)

	go func() {
		errChan <- waitForUnityLockfile(context.Background(), lockfilePath, time.Millisecond, time.Second)
	}()

	if err := os.MkdirAll(filepath.Dir(lockfilePath), 0o755); err != nil {
		t.Fatalf("failed to create Temp: %v", err)
	}
	if err := os.WriteFile(lockfilePath, []byte{}, 0o644); err != nil {
		t.Fatalf("failed to create UnityLockfile: %v", err)
	}

	select {
	case err := <-errChan:
		if err != nil {
			t.Fatalf("waitForUnityLockfile failed: %v", err)
		}
	case <-time.After(time.Second):
		t.Fatal("timed out waiting for waitForUnityLockfile")
	}
}

func createLaunchTestProject(t *testing.T) string {
	t.Helper()

	projectRoot := t.TempDir()
	for _, directory := range []string{"Assets", "ProjectSettings"} {
		if err := os.MkdirAll(filepath.Join(projectRoot, directory), 0o755); err != nil {
			t.Fatalf("failed to create %s: %v", directory, err)
		}
	}
	return projectRoot
}
