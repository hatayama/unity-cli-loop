package dispatcher

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/githubapi"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
)

// periodicUpdateFixture is a state in which the periodic self-update does run: self-update is
// enabled, the binary is unmanaged and sits in the install directory, no hold exists and the
// last check is a day old. Each negative test flips exactly one of those conditions.
type periodicUpdateFixture struct {
	t              *testing.T
	cacheRoot      string
	configRoot     string
	installDir     string
	executablePath string
	now            time.Time
	deps           dispatcherRunDeps
	periodicCalls  int
	requiredCalls  int
	runnerCalls    int
	periodicResult string
	periodicErr    error
}

func newPeriodicUpdateFixture(t *testing.T) *periodicUpdateFixture {
	t.Helper()
	enableSelfUpdateForTest(t)
	fixture := &periodicUpdateFixture{
		t:          t,
		cacheRoot:  t.TempDir(),
		configRoot: t.TempDir(),
		installDir: t.TempDir(),
		now:        time.Date(2026, time.November, 17, 12, 0, 0, 0, time.UTC),
	}
	fixture.executablePath = filepath.Join(fixture.installDir, "uloop")
	t.Setenv(nativepath.CacheDirEnvName, fixture.cacheRoot)
	t.Setenv(nativepath.ConfigDirEnvName, fixture.configRoot)
	fixture.useExecutablePath(fixture.executablePath)

	deps := defaultDispatcherRunDeps()
	deps.now = func() time.Time { return fixture.now }
	deps.resolveSelfUpdateLocation = func() (string, string, error) {
		return fixture.executablePath, fixture.installDir, nil
	}
	deps.runPeriodicUpdate = func(context.Context, io.Writer) (string, error) {
		fixture.periodicCalls++
		return fixture.periodicResult, fixture.periodicErr
	}
	deps.runUpdate = func(context.Context) (bool, error) {
		fixture.requiredCalls++
		return true, nil
	}
	deps.runRealCLI = func(context.Context, string, []string, io.Writer, io.Writer) int {
		fixture.runnerCalls++
		return 0
	}
	fixture.deps = deps
	fixture.writeLastChecked(fixture.now.Add(-dispatcherSelfUpdateInterval))
	return fixture
}

// useExecutablePath controls what managed-install detection sees as the running binary.
func (fixture *periodicUpdateFixture) useExecutablePath(executablePath string) {
	previous := resolveUpdateExecutablePathFunc
	fixture.t.Cleanup(func() {
		resolveUpdateExecutablePathFunc = previous
	})
	resolveUpdateExecutablePathFunc = func() (string, error) {
		return executablePath, nil
	}
}

func (fixture *periodicUpdateFixture) writeLastChecked(checkedAt time.Time) {
	fixture.t.Helper()
	content, err := json.Marshal(dispatcherUpdateState{LastChecked: checkedAt.UTC()})
	if err != nil {
		fixture.t.Fatalf("marshal update state: %v", err)
	}
	writeDispatcherTestFile(fixture.t, filepath.Join(fixture.cacheRoot, dispatcherUpdateStateFileName), string(content))
}

func (fixture *periodicUpdateFixture) readLastChecked() time.Time {
	fixture.t.Helper()
	content, err := os.ReadFile(filepath.Join(fixture.cacheRoot, dispatcherUpdateStateFileName))
	if err != nil {
		fixture.t.Fatalf("read update state: %v", err)
	}
	state := dispatcherUpdateState{}
	if err := json.Unmarshal(content, &state); err != nil {
		fixture.t.Fatalf("decode update state: %v", err)
	}
	return state.LastChecked
}

func (fixture *periodicUpdateFixture) writeLockFile(modifiedAt time.Time) string {
	fixture.t.Helper()
	lockPath := filepath.Join(fixture.cacheRoot, dispatcherUpdateLockFileName)
	writeDispatcherTestFile(fixture.t, lockPath, "")
	if err := os.Chtimes(lockPath, modifiedAt, modifiedAt); err != nil {
		fixture.t.Fatalf("set lock time: %v", err)
	}
	return lockPath
}

