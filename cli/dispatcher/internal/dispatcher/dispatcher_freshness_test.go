package dispatcher

import (
	"bytes"
	"context"
	"errors"
	"io"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
)

func unsetDispatcherCacheRoot(t *testing.T) {
	t.Helper()
	if runtime.GOOS == "windows" {
		t.Skip("Windows resolves the cache root from LOCALAPPDATA, not HOME.")
	}
	t.Setenv(nativepath.CacheDirEnvName, "")
	t.Setenv("XDG_CACHE_HOME", "")
	t.Setenv("HOME", "")
}

func TestDetectManagedDispatcherInstallIgnoresExecutablePathFailure(t *testing.T) {
	// Verifies an executable path lookup failure is treated as an unmanaged install so ordinary commands keep running.
	previous := resolveUpdateExecutablePathFunc
	t.Cleanup(func() {
		resolveUpdateExecutablePathFunc = previous
	})
	// The path would be detected as Homebrew-managed if the error were ignored.
	resolveUpdateExecutablePathFunc = func() (string, error) {
		return "/opt/homebrew/Cellar/uloop/1.0.0/bin/uloop", errors.New("executable path unavailable")
	}

	if detectManagedDispatcherInstall().IsManaged() {
		t.Fatal("a failed executable lookup must not report a managed install")
	}
}

func TestExecuteDispatcherFreshnessPlanRejectsUnknownAction(t *testing.T) {
	// Verifies an unknown freshness action fails fast with a routing-bug envelope instead of running the command.
	var stderr bytes.Buffer

	handled, code := executeDispatcherFreshnessPlan(context.Background(), dispatcherFreshnessPlan{Action: "unexpected"}, &stderr, defaultDispatcherRunDeps())

	if !handled || code != 1 {
		t.Fatalf("result mismatch: handled=%t code=%d", handled, code)
	}
	if !strings.Contains(stderr.String(), "Dispatcher freshness routing bug: unknown action: unexpected") {
		t.Fatalf("missing routing bug message: %s", stderr.String())
	}
}

func TestRunDispatcherFreshnessUpdateTurnsFailedRequiredUpdateIntoManualUpdate(t *testing.T) {
	// Verifies a failed required update stops the command and tells the user to run `uloop update` with the cause.
	t.Setenv(nativepath.CacheDirEnvName, t.TempDir())
	deps := defaultDispatcherRunDeps()
	deps.runUpdate = func(context.Context) (bool, error) {
		return false, errors.New("network unavailable")
	}

	var stderr bytes.Buffer
	handled, code := runDispatcherFreshnessUpdate(
		context.Background(),
		dispatcherFreshnessPlan{Action: dispatcherFreshnessRunRequiredUpdate, MinimumVersion: "999.0.0"},
		&stderr,
		deps)

	if !handled || code != 1 {
		t.Fatalf("result mismatch: handled=%t code=%d", handled, code)
	}
	for _, expected := range []string{"999.0.0", "Automatic update failed: network unavailable", "Run `uloop update` and retry the command."} {
		if !strings.Contains(stderr.String(), expected) {
			t.Fatalf("missing %q in envelope: %s", expected, stderr.String())
		}
	}
}

func TestDispatcherSelfUpdateDueWithDeps(t *testing.T) {
	// Verifies the optional update check is due when the state file is missing or corrupt and not due right after a check.
	now := time.Date(2026, 1, 2, 3, 4, 5, 0, time.UTC)
	deps := defaultDispatcherRunDeps()
	deps.now = func() time.Time { return now }

	cases := []struct {
		name    string
		content string
		wantDue bool
	}{
		{name: "missing state", wantDue: true},
		{name: "corrupt state", content: "{", wantDue: true},
		{name: "recent check", content: `{"lastChecked":"` + now.Add(-time.Hour).Format(time.RFC3339) + `"}`, wantDue: false},
		{name: "stale check", content: `{"lastChecked":"` + now.Add(-25*time.Hour).Format(time.RFC3339) + `"}`, wantDue: true},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			cacheRoot := t.TempDir()
			t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
			if testCase.content != "" {
				writeDispatcherTestFile(t, filepath.Join(cacheRoot, dispatcherUpdateStateFileName), testCase.content)
			}

			if due := dispatcherSelfUpdateDueWithDeps(deps); due != testCase.wantDue {
				t.Fatalf("due mismatch: got %t want %t", due, testCase.wantDue)
			}
		})
	}
}

