package dispatcher

import (
	"context"
	"encoding/json"
	"errors"
	"net"
	"os"
	"path/filepath"
	"strconv"
	"sync"
	"testing"
	"time"
)

const v2ServerSettingsBakFileName = "UnityMcpSettings.json.bak"

func writeV2ServerSettingsJSON(t *testing.T, projectRoot string, fileName string, settings v2ServerSettings) {
	t.Helper()
	settingsDirectory := filepath.Join(projectRoot, v2UserSettingsDirectoryName)
	if err := os.MkdirAll(settingsDirectory, 0o755); err != nil {
		t.Fatalf("mkdir settings: %v", err)
	}
	payload, err := json.Marshal(settings)
	if err != nil {
		t.Fatalf("marshal settings: %v", err)
	}
	if err := os.WriteFile(filepath.Join(settingsDirectory, fileName), payload, 0o644); err != nil {
		t.Fatalf("write settings %s: %v", fileName, err)
	}
}

func writeV2ServerSettingsRaw(t *testing.T, projectRoot string, fileName string, data []byte) {
	t.Helper()
	settingsDirectory := filepath.Join(projectRoot, v2UserSettingsDirectoryName)
	if err := os.MkdirAll(settingsDirectory, 0o755); err != nil {
		t.Fatalf("mkdir settings: %v", err)
	}
	if err := os.WriteFile(filepath.Join(settingsDirectory, fileName), data, 0o644); err != nil {
		t.Fatalf("write settings %s: %v", fileName, err)
	}
}

func startFakeTCPListener(t *testing.T) (int, net.Listener) {
	t.Helper()
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("listen: %v", err)
	}
	t.Cleanup(func() {
		_ = listener.Close()
	})
	tcpAddress, ok := listener.Addr().(*net.TCPAddr)
	if !ok {
		t.Fatalf("unexpected listener address type %T", listener.Addr())
	}
	return tcpAddress.Port, listener
}

func dialTCPPort(port int) error {
	connection, err := net.DialTimeout("tcp", net.JoinHostPort("127.0.0.1", strconv.Itoa(port)), 200*time.Millisecond)
	if err != nil {
		return err
	}
	return connection.Close()
}

// Verifies missing V2 settings files produce an error (neither .json nor .tmp present).
func TestReadV2ServerSettingsMissingFile(t *testing.T) {
	projectRoot := t.TempDir()
	_, err := readV2ServerSettings(projectRoot)
	if err == nil {
		t.Fatal("expected error when settings are missing")
	}
}

// Verifies a well-formed UnityMcpSettings.json is unmarshaled into v2ServerSettings.
func TestReadV2ServerSettingsReadsValidJSON(t *testing.T) {
	projectRoot := t.TempDir()
	writeV2ServerSettingsJSON(t, projectRoot, v2ServerSettingsFileName, v2ServerSettings{
		CustomPort:      8700,
		IsServerRunning: true,
		ServerSessionID: "session-abc",
	})

	settings, err := readV2ServerSettings(projectRoot)
	if err != nil {
		t.Fatalf("read settings: %v", err)
	}
	if settings.CustomPort != 8700 || !settings.IsServerRunning || settings.ServerSessionID != "session-abc" {
		t.Fatalf("unexpected settings %#v", settings)
	}
}

// Verifies UTF-8 BOM prefixed settings JSON is still readable.
func TestReadV2ServerSettingsReadsUTF8BOMJSON(t *testing.T) {
	projectRoot := t.TempDir()
	payload, err := json.Marshal(v2ServerSettings{
		CustomPort:      8701,
		IsServerRunning: true,
		ServerSessionID: "session-bom",
	})
	if err != nil {
		t.Fatalf("marshal settings: %v", err)
	}
	writeV2ServerSettingsRaw(t, projectRoot, v2ServerSettingsFileName, append([]byte("\xef\xbb\xbf"), payload...))

	settings, err := readV2ServerSettings(projectRoot)
	if err != nil {
		t.Fatalf("read BOM settings: %v", err)
	}
	if settings.CustomPort != 8701 || settings.ServerSessionID != "session-bom" {
		t.Fatalf("unexpected BOM settings %#v", settings)
	}
}