func (fixture *periodicUpdateFixture) run() (string, string) {
	var stderr bytes.Buffer
	installed := runPeriodicDispatcherSelfUpdate(context.Background(), &stderr, fixture.deps)
	return installed, stderr.String()
}

func TestPeriodicSelfUpdateRunsForDispatcherCommandOutsideProject(t *testing.T) {
	// Verifies a dispatcher-owned command outside any project (no pin) runs the periodic update once when the last check is a day old.
	fixture := newPeriodicUpdateFixture(t)
	t.Chdir(t.TempDir())

	runDispatcherWithDeps(context.Background(), []string{"skills", "list"}, io.Discard, io.Discard, fixture.deps)

	if fixture.periodicCalls != 1 {
		t.Fatalf("expected one periodic update, got %d", fixture.periodicCalls)
	}
}

func TestIsExcludedFromPeriodicSelfUpdate(t *testing.T) {
	// Verifies update, install, uninstall, version and help requests never trigger the periodic update while ordinary commands do.
	cases := []struct {
		args     []string
		excluded bool
	}{
		{args: []string{"update"}, excluded: true},
		{args: []string{"update", "--to-version", "3.0.0"}, excluded: true},
		{args: []string{"install"}, excluded: true},
		{args: []string{"uninstall"}, excluded: true},
		{args: []string{"version"}, excluded: true},
		{args: []string{"--help"}, excluded: true},
		{args: []string{"-h"}, excluded: true},
		{args: []string{"compile", "--help"}, excluded: true},
		{args: []string{"--version"}, excluded: true},
		{args: []string{"-v"}, excluded: true},
		{args: []string{"--version", "--json"}, excluded: true},
		{args: []string{}, excluded: true},
		{args: []string{"launch"}, excluded: false},
		{args: []string{"skills", "list"}, excluded: false},
		{args: []string{"compile"}, excluded: false},
	}
	for _, testCase := range cases {
		t.Run(strings.Join(testCase.args, " "), func(t *testing.T) {
			if got := isExcludedFromPeriodicSelfUpdate(testCase.args); got != testCase.excluded {
				t.Fatalf("excluded mismatch for %v: got %t want %t", testCase.args, got, testCase.excluded)
			}
		})
	}
}

func TestPeriodicSelfUpdateSkipsProjectScopedVersionRequests(t *testing.T) {
	// Verifies project-scoped version requests that reach the runner do not trigger the periodic update, while a tool command on the same project does.
	fixture := newPeriodicUpdateFixture(t)
	projectRoot := createDispatcherUnityProject(t)
	writeDispatcherProjectPin(t, projectRoot, clicontract.ProjectRunnerVersion())
	writeCachedDispatcherRealCLI(t, fixture.cacheRoot, clicontract.ProjectRunnerVersion())
	t.Chdir(t.TempDir())

	for _, args := range [][]string{
		{"--project-path", projectRoot, "--version"},
		{"--project-path", projectRoot, "--version", "--json"},
	} {
		runDispatcherWithDeps(context.Background(), args, io.Discard, io.Discard, fixture.deps)
	}
	if fixture.runnerCalls != 2 {
		t.Fatalf("expected both version requests to reach the runner, got %d", fixture.runnerCalls)
	}
	if fixture.periodicCalls != 0 {
		t.Fatalf("version requests must not trigger the periodic update, got %d", fixture.periodicCalls)
	}

	runDispatcherWithDeps(context.Background(), []string{"--project-path", projectRoot, "compile"}, io.Discard, io.Discard, fixture.deps)
	if fixture.periodicCalls != 1 {
		t.Fatalf("a tool command must trigger the periodic update, got %d", fixture.periodicCalls)
	}
}

