package cli

import (
	"context"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// Verifies that missing server state is treated as absent rather than failed.
func TestReadServerStateAllowsMissingFile(t *testing.T) {
	_, ok, err := readServerState(t.TempDir())
	if err != nil {
		t.Fatalf("readServerState failed: %v", err)
	}
	if ok {
		t.Fatal("missing server state should not be reported as present")
	}
}

// Verifies that failed readiness state exposes the server-side error message.
func TestServerStateFailureErrorIncludesLastError(t *testing.T) {
	state := serverState{Phase: "failed", LastError: "project IPC probe failed"}

	err := serverStateFailureError(state)

	if err == nil || !strings.Contains(err.Error(), "project IPC probe failed") {
		t.Fatalf("failure error mismatch: %v", err)
	}
}

// Verifies that busy phases are the only phases that block CLI wait loops.
func TestIsServerStateBusyMatchesRecoveryPhases(t *testing.T) {
	for _, phase := range []string{"starting", "compiling", "reloading", "recovering", "stopping"} {
		if !isServerStateBusy(serverState{Phase: phase}) {
			t.Fatalf("phase should be busy: %s", phase)
		}
	}

	for _, phase := range []string{"ready", "failed", "stopped", ""} {
		if isServerStateBusy(serverState{Phase: phase}) {
			t.Fatalf("phase should not be busy: %s", phase)
		}
	}
}

// Verifies that busy server state defers command dispatch until readiness succeeds.
func TestWaitForRecoveringServerIfNeededWaitsWhenStateIsBusy(t *testing.T) {
	projectRoot := t.TempDir()
	writeReadinessServerStateForTest(t, projectRoot, `{"phase":"recovering"}`)
	waitCalled := false

	err := waitForRecoveringServerIfNeeded(
		context.Background(),
		projectRoot,
		func(context.Context, string) error {
			waitCalled = true
			return nil
		})
	if err != nil {
		t.Fatalf("waitForRecoveringServerIfNeeded failed: %v", err)
	}
	if !waitCalled {
		t.Fatal("busy state should call readiness wait")
	}
}

// Verifies that ready server state allows commands to dispatch immediately.
func TestWaitForRecoveringServerIfNeededSkipsWaitWhenReady(t *testing.T) {
	projectRoot := t.TempDir()
	writeReadinessServerStateForTest(t, projectRoot, `{"phase":"ready"}`)

	err := waitForRecoveringServerIfNeeded(
		context.Background(),
		projectRoot,
		func(context.Context, string) error {
			t.Fatal("ready state should not call readiness wait")
			return nil
		})
	if err != nil {
		t.Fatalf("waitForRecoveringServerIfNeeded failed: %v", err)
	}
}

// Verifies that failed server state stops command dispatch with the recorded error.
func TestWaitForRecoveringServerIfNeededFailsWhenStateFailed(t *testing.T) {
	projectRoot := t.TempDir()
	writeReadinessServerStateForTest(t, projectRoot, `{"phase":"failed","lastError":"readiness probe failed"}`)

	err := waitForRecoveringServerIfNeeded(
		context.Background(),
		projectRoot,
		func(context.Context, string) error {
			t.Fatal("failed state should not call readiness wait")
			return nil
		})

	if err == nil || !strings.Contains(err.Error(), "readiness probe failed") {
		t.Fatalf("failure error mismatch: %v", err)
	}
}

// Verifies that server state JSON is read from the shared project Temp path.
func TestReadServerStateReadsSharedTempPath(t *testing.T) {
	projectRoot := t.TempDir()
	writeReadinessServerStateForTest(t, projectRoot, `{"phase":"ready","generationId":"gen","updatedAt":"now","reason":"test","endpoint":"endpoint","lastError":""}`)

	state, ok, err := readServerState(projectRoot)
	if err != nil {
		t.Fatalf("readServerState failed: %v", err)
	}

	if !ok {
		t.Fatal("server state was not found")
	}
	if state.Phase != "ready" || state.Endpoint != "endpoint" {
		t.Fatalf("server state mismatch: %#v", state)
	}
}

// Verifies that a state write in the .tmp phase is still visible to CLI waiters.
func TestReadServerStateReadsTempSidecarWhenTargetIsMissing(t *testing.T) {
	projectRoot := t.TempDir()
	writeReadinessServerStateSidecarForTest(t, projectRoot, ".tmp", `{"phase":"recovering","generationId":"tmp"}`)

	state, ok, err := readServerState(projectRoot)
	if err != nil {
		t.Fatalf("readServerState failed: %v", err)
	}

	if !ok || state.Phase != "recovering" || state.GenerationID != "tmp" {
		t.Fatalf("server state sidecar mismatch: ok=%v state=%#v", ok, state)
	}
}

// Verifies that a crash leaving only the .bak sidecar still preserves recovery state for CLI waiters.
func TestReadServerStateReadsBackupSidecarWhenTargetIsMissing(t *testing.T) {
	projectRoot := t.TempDir()
	writeReadinessServerStateSidecarForTest(t, projectRoot, ".bak", `{"phase":"starting","generationId":"bak"}`)

	state, ok, err := readServerState(projectRoot)
	if err != nil {
		t.Fatalf("readServerState failed: %v", err)
	}

	if !ok || state.Phase != "starting" || state.GenerationID != "bak" {
		t.Fatalf("server state backup mismatch: ok=%v state=%#v", ok, state)
	}
}

// Verifies that .tmp wins over .bak because it represents the newer atomic-write sidecar.
func TestReadServerStatePrefersTempSidecarOverBackup(t *testing.T) {
	projectRoot := t.TempDir()
	writeReadinessServerStateSidecarForTest(t, projectRoot, ".bak", `{"phase":"ready","generationId":"bak"}`)
	writeReadinessServerStateSidecarForTest(t, projectRoot, ".tmp", `{"phase":"recovering","generationId":"tmp"}`)

	state, ok, err := readServerState(projectRoot)
	if err != nil {
		t.Fatalf("readServerState failed: %v", err)
	}

	if !ok || state.GenerationID != "tmp" {
		t.Fatalf("server state sidecar priority mismatch: ok=%v state=%#v", ok, state)
	}
}

func writeReadinessServerStateForTest(t *testing.T, projectRoot string, content string) {
	t.Helper()

	statePath := filepath.Join(projectRoot, serverStateRelativePath)
	if err := os.MkdirAll(filepath.Dir(statePath), 0o755); err != nil {
		t.Fatalf("failed to create state directory: %v", err)
	}
	if err := os.WriteFile(statePath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write server state: %v", err)
	}
}

func writeReadinessServerStateSidecarForTest(t *testing.T, projectRoot string, suffix string, content string) {
	t.Helper()

	statePath := filepath.Join(projectRoot, serverStateRelativePath)
	if err := os.MkdirAll(filepath.Dir(statePath), 0o755); err != nil {
		t.Fatalf("failed to create state directory: %v", err)
	}
	if err := os.WriteFile(statePath+suffix, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write server state sidecar: %v", err)
	}
}