// Verifies a corrupt primary settings file falls back to .json.tmp, and .bak alone is never used.
func TestReadV2ServerSettingsFallsBackToTmpAndIgnoresBak(t *testing.T) {
	projectRoot := t.TempDir()
	writeV2ServerSettingsRaw(t, projectRoot, v2ServerSettingsFileName, []byte("{not-json"))
	writeV2ServerSettingsJSON(t, projectRoot, v2ServerSettingsTmpFileName, v2ServerSettings{
		CustomPort:      8702,
		IsServerRunning: true,
		ServerSessionID: "session-tmp",
	})
	writeV2ServerSettingsJSON(t, projectRoot, v2ServerSettingsBakFileName, v2ServerSettings{
		CustomPort:      9999,
		IsServerRunning: true,
		ServerSessionID: "session-bak",
	})

	settings, err := readV2ServerSettings(projectRoot)
	if err != nil {
		t.Fatalf("read settings via tmp: %v", err)
	}
	if settings.CustomPort != 8702 || settings.ServerSessionID != "session-tmp" {
		t.Fatalf("expected tmp settings, got %#v", settings)
	}

	bakOnlyRoot := t.TempDir()
	writeV2ServerSettingsJSON(t, bakOnlyRoot, v2ServerSettingsBakFileName, v2ServerSettings{
		CustomPort:      1,
		IsServerRunning: true,
		ServerSessionID: "session-bak-only",
	})
	_, bakErr := readV2ServerSettings(bakOnlyRoot)
	if bakErr == nil {
		t.Fatal("expected error when only .bak exists")
	}
}

// Verifies ready conditions: new session id, isServerRunning, dial success, and no compile/reload locks.
func TestWaitForV2ServerReadySucceedsWhenSessionDialAndIdle(t *testing.T) {
	projectRoot := t.TempDir()
	port, _ := startFakeTCPListener(t)
	writeV2ServerSettingsJSON(t, projectRoot, v2ServerSettingsFileName, v2ServerSettings{
		CustomPort:      port,
		IsServerRunning: true,
		ServerSessionID: "new-session",
	})

	err := waitForV2ServerReady(context.Background(), projectRoot, "old-session", dialTCPPort, 5*time.Millisecond, time.Second)
	if err != nil {
		t.Fatalf("wait for V2 server ready: %v", err)
	}
}

// Verifies compiling.lock keeps readiness false until the lock is removed.
func TestWaitForV2ServerReadyWaitsWhileCompilingLockPresent(t *testing.T) {
	projectRoot := t.TempDir()
	port, _ := startFakeTCPListener(t)
	tempDirectory := filepath.Join(projectRoot, launchTempDirectoryName)
	if err := os.MkdirAll(tempDirectory, 0o755); err != nil {
		t.Fatalf("mkdir Temp: %v", err)
	}
	compilingLockPath := filepath.Join(tempDirectory, v2CompilingLockFileName)
	if err := os.WriteFile(compilingLockPath, []byte("busy"), 0o644); err != nil {
		t.Fatalf("write compiling.lock: %v", err)
	}
	writeV2ServerSettingsJSON(t, projectRoot, v2ServerSettingsFileName, v2ServerSettings{
		CustomPort:      port,
		IsServerRunning: true,
		ServerSessionID: "new-session",
	})

	go func() {
		time.Sleep(40 * time.Millisecond)
		_ = os.Remove(compilingLockPath)
	}()

	err := waitForV2ServerReady(context.Background(), projectRoot, "old-session", dialTCPPort, 5*time.Millisecond, time.Second)
	if err != nil {
		t.Fatalf("wait while compiling: %v", err)
	}
}

// Verifies readiness waits until serverSessionId differs from the previous generation.
func TestWaitForV2ServerReadyWaitsForSessionIdGenerationChange(t *testing.T) {
	projectRoot := t.TempDir()
	port, _ := startFakeTCPListener(t)
	writeV2ServerSettingsJSON(t, projectRoot, v2ServerSettingsFileName, v2ServerSettings{
		CustomPort:      port,
		IsServerRunning: true,
		ServerSessionID: "same-session",
	})

	go func() {
		time.Sleep(40 * time.Millisecond)
		writeV2ServerSettingsJSON(t, projectRoot, v2ServerSettingsFileName, v2ServerSettings{
			CustomPort:      port,
			IsServerRunning: true,
			ServerSessionID: "changed-session",
		})
	}()

	err := waitForV2ServerReady(context.Background(), projectRoot, "same-session", dialTCPPort, 5*time.Millisecond, time.Second)
	if err != nil {
		t.Fatalf("wait for session generation: %v", err)
	}
}