func TestPeriodicSelfUpdateRunsInV2Project(t *testing.T) {
	// Verifies commands in a V2 project also run the periodic update before being delegated to the V2 CLI.
	fixture := newPeriodicUpdateFixture(t)
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "abc123", "2.2.0")
	t.Chdir(projectRoot)
	delegated := false
	fixture.deps.runV2CLI = func(context.Context, string, []string, io.Writer, io.Writer) (int, error) {
		delegated = true
		return 0, nil
	}

	runDispatcherWithDeps(context.Background(), []string{"compile"}, io.Discard, io.Discard, fixture.deps)

	if !delegated {
		t.Fatal("expected the V2 CLI to run")
	}
	if fixture.periodicCalls != 1 {
		t.Fatalf("expected one periodic update in a V2 project, got %d", fixture.periodicCalls)
	}
}

func TestPeriodicSelfUpdateHonorsTheInterval(t *testing.T) {
	// Verifies the update does not run 23h59m after the last check and runs exactly 24h after it.
	cases := []struct {
		name      string
		age       time.Duration
		wantCalls int
	}{
		{name: "one minute early", age: dispatcherSelfUpdateInterval - time.Minute, wantCalls: 0},
		{name: "exactly due", age: dispatcherSelfUpdateInterval, wantCalls: 1},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			fixture := newPeriodicUpdateFixture(t)
			fixture.writeLastChecked(fixture.now.Add(-testCase.age))

			fixture.run()

			if fixture.periodicCalls != testCase.wantCalls {
				t.Fatalf("calls mismatch: got %d want %d", fixture.periodicCalls, testCase.wantCalls)
			}
		})
	}
}

func TestPeriodicSelfUpdateSkipsWhenDisabledOrManaged(t *testing.T) {
	// Verifies ULOOP_DISABLE_SELF_UPDATE, Homebrew and winget installs each skip the periodic update silently.
	cases := []struct {
		name  string
		apply func(*periodicUpdateFixture)
	}{
		{name: "disabled by env", apply: func(fixture *periodicUpdateFixture) {
			fixture.t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")
		}},
		{name: "homebrew managed", apply: func(fixture *periodicUpdateFixture) {
			fixture.useExecutablePath("/opt/homebrew/Cellar/uloop/3.0.0/bin/uloop")
		}},
		{name: "winget managed", apply: func(fixture *periodicUpdateFixture) {
			fixture.useExecutablePath(`C:\Program Files\WinGet\Links\uloop.exe`)
		}},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			fixture := newPeriodicUpdateFixture(t)
			testCase.apply(fixture)

			installed, stderr := fixture.run()

			if fixture.periodicCalls != 0 || installed != "" || stderr != "" {
				t.Fatalf("expected a silent skip: calls=%d installed=%q stderr=%s", fixture.periodicCalls, installed, stderr)
			}
		})
	}
}

func TestIsRunningFromInstallDir(t *testing.T) {
	// Verifies the install-directory check compares resolved directories, folds case only on Windows, and rejects an empty install dir.
	installDir := t.TempDir()
	otherDir := t.TempDir()
	executablePath := filepath.Join(installDir, "uloop")
	cases := []struct {
		name           string
		executablePath string
		installDir     string
		goos           string
		want           bool
	}{
		{name: "same directory", executablePath: executablePath, installDir: installDir, goos: runtime.GOOS, want: true},
		{name: "different directory", executablePath: filepath.Join(otherDir, "uloop"), installDir: installDir, goos: runtime.GOOS, want: false},
		{name: "windows ignores case", executablePath: filepath.Join("/Missing/Install", "uloop.exe"), installDir: "/missing/install", goos: "windows", want: true},
		{name: "darwin keeps case", executablePath: filepath.Join("/Missing/Install", "uloop"), installDir: "/missing/install", goos: "darwin", want: false},
		{name: "empty install dir", executablePath: executablePath, installDir: "", goos: runtime.GOOS, want: false},
	}
	linkDir := filepath.Join(t.TempDir(), "link")
	if err := os.Symlink(installDir, linkDir); err == nil {
		cases = append(cases, struct {
			name           string
			executablePath string
			installDir     string
			goos           string
			want           bool
		}{name: "symlinked install dir", executablePath: executablePath, installDir: linkDir, goos: runtime.GOOS, want: true})
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if got := isRunningFromInstallDir(testCase.executablePath, testCase.installDir, testCase.goos); got != testCase.want {
				t.Fatalf("got %t want %t", got, testCase.want)
			}
		})
	}
}