func TestDispatcherSelfUpdateIsNotDueWithoutCacheRoot(t *testing.T) {
	// Verifies an unresolvable cache root skips the optional update check instead of updating on every command.
	unsetDispatcherCacheRoot(t)

	if dispatcherSelfUpdateDueWithDeps(defaultDispatcherRunDeps()) {
		t.Fatal("update must not be due when the cache root cannot be resolved")
	}
}

func TestMarkDispatcherSelfUpdateCheckedWritesState(t *testing.T) {
	// Verifies a completed check records the current time so the next check is throttled.
	cacheRoot := filepath.Join(t.TempDir(), "nested", "cache")
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	now := time.Date(2026, 1, 2, 3, 4, 5, 0, time.UTC)
	deps := defaultDispatcherRunDeps()
	deps.now = func() time.Time { return now }

	markDispatcherSelfUpdateCheckedWithDeps(deps)

	assertFileContent(t, filepath.Join(cacheRoot, dispatcherUpdateStateFileName), `{"lastChecked":"2026-01-02T03:04:05Z"}`)
	if dispatcherSelfUpdateDueWithDeps(deps) {
		t.Fatal("update must not be due right after it was marked checked")
	}
}

func TestRunDispatcherUpdateCommandForOSReportsFailures(t *testing.T) {
	// Verifies target resolution, unsupported platform, and installer failures are returned without reporting an update.
	cases := []struct {
		name        string
		goos        string
		resolveErr  error
		runErr      error
		wantMessage string
	}{
		{name: "resolution failure", goos: "linux", resolveErr: errors.New("release lookup failed"), wantMessage: "release lookup failed"},
		{name: "unsupported platform", goos: "plan9", wantMessage: "only supported on macOS, Linux, and Windows"},
		{name: "installer failure", goos: "linux", runErr: errors.New("installer exited 1"), wantMessage: "installer exited 1"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			stubDispatcherUpdateRun(t, testCase.resolveErr, testCase.runErr)

			updated, err := runDispatcherUpdateCommandForOS(context.Background(), testCase.goos)

			if updated {
				t.Fatal("a failed update must not report an installed update")
			}
			if err == nil || !strings.Contains(err.Error(), testCase.wantMessage) {
				t.Fatalf("expected error containing %q, got %v", testCase.wantMessage, err)
			}
		})
	}
}

func stubDispatcherUpdateRun(t *testing.T, resolveErr error, runErr error) {
	t.Helper()
	previousResolver := resolveUpdateTargetVersionFunc
	previousRunner := updateRunCommand
	t.Cleanup(func() {
		resolveUpdateTargetVersionFunc = previousResolver
		updateRunCommand = previousRunner
	})
	resolveUpdateTargetVersionFunc = func(_ context.Context, options update.Options) (update.Options, error) {
		options.TargetVersion = "999.0.0"
		return options, resolveErr
	}
	updateRunCommand = func(context.Context, update.Command, io.Writer, io.Writer) error {
		return runErr
	}
}

func TestRunDispatcherUpdateCommandForOSReportsInstalledUpdate(t *testing.T) {
	// Verifies a newer resolved target runs the installer and reports that an update was installed.
	stubDispatcherUpdateRun(t, nil, nil)

	updated, err := runDispatcherUpdateCommandForOS(context.Background(), "linux")

	if err != nil || !updated {
		t.Fatalf("expected an installed update, got updated=%t err=%v", updated, err)
	}
}