// Verifies an empty serverSessionId (port-acquire retry) never satisfies readiness.
func TestWaitForV2ServerReadyRejectsEmptyServerSessionID(t *testing.T) {
	projectRoot := t.TempDir()
	port, _ := startFakeTCPListener(t)
	writeV2ServerSettingsJSON(t, projectRoot, v2ServerSettingsFileName, v2ServerSettings{
		CustomPort:      port,
		IsServerRunning: true,
		ServerSessionID: "",
	})

	err := waitForV2ServerReady(context.Background(), projectRoot, "", dialTCPPort, 5*time.Millisecond, 40*time.Millisecond)
	var timeoutErr v2ServerReadyTimeoutError
	if !errors.As(err, &timeoutErr) {
		t.Fatalf("expected V2 server ready timeout, got %v", err)
	}
}

// Verifies waitForV2ServerReady returns v2ServerReadyTimeoutError when conditions never become ready.
func TestWaitForV2ServerReadyTimesOut(t *testing.T) {
	projectRoot := t.TempDir()
	writeV2ServerSettingsJSON(t, projectRoot, v2ServerSettingsFileName, v2ServerSettings{
		CustomPort:      1,
		IsServerRunning: false,
		ServerSessionID: "session",
	})

	err := waitForV2ServerReady(context.Background(), projectRoot, "other", dialTCPPort, 5*time.Millisecond, 30*time.Millisecond)
	var timeoutErr v2ServerReadyTimeoutError
	if !errors.As(err, &timeoutErr) {
		t.Fatalf("expected V2 server ready timeout, got %v", err)
	}
}

// Verifies previousServerSessionID="" only requires a non-empty current session id (no generation compare).
func TestWaitForV2ServerReadySucceedsWithEmptyPreviousSessionID(t *testing.T) {
	projectRoot := t.TempDir()
	port, _ := startFakeTCPListener(t)
	writeV2ServerSettingsJSON(t, projectRoot, v2ServerSettingsFileName, v2ServerSettings{
		CustomPort:      port,
		IsServerRunning: true,
		ServerSessionID: "any-non-empty",
	})

	err := waitForV2ServerReady(context.Background(), projectRoot, "", dialTCPPort, 5*time.Millisecond, time.Second)
	if err != nil {
		t.Fatalf("wait with empty previous session: %v", err)
	}
}

// Verifies each poll re-reads customPort so a fallback port switch is followed.
func TestWaitForV2ServerReadyFollowsCustomPortChange(t *testing.T) {
	projectRoot := t.TempDir()
	// Bind then close so settings can advertise a real-looking old port that is not reachable.
	oldPort, oldListener := startFakeTCPListener(t)
	if err := oldListener.Close(); err != nil {
		t.Fatalf("close old listener: %v", err)
	}
	newPort, _ := startFakeTCPListener(t)
	writeV2ServerSettingsJSON(t, projectRoot, v2ServerSettingsFileName, v2ServerSettings{
		CustomPort:      oldPort,
		IsServerRunning: true,
		ServerSessionID: "new-session",
	})

	var dialedPorts []int
	var dialMutex sync.Mutex
	dial := func(port int) error {
		dialMutex.Lock()
		dialedPorts = append(dialedPorts, port)
		dialMutex.Unlock()
		return dialTCPPort(port)
	}

	go func() {
		time.Sleep(30 * time.Millisecond)
		writeV2ServerSettingsJSON(t, projectRoot, v2ServerSettingsFileName, v2ServerSettings{
			CustomPort:      newPort,
			IsServerRunning: true,
			ServerSessionID: "new-session",
		})
	}()

	err := waitForV2ServerReady(context.Background(), projectRoot, "old-session", dial, 5*time.Millisecond, time.Second)
	if err != nil {
		t.Fatalf("wait after port change: %v", err)
	}
	dialMutex.Lock()
	defer dialMutex.Unlock()
	sawOldPort := false
	sawNewPort := false
	for _, port := range dialedPorts {
		if port == oldPort {
			sawOldPort = true
		}
		if port == newPort {
			sawNewPort = true
		}
	}
	if !sawOldPort || !sawNewPort {
		t.Fatalf("expected dials of old %d and new %d, dialed %v", oldPort, newPort, dialedPorts)
	}
}