func TestPeriodicSelfUpdateSkipsBinaryOutsideInstallDir(t *testing.T) {
	// Verifies a uloop that does not run from the install directory never updates the global install.
	fixture := newPeriodicUpdateFixture(t)
	fixture.executablePath = filepath.Join(t.TempDir(), "uloop")

	fixture.run()

	if fixture.periodicCalls != 0 {
		t.Fatalf("expected no update outside the install dir, got %d", fixture.periodicCalls)
	}
}

func TestPeriodicSelfUpdateRespectsTheUpdateLock(t *testing.T) {
	// Verifies a fresh lock blocks the update, while a 16-minute-old lock is treated as abandoned and removed after the update.
	t.Run("fresh lock", func(t *testing.T) {
		fixture := newPeriodicUpdateFixture(t)
		fixture.writeLockFile(fixture.now.Add(-time.Minute))

		fixture.run()

		if fixture.periodicCalls != 0 {
			t.Fatalf("a held lock must block the update, got %d", fixture.periodicCalls)
		}
	})
	t.Run("abandoned lock", func(t *testing.T) {
		fixture := newPeriodicUpdateFixture(t)
		lockPath := fixture.writeLockFile(fixture.now.Add(-16 * time.Minute))

		fixture.run()

		if fixture.periodicCalls != 1 {
			t.Fatalf("an abandoned lock must not block the update, got %d", fixture.periodicCalls)
		}
		if _, err := os.Stat(lockPath); !errors.Is(err, os.ErrNotExist) {
			t.Fatalf("expected the lock to be released, stat err=%v", err)
		}
	})
}

func TestPeriodicSelfUpdateSkipsSilentlyWhenCacheRootCannotBeCreated(t *testing.T) {
	// Verifies an unusable cache root (a regular file in its place) skips the update without printing a warning.
	fixture := newPeriodicUpdateFixture(t)
	blockedRoot := filepath.Join(t.TempDir(), "cache")
	writeDispatcherTestFile(t, blockedRoot, "not a directory")
	t.Setenv(nativepath.CacheDirEnvName, blockedRoot)

	_, stderr := fixture.run()

	if fixture.periodicCalls != 0 || stderr != "" {
		t.Fatalf("expected a silent skip: calls=%d stderr=%s", fixture.periodicCalls, stderr)
	}
}

func TestPeriodicSelfUpdateDoesNotCheckWhenTheRecordCannotBeWritten(t *testing.T) {
	// Verifies the lookup never runs when the check record cannot be written, so an unwritable record cannot cause a lookup on every command.
	fixture := newPeriodicUpdateFixture(t)
	statePath := filepath.Join(fixture.cacheRoot, dispatcherUpdateStateFileName)
	if err := os.Remove(statePath); err != nil {
		t.Fatalf("remove state: %v", err)
	}
	if err := os.MkdirAll(filepath.Join(statePath, "occupied"), 0o755); err != nil {
		t.Fatalf("block state path: %v", err)
	}

	fixture.run()

	if fixture.periodicCalls != 0 {
		t.Fatalf("expected no lookup without a written record, got %d", fixture.periodicCalls)
	}
}

func TestPeriodicSelfUpdateRecordsTheCheckBeforeLookingUp(t *testing.T) {
	// Verifies the check record already holds the current time while the lookup is running.
	fixture := newPeriodicUpdateFixture(t)
	var recordedDuringLookup time.Time
	fixture.deps.runPeriodicUpdate = func(context.Context, io.Writer) (string, error) {
		fixture.periodicCalls++
		recordedDuringLookup = fixture.readLastChecked()
		return "", nil
	}

	fixture.run()

	if fixture.periodicCalls != 1 {
		t.Fatalf("expected one lookup, got %d", fixture.periodicCalls)
	}
	if !recordedDuringLookup.Equal(fixture.now) {
		t.Fatalf("record during lookup = %s, want %s", recordedDuringLookup, fixture.now)
	}
}

