package dispatcher

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net"
	"os"
	"path/filepath"
	"strconv"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/ui"
)

const (
	v2UserSettingsDirectoryName = "UserSettings"
	v2ServerSettingsFileName    = "UnityMcpSettings.json"
	v2ServerSettingsTmpFileName = "UnityMcpSettings.json.tmp"
	v2CompilingLockFileName     = "compiling.lock"
	v2DomainReloadLockFileName  = "domainreload.lock"
)

// v2ServerSettings holds the V2 Unity package server state written to UserSettings/UnityMcpSettings.json.
type v2ServerSettings struct {
	CustomPort      int    `json:"customPort"`
	IsServerRunning bool   `json:"isServerRunning"`
	ServerSessionID string `json:"serverSessionId"`
}

type v2ServerReadyTimeoutError struct {
	projectRoot string
}

func (err v2ServerReadyTimeoutError) Error() string {
	return fmt.Sprintf("timed out waiting for V2 uloop server readiness in %s", err.projectRoot)
}

func (err v2ServerReadyTimeoutError) ToCLIError(context clierrors.ErrorContext) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeUnityStartupTimeout,
		Phase:       clierrors.ErrorPhaseConnection,
		Message:     "Unity opened the V2 project, but the V2 uloop server did not become reachable before the launch timeout.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: []string{
			"Check whether the uLoopMCP server is running in the Unity Editor window for this project.",
			"Check whether another process is holding the server port and forcing the V2 server into a retry loop.",
			"Retry with uloop launch -r to restart Unity and the V2 server.",
		},
		Details: map[string]any{
			"ProjectRoot":    err.projectRoot,
			"TimeoutSeconds": int(launchReadinessTimeout.Seconds()),
		},
	}
}

// readV2ServerSettings reads V2 server state from UnityMcpSettings.json, falling back to .json.tmp only.
func readV2ServerSettings(projectRoot string) (v2ServerSettings, error) {
	candidates := []string{
		filepath.Join(projectRoot, v2UserSettingsDirectoryName, v2ServerSettingsFileName),
		filepath.Join(projectRoot, v2UserSettingsDirectoryName, v2ServerSettingsTmpFileName),
	}
	var lastErr error
	for _, candidatePath := range candidates {
		settings, err := readV2ServerSettingsFile(candidatePath)
		if err == nil {
			return settings, nil
		}
		lastErr = err
	}
	if lastErr == nil {
		lastErr = fmt.Errorf("V2 server settings not found under %s", filepath.Join(projectRoot, v2UserSettingsDirectoryName))
	}
	return v2ServerSettings{}, lastErr
}

func readV2ServerSettingsFile(path string) (v2ServerSettings, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return v2ServerSettings{}, err
	}
	data = bytes.TrimPrefix(data, []byte("\xef\xbb\xbf"))
	var settings v2ServerSettings
	if unmarshalErr := json.Unmarshal(data, &settings); unmarshalErr != nil {
		return v2ServerSettings{}, unmarshalErr
	}
	return settings, nil
}

// isV2ProjectBusy reports compile or domain-reload locks. serverstarting.lock is intentionally ignored:
// V2 CLI documents that the startup lock can outlive the listener and waiting on it reintroduces false busy;
// sessionId generation already requires SaveRunningServerSession (listen success), so skipping this lock does
// not allow "ready but unreachable" — it only means commands may be cold for a few seconds before prewarm.
func isV2ProjectBusy(projectRoot string) bool {
	compilingLockPath := filepath.Join(projectRoot, launchTempDirectoryName, v2CompilingLockFileName)
	domainReloadLockPath := filepath.Join(projectRoot, launchTempDirectoryName, v2DomainReloadLockFileName)
	if _, err := os.Stat(compilingLockPath); err == nil {
		return true
	}
	if _, err := os.Stat(domainReloadLockPath); err == nil {
		return true
	}
	return false
}

func defaultV2ServerDial(port int) error {
	connection, err := net.DialTimeout(
		"tcp",
		net.JoinHostPort("127.0.0.1", strconv.Itoa(port)),
		launchV2ServerDialTimeout,
	)
	if err != nil {
		return err
	}
	return connection.Close()
}

// waitForV2ServerReady polls until the V2 TCP server is reachable for a new serverSessionId generation
// while compile/domain-reload locks are absent.
//
// Why we only TCP-connect (no V2 JSON-RPC ping): the existing-Editor path cannot prove the listener on the
// settings port belongs to this project without implementing V2's framed JSON-RPC ping client in the
// dispatcher — cost that is not justified for launch readiness. Fresh-launch / -r paths already identify
// the session via serverSessionId generation compare. Do not claim "V2 CLI also only connects"; V2 CLI
// does ping-based identification.
func waitForV2ServerReady(
	ctx context.Context,
	projectRoot string,
	previousServerSessionID string,
	dial func(port int) error,
	poll time.Duration,
	timeout time.Duration,
) error {
	if dial == nil {
		dial = defaultV2ServerDial
	}
	timeoutContext, cancel := context.WithTimeout(ctx, timeout)
	defer cancel()
	ticker := time.NewTicker(poll)
	defer ticker.Stop()

	for {
		if isV2ServerReadyNow(projectRoot, previousServerSessionID, dial) {
			return nil
		}

		select {
		case <-timeoutContext.Done():
			if ctx.Err() != nil {
				return ctx.Err()
			}
			return v2ServerReadyTimeoutError{projectRoot: projectRoot}
		case <-ticker.C:
		}
	}
}

func isV2ServerReadyNow(projectRoot string, previousServerSessionID string, dial func(port int) error) bool {
	settings, err := readV2ServerSettings(projectRoot)
	if err != nil {
		return false
	}
	if !settings.IsServerRunning {
		return false
	}
	if settings.ServerSessionID == "" {
		return false
	}
	if settings.ServerSessionID == previousServerSessionID {
		return false
	}
	if dial(settings.CustomPort) != nil {
		return false
	}
	return !isV2ProjectBusy(projectRoot)
}

func readPreviousV2ServerSessionID(projectRoot string) string {
	settings, err := readV2ServerSettings(projectRoot)
	if err != nil {
		return ""
	}
	return settings.ServerSessionID
}

func waitForExistingV2LaunchReadiness(ctx context.Context, projectRoot string, pid int, stdout io.Writer, stderr io.Writer, deps launchDeps) int {
	logLaunchExistingFocusWithDeps(ctx, projectRoot, pid, deps)
	spinner := ui.NewLaunchSpinner(stdout, stderr)
	defer spinner.Stop()
	writeLaunchReadinessWait(stdout, spinner)
	if err := deps.waitForV2ServerReady(ctx, projectRoot, "", launchV2ServerReadyPoll, launchReadinessTimeout); err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
		return 1
	}
	spinner.Stop()
	return writeExistingV2LaunchReadyResponse(stdout, stderr, projectRoot, pid)
}