func TestPeriodicSelfUpdateRechecksDueAfterTakingTheLock(t *testing.T) {
	// Verifies that when another process records a check while this one takes the lock, this process does not look up again.
	fixture := newPeriodicUpdateFixture(t)
	fixture.deps.acquireUpdateLock = func(cacheRoot string, now time.Time) (func(), dispatcherUpdateLockResult) {
		release, result := tryAcquireDispatcherUpdateLock(cacheRoot, now)
		fixture.writeLastChecked(fixture.now)
		return release, result
	}

	fixture.run()

	if fixture.periodicCalls != 0 {
		t.Fatalf("expected no lookup after another process checked, got %d", fixture.periodicCalls)
	}
}

func TestPeriodicSelfUpdateFailureWarnsOnceAndThrottles(t *testing.T) {
	// Verifies a failed update prints one warning, records the check, and does not run again on the next command.
	fixture := newPeriodicUpdateFixture(t)
	fixture.periodicErr = errors.New("network unavailable")

	_, stderr := fixture.run()

	if stderr != "warning: dispatcher self-update skipped: network unavailable\n" {
		t.Fatalf("unexpected warning output: %q", stderr)
	}
	if !fixture.readLastChecked().Equal(fixture.now) {
		t.Fatal("expected the failed check to be recorded")
	}
	_, stderr = fixture.run()
	if fixture.periodicCalls != 1 || stderr != "" {
		t.Fatalf("expected the second command to skip silently: calls=%d stderr=%s", fixture.periodicCalls, stderr)
	}
}

func TestPeriodicSelfUpdateRateLimitSuggestsToken(t *testing.T) {
	// Verifies a rate-limited lookup adds the token hint after the skip warning.
	fixture := newPeriodicUpdateFixture(t)
	fixture.periodicErr = fmt.Errorf("list releases: %w", githubapi.RateLimitError{})

	_, stderr := fixture.run()

	if !strings.Contains(stderr, "rate limit exhausted") || !strings.Contains(stderr, "GH_TOKEN") {
		t.Fatalf("expected rate-limit warning with token hint, got: %s", stderr)
	}
}

func TestPeriodicSelfUpdateReportsTheInstalledVersion(t *testing.T) {
	// Verifies an installed release is announced and returned, while a current dispatcher prints nothing.
	t.Run("installed", func(t *testing.T) {
		fixture := newPeriodicUpdateFixture(t)
		fixture.periodicResult = "9.9.9"

		installed, stderr := fixture.run()

		if installed != "9.9.9" {
			t.Fatalf("installed version mismatch: %q", installed)
		}
		expected := "uloop: dispatcher updated from " + dispatcherVersion + " to 9.9.9"
		if !strings.Contains(stderr, expected) {
			t.Fatalf("missing completion notice: %s", stderr)
		}
	})
	t.Run("already current", func(t *testing.T) {
		fixture := newPeriodicUpdateFixture(t)

		installed, stderr := fixture.run()

		if fixture.periodicCalls != 1 || installed != "" || stderr != "" {
			t.Fatalf("expected a silent no-op: calls=%d installed=%q stderr=%s", fixture.periodicCalls, installed, stderr)
		}
	})
}

// stubPeriodicLookupAndInstaller replaces the release lookup and the installer for runPeriodicDispatcherUpdateForOS.
func stubPeriodicLookupAndInstaller(t *testing.T, targetVersion string, installer func(context.Context, update.Command, io.Writer, io.Writer) error) *context.Context {
	t.Helper()
	previousResolver := resolveUpdateTargetVersionFunc
	previousRunner := updateRunCommand
	t.Cleanup(func() {
		resolveUpdateTargetVersionFunc = previousResolver
		updateRunCommand = previousRunner
	})
	var lookupCtx context.Context
	resolveUpdateTargetVersionFunc = func(ctx context.Context, options update.Options) (update.Options, error) {
		lookupCtx = ctx
		options.TargetVersion = targetVersion
		return options, nil
	}
	updateRunCommand = installer
	return &lookupCtx
}

func TestPeriodicDispatcherUpdateSkipsInstallerWhenTargetIsCurrent(t *testing.T) {
	// Verifies a current release skips the installer and reports nothing installed.
	stubPeriodicLookupAndInstaller(t, "v"+dispatcherVersion, func(context.Context, update.Command, io.Writer, io.Writer) error {
		t.Fatal("the installer must not run when the target is current")
		return nil
	})
	var stderr bytes.Buffer

	installed, err := runPeriodicDispatcherUpdateForOS(context.Background(), &stderr, "darwin")

	if err != nil || installed != "" || stderr.Len() != 0 {
		t.Fatalf("expected a silent no-op: installed=%q err=%v stderr=%s", installed, err, stderr.String())
	}
}

func TestPeriodicDispatcherUpdateBoundsOnlyTheLookup(t *testing.T) {
	// Verifies the lookup gets a deadline within the lookup timeout while the installer receives a context without a deadline.
	var installerCtx context.Context
	startedAt := time.Now()
	lookupCtx := stubPeriodicLookupAndInstaller(t, "999.0.0", func(ctx context.Context, _ update.Command, _ io.Writer, _ io.Writer) error {
		installerCtx = ctx
		return nil
	})

	if _, err := runPeriodicDispatcherUpdateForOS(context.Background(), io.Discard, "darwin"); err != nil {
		t.Fatalf("update failed: %v", err)
	}

	deadline, ok := (*lookupCtx).Deadline()
	if !ok {
		t.Fatal("the lookup must have a deadline")
	}
	if deadline.After(startedAt.Add(periodicUpdateLookupTimeout + time.Second)) {
		t.Fatalf("lookup deadline %s exceeds the lookup timeout", deadline)
	}
	if _, ok := installerCtx.Deadline(); ok {
		t.Fatal("the installer must not receive a deadline")
	}
}

func TestPeriodicDispatcherUpdateReturnsTheTargetVersion(t *testing.T) {
	// Verifies a successful install returns the target version even when re-reading the installed binary would fail, and announces the install first.
	previousReader := dispatcherReadInstalledVersion
	t.Cleanup(func() {
		dispatcherReadInstalledVersion = previousReader
	})
	dispatcherReadInstalledVersion = func(context.Context) (string, error) {
		return "", errors.New("cannot run the installed binary")
	}
	var stderr bytes.Buffer
	announcedBeforeInstall := false
	stubPeriodicLookupAndInstaller(t, "999.0.0", func(context.Context, update.Command, io.Writer, io.Writer) error {
		announcedBeforeInstall = strings.Contains(stderr.String(), "updating the dispatcher from "+dispatcherVersion+" to 999.0.0")
		return nil
	})

	installed, err := runPeriodicDispatcherUpdateForOS(context.Background(), &stderr, "darwin")

	if err != nil || installed != "999.0.0" {
		t.Fatalf("expected the target version: installed=%q err=%v", installed, err)
	}
	if !announcedBeforeInstall {
		t.Fatalf("expected the update notice before the installer ran: %s", stderr.String())
	}
}

func TestUpdateInstallerEnvironmentDisablesNestedSelfUpdate(t *testing.T) {
	// Verifies the installer child process runs with self-update disabled even when the parent environment enabled it.
	env := updateInstallerEnvironment([]string{dispatcherDisableSelfUpdateEnvName + "=0"}, update.Command{}, "manifest")

	if env[len(env)-1] != dispatcherDisableSelfUpdateEnvName+"=1" {
		t.Fatalf("expected the disable switch to be the last entry, got %v", env)
	}
}

// writePinnedProjectWithMinimum creates a V3 project whose pin requires the given dispatcher version.
func writePinnedProjectWithMinimum(t *testing.T, fixture *periodicUpdateFixture, minimumVersion string) string {
	t.Helper()
	projectRoot := createDispatcherUnityProject(t)
	pinPath := filepath.Join(projectRoot, dispatcherProjectPinRelativePath)
	writeDispatcherPinFileWithMinimum(t, pinPath, clicontract.ProjectRunnerVersion(), minimumVersion)
	writeCachedDispatcherRealCLI(t, fixture.cacheRoot, clicontract.ProjectRunnerVersion())
	t.Chdir(projectRoot)
	previousReader := dispatcherReadInstalledVersion
	t.Cleanup(func() {
		dispatcherReadInstalledVersion = previousReader
	})
	dispatcherReadInstalledVersion = func(context.Context) (string, error) {
		return "999.0.0", nil
	}
	return projectRoot
}

func TestRequiredUpdateDoesNotReinstallAfterPeriodicUpdate(t *testing.T) {
	// Verifies that when the periodic update installed a release below-floor commands stop with CLI_UPDATE_REQUIRED without a second install.
	fixture := newPeriodicUpdateFixture(t)
	writePinnedProjectWithMinimum(t, fixture, "999.0.0")
	fixture.periodicResult = "999.0.0"
	var stderr bytes.Buffer

	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, io.Discard, &stderr, fixture.deps)

	if code != 1 || fixture.periodicCalls != 1 || fixture.requiredCalls != 0 || fixture.runnerCalls != 0 {
		t.Fatalf("expected one install and a retry request: code=%d periodic=%d required=%d runner=%d", code, fixture.periodicCalls, fixture.requiredCalls, fixture.runnerCalls)
	}
	if !strings.Contains(stderr.String(), clierrors.ErrorCodeCLIUpdateRequired) {
		t.Fatalf("expected CLI_UPDATE_REQUIRED: %s", stderr.String())
	}
}

func TestPeriodicUpdateAloneRunsWhenTheFloorIsMet(t *testing.T) {
	// Verifies a project whose floor is already met gets only the periodic update and then runs the command.
	fixture := newPeriodicUpdateFixture(t)
	writePinnedProjectWithMinimum(t, fixture, dispatcherVersion)
	fixture.periodicResult = "999.0.0"

	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, io.Discard, io.Discard, fixture.deps)

	if code != 0 || fixture.periodicCalls != 1 || fixture.requiredCalls != 0 || fixture.runnerCalls != 1 {
		t.Fatalf("expected one periodic update then the command: code=%d periodic=%d required=%d runner=%d", code, fixture.periodicCalls, fixture.requiredCalls, fixture.runnerCalls)
	}
}

func TestRequiredUpdateWaitsForAnotherProcessHoldingTheLock(t *testing.T) {
	// Verifies a below-floor command does not install while another process holds the update lock and asks for a retry instead.
	fixture := newPeriodicUpdateFixture(t)
	writePinnedProjectWithMinimum(t, fixture, "999.0.0")
	fixture.writeLastChecked(fixture.now)
	fixture.writeLockFile(fixture.now.Add(-time.Minute))
	var stderr bytes.Buffer

	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, io.Discard, &stderr, fixture.deps)

	if code != 1 || fixture.requiredCalls != 0 {
		t.Fatalf("expected no install while the lock is held: code=%d required=%d", code, fixture.requiredCalls)
	}
	for _, expected := range []string{clierrors.ErrorCodeCLIUpdateRequired, "Another uloop command is updating the dispatcher", `"Retryable": true`} {
		if !strings.Contains(stderr.String(), expected) {
			t.Fatalf("missing %q in: %s", expected, stderr.String())
		}
	}
}

func TestRequiredUpdateStillRunsWithoutAUsableLock(t *testing.T) {
	// Verifies an unwritable cache root does not stop the required update, the only way past the pin's floor.
	fixture := newPeriodicUpdateFixture(t)
	writePinnedProjectWithMinimum(t, fixture, "999.0.0")
	blockedRoot := filepath.Join(t.TempDir(), "cache")
	writeDispatcherTestFile(t, blockedRoot, "not a directory")
	t.Setenv(nativepath.CacheDirEnvName, blockedRoot)

	var stderr bytes.Buffer
	handled, code := enforceDispatcherFreshnessWithDeps(context.Background(), dispatcherPin{MinimumDispatcherVersion: "999.0.0"}, "", &stderr, fixture.deps)

	if !handled || code != 1 || fixture.requiredCalls != 1 {
		t.Fatalf("expected the required update to run: handled=%t code=%d required=%d stderr=%s", handled, code, fixture.requiredCalls, stderr.String())
	}
}
