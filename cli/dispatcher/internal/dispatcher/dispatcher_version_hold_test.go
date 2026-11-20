package dispatcher

import (
	"bytes"
	"context"
	"errors"
	"io"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
)

// versionHoldUpdateFixture stubs the release lookup, the installer and the executable path for
// `uloop update`, and gives the test its own config directory.
type versionHoldUpdateFixture struct {
	t              *testing.T
	configRoot     string
	targetVersion  string
	resolveErr     error
	installerErr   error
	installerCalls int
	resolverCalls  int
	holdDuringRun  *dispatcherVersionHoldContent
	holdFileExists bool
}

func newVersionHoldUpdateFixture(t *testing.T) *versionHoldUpdateFixture {
	t.Helper()
	fixture := &versionHoldUpdateFixture{t: t, configRoot: t.TempDir(), targetVersion: "999.0.0"}
	t.Setenv(nativepath.ConfigDirEnvName, fixture.configRoot)
	previousResolver := resolveUpdateTargetVersionFunc
	previousRunner := updateRunCommand
	previousExecutablePath := resolveUpdateExecutablePathFunc
	previousReader := dispatcherReadInstalledVersion
	t.Cleanup(func() {
		resolveUpdateTargetVersionFunc = previousResolver
		updateRunCommand = previousRunner
		resolveUpdateExecutablePathFunc = previousExecutablePath
		dispatcherReadInstalledVersion = previousReader
	})
	resolveUpdateTargetVersionFunc = func(_ context.Context, options update.Options) (update.Options, error) {
		fixture.resolverCalls++
		if options.TargetVersion == "" {
			options.TargetVersion = fixture.targetVersion
		}
		return options, fixture.resolveErr
	}
	updateRunCommand = func(context.Context, update.Command, io.Writer, io.Writer) error {
		fixture.installerCalls++
		held, version := readDispatcherVersionHold()
		fixture.holdFileExists = held
		if held {
			fixture.holdDuringRun = &dispatcherVersionHoldContent{Version: version}
		}
		return fixture.installerErr
	}
	resolveUpdateExecutablePathFunc = func() (string, error) {
		return "/tmp/uloop", nil
	}
	dispatcherReadInstalledVersion = func(context.Context) (string, error) {
		return fixture.targetVersion, nil
	}
	return fixture
}

func (fixture *versionHoldUpdateFixture) holdPath() string {
	return filepath.Join(fixture.configRoot, dispatcherVersionHoldFileName)
}

func (fixture *versionHoldUpdateFixture) writeHold(content string) {
	fixture.t.Helper()
	writeDispatcherTestFile(fixture.t, fixture.holdPath(), content)
}

func (fixture *versionHoldUpdateFixture) assertNoHold() {
	fixture.t.Helper()
	if _, err := os.Stat(fixture.holdPath()); !errors.Is(err, os.ErrNotExist) {
		fixture.t.Fatalf("expected no hold file, stat err=%v", err)
	}
}

func runUpdateForHoldTest(args ...string) (int, string, string) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	_, code := tryHandleUpdateRequest(context.Background(), append([]string{clicore.UpdateCommandName}, args...), &stdout, &stderr)
	return code, stdout.String(), stderr.String()
}

func TestUpdateToVersionRecordsTheHoldBeforeTheInstaller(t *testing.T) {
	// Verifies `uloop update --to-version X` writes the hold before the installer runs and reports it afterwards.
	fixture := newVersionHoldUpdateFixture(t)

	code, stdout, stderr := runUpdateForHoldTest("--to-version", "999.0.0")

	if code != 0 || fixture.installerCalls != 1 {
		t.Fatalf("expected a successful install: code=%d installer=%d stderr=%s", code, fixture.installerCalls, stderr)
	}
	if fixture.holdDuringRun == nil || fixture.holdDuringRun.Version != "999.0.0" {
		t.Fatalf("expected the hold to be written before the installer, got %+v", fixture.holdDuringRun)
	}
	if !strings.Contains(stdout, "uloop is held at 999.0.0; automatic updates are paused.") {
		t.Fatalf("missing hold notice: %s", stdout)
	}
}

func TestUpdateToVersionRestoresTheHoldWhenTheInstallerFails(t *testing.T) {
	// Verifies a failed install removes a new hold, or puts back the previous one exactly.
	t.Run("no previous hold", func(t *testing.T) {
		fixture := newVersionHoldUpdateFixture(t)
		fixture.installerErr = errors.New("installer exited 1")

		code, _, _ := runUpdateForHoldTest("--to-version", "999.0.0")

		if code != 1 || fixture.holdDuringRun == nil {
			t.Fatalf("expected a failed install after recording the hold: code=%d hold=%+v", code, fixture.holdDuringRun)
		}
		fixture.assertNoHold()
	})
	t.Run("previous hold", func(t *testing.T) {
		fixture := newVersionHoldUpdateFixture(t)
		fixture.installerErr = errors.New("installer exited 1")
		fixture.writeHold(`{"version":"3.1.0"}`)

		code, _, _ := runUpdateForHoldTest("--to-version", "999.0.0")

		if code != 1 || fixture.holdDuringRun == nil || fixture.holdDuringRun.Version != "999.0.0" {
			t.Fatalf("expected a failed install after recording the new hold: code=%d hold=%+v", code, fixture.holdDuringRun)
		}
		assertFileContent(t, fixture.holdPath(), `{"version":"3.1.0"}`)
	})
}

func TestUpdateToVersionLeavesNoHoldWhenItFailsBeforeInstalling(t *testing.T) {
	// Verifies a failed release lookup or an unsupported platform leaves no hold behind.
	t.Run("lookup failure", func(t *testing.T) {
		fixture := newVersionHoldUpdateFixture(t)
		fixture.resolveErr = errors.New("release not found")

		code, _, _ := runUpdateForHoldTest("--to-version", "999.0.0")

		if code != 1 || fixture.installerCalls != 0 {
			t.Fatalf("expected a failure before installing: code=%d installer=%d", code, fixture.installerCalls)
		}
		fixture.assertNoHold()
	})
	t.Run("unsupported platform", func(t *testing.T) {
		fixture := newVersionHoldUpdateFixture(t)
		var stderr bytes.Buffer

		code := runDispatcherUpdateRequestForOS(context.Background(), updateOptions{targetVersion: "999.0.0"}, "plan9", io.Discard, &stderr)

		if code != 1 || fixture.installerCalls != 0 {
			t.Fatalf("expected a failure before installing: code=%d installer=%d stderr=%s", code, fixture.installerCalls, stderr.String())
		}
		fixture.assertNoHold()
	})
}

func TestUpdateToVersionFailsWhenTheHoldCannotBeWritten(t *testing.T) {
	// Verifies an unwritable config directory stops the request with exit 1 before the installer runs.
	fixture := newVersionHoldUpdateFixture(t)
	blockedRoot := filepath.Join(t.TempDir(), "config")
	writeDispatcherTestFile(t, blockedRoot, "not a directory")
	t.Setenv(nativepath.ConfigDirEnvName, blockedRoot)

	code, _, stderr := runUpdateForHoldTest("--to-version", "999.0.0")

	if code != 1 || fixture.installerCalls != 0 {
		t.Fatalf("expected exit 1 without installing: code=%d installer=%d", code, fixture.installerCalls)
	}
	if !strings.Contains(stderr, "Could not record the version hold") {
		t.Fatalf("missing hold error: %s", stderr)
	}
}

func TestUpdateToRunningVersionRecordsTheHoldWithoutInstalling(t *testing.T) {
	// Verifies --to-version with the running version records the hold and prints the hold notice without running the installer.
	fixture := newVersionHoldUpdateFixture(t)

	code, stdout, _ := runUpdateForHoldTest("--to-version", dispatcherVersion)

	if code != 0 || fixture.installerCalls != 0 {
		t.Fatalf("expected success without installing: code=%d installer=%d", code, fixture.installerCalls)
	}
	if held, version := readDispatcherVersionHold(); !held || version != dispatcherVersion {
		t.Fatalf("expected a hold at %s, got held=%t version=%s", dispatcherVersion, held, version)
	}
	if !strings.Contains(stdout, "uloop is held at "+dispatcherVersion+"; automatic updates are paused.") {
		t.Fatalf("missing hold notice: %s", stdout)
	}
}

func TestUpdateWithoutVersionRemovesTheHoldFirst(t *testing.T) {
	// Verifies plain `uloop update` removes the hold before installing, announces the resume, and stays silent when there was no hold.
	t.Run("held", func(t *testing.T) {
		fixture := newVersionHoldUpdateFixture(t)
		fixture.writeHold(`{"version":"3.1.0"}`)

		code, stdout, _ := runUpdateForHoldTest()

		if code != 0 || fixture.installerCalls != 1 || fixture.holdFileExists {
			t.Fatalf("expected the hold to be gone before installing: code=%d installer=%d held=%t", code, fixture.installerCalls, fixture.holdFileExists)
		}
		if !strings.Contains(stdout, "Automatic updates resumed.") {
			t.Fatalf("missing resume notice: %s", stdout)
		}
	})
	t.Run("held and already current", func(t *testing.T) {
		fixture := newVersionHoldUpdateFixture(t)
		fixture.targetVersion = dispatcherVersion
		fixture.writeHold(`{"version":"3.1.0"}`)

		code, stdout, _ := runUpdateForHoldTest()

		if code != 0 || fixture.installerCalls != 0 {
			t.Fatalf("expected success without installing: code=%d installer=%d", code, fixture.installerCalls)
		}
		resumedAt := strings.Index(stdout, "Automatic updates resumed.")
		currentAt := strings.Index(stdout, "already up to date")
		if resumedAt < 0 || currentAt < resumedAt {
			t.Fatalf("expected the resume notice before the up-to-date line: %s", stdout)
		}
		fixture.assertNoHold()
	})
	t.Run("held while offline", func(t *testing.T) {
		fixture := newVersionHoldUpdateFixture(t)
		fixture.resolveErr = errors.New("network unavailable")
		fixture.writeHold(`{"version":"3.1.0"}`)

		code, _, _ := runUpdateForHoldTest()

		if code != 1 {
			t.Fatalf("expected the lookup failure to surface, got code=%d", code)
		}
		fixture.assertNoHold()
	})
	t.Run("not held", func(t *testing.T) {
		newVersionHoldUpdateFixture(t)

		_, stdout, _ := runUpdateForHoldTest()

		if strings.Contains(stdout, "Automatic updates resumed.") {
			t.Fatalf("resume notice must not appear without a hold: %s", stdout)
		}
	})
}

func TestUpdateWithoutVersionFailsWhenTheHoldCannotBeRemoved(t *testing.T) {
	// Verifies an undeletable hold stops plain `uloop update` with exit 1 before the lookup and the installer.
	fixture := newVersionHoldUpdateFixture(t)
	writeDispatcherTestFile(t, filepath.Join(fixture.holdPath(), "occupied"), "")

	code, _, stderr := runUpdateForHoldTest()

	if code != 1 || fixture.installerCalls != 0 || fixture.resolverCalls != 0 {
		t.Fatalf("expected exit 1 before any update work: code=%d installer=%d resolver=%d", code, fixture.installerCalls, fixture.resolverCalls)
	}
	if !strings.Contains(stderr, "Could not remove the version hold") {
		t.Fatalf("missing hold error: %s", stderr)
	}
}

func TestUpdateToVersionUnderHomebrewRecordsNoHold(t *testing.T) {
	// Verifies a Homebrew-managed install still rejects --to-version and records no hold.
	fixture := newVersionHoldUpdateFixture(t)
	resolveUpdateExecutablePathFunc = func() (string, error) {
		return "/opt/homebrew/Cellar/uloop/3.0.0/bin/uloop", nil
	}

	code, _, stderr := runUpdateForHoldTest("--to-version", "999.0.0")

	if code != 1 || !strings.Contains(stderr, "brew upgrade uloop") {
		t.Fatalf("expected the Homebrew rejection: code=%d stderr=%s", code, stderr)
	}
	fixture.assertNoHold()
}

func TestPeriodicSelfUpdateStopsWhileAVersionIsHeld(t *testing.T) {
	// Verifies a hold file, even a corrupt one, stops the periodic update, and its absence lets it run.
	cases := []struct {
		name      string
		hold      string
		wantCalls int
	}{
		{name: "held", hold: `{"version":"3.1.0"}`, wantCalls: 0},
		{name: "corrupt hold", hold: "{", wantCalls: 0},
		{name: "no hold", wantCalls: 1},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			fixture := newPeriodicUpdateFixture(t)
			if testCase.hold != "" {
				writeDispatcherTestFile(t, filepath.Join(fixture.configRoot, dispatcherVersionHoldFileName), testCase.hold)
			}

			fixture.run()

			if fixture.periodicCalls != testCase.wantCalls {
				t.Fatalf("calls mismatch: got %d want %d", fixture.periodicCalls, testCase.wantCalls)
			}
		})
	}
}

func TestReadDispatcherVersionHoldTreatsUnreadableHoldsAsHeld(t *testing.T) {
	// Verifies a corrupt or invalid hold still counts as held and shows its version as unknown.
	cases := []struct {
		name        string
		content     string
		wantVersion string
	}{
		{name: "valid", content: `{"version":"v3.1.0"}`, wantVersion: "3.1.0"},
		{name: "corrupt json", content: "{", wantVersion: dispatcherVersionHoldUnknownVersion},
		{name: "invalid version", content: `{"version":"latest"}`, wantVersion: dispatcherVersionHoldUnknownVersion},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			configRoot := t.TempDir()
			t.Setenv(nativepath.ConfigDirEnvName, configRoot)
			writeDispatcherTestFile(t, filepath.Join(configRoot, dispatcherVersionHoldFileName), testCase.content)

			held, version := readDispatcherVersionHold()

			if !held || version != testCase.wantVersion {
				t.Fatalf("got held=%t version=%q want %q", held, version, testCase.wantVersion)
			}
		})
	}
}

func TestRequiredUpdateAsksForManualUpdateWhileAVersionIsHeld(t *testing.T) {
	// Verifies a below-floor command under a hold asks the user to run `uloop update` instead of installing.
	fixture := newPeriodicUpdateFixture(t)
	writeDispatcherTestFile(t, filepath.Join(fixture.configRoot, dispatcherVersionHoldFileName), `{"version":"3.1.0"}`)
	var stderr bytes.Buffer

	handled, code := enforceDispatcherFreshnessWithDeps(context.Background(), dispatcherPin{MinimumDispatcherVersion: "999.0.0"}, "", &stderr, fixture.deps)

	if !handled || code != 1 || fixture.requiredCalls != 0 {
		t.Fatalf("expected a manual-update error without installing: handled=%t code=%d required=%d", handled, code, fixture.requiredCalls)
	}
	for _, expected := range []string{
		clierrors.ErrorCodeCLIUpdateRequired,
		"This uloop is held at 3.1.0 by `uloop update --to-version`.",
		"Run `uloop update` to resume automatic updates, then retry the command.",
	} {
		if !strings.Contains(stderr.String(), expected) {
			t.Fatalf("missing %q in: %s", expected, stderr.String())
		}
	}
}

func TestManualUpdateWaitsForAnotherProcessHoldingTheLock(t *testing.T) {
	// Verifies `uloop update --to-version` refuses with a retryable error, records no hold, and runs no installer while another process holds the update lock.
	fixture := newVersionHoldUpdateFixture(t)
	cacheRoot := t.TempDir()
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	holdUpdateLockElsewhere(t, cacheRoot)

	code, _, stderr := runUpdateForHoldTest("--to-version", "999.0.0")

	if code != 1 || fixture.installerCalls != 0 {
		t.Fatalf("expected exit 1 without installing: code=%d installer=%d", code, fixture.installerCalls)
	}
	fixture.assertNoHold()
	for _, expected := range []string{"Another uloop command is updating the dispatcher.", `"Retryable": true`, "Retry `uloop update` when it finishes."} {
		if !strings.Contains(stderr, expected) {
			t.Fatalf("missing %q in: %s", expected, stderr)
		}
	}
}

func TestManualUpdateHoldsTheLockWhileInstallingAndReleasesIt(t *testing.T) {
	// Verifies `uloop update` holds the update lock while the installer runs and releases it afterwards.
	fixture := newVersionHoldUpdateFixture(t)
	cacheRoot := t.TempDir()
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	var resultDuringInstall dispatcherUpdateLockResult
	updateRunCommand = func(context.Context, update.Command, io.Writer, io.Writer) error {
		fixture.installerCalls++
		release, result := tryAcquireDispatcherUpdateLock(cacheRoot)
		release()
		resultDuringInstall = result
		return nil
	}

	code, _, _ := runUpdateForHoldTest()

	if code != 0 || fixture.installerCalls != 1 || resultDuringInstall != dispatcherUpdateLockHeld {
		t.Fatalf("expected an install under the lock: code=%d installer=%d lock=%s", code, fixture.installerCalls, resultDuringInstall)
	}
	release, result := tryAcquireDispatcherUpdateLock(cacheRoot)
	defer release()
	if result != dispatcherUpdateLockAcquired {
		t.Fatalf("expected the lock to be released, got %s", result)
	}
}

func TestPlainUpdateKeepsTheHoldWhileAnotherProcessUpdates(t *testing.T) {
	// Verifies plain `uloop update` neither removes the hold nor installs while another process holds the update lock.
	fixture := newVersionHoldUpdateFixture(t)
	cacheRoot := t.TempDir()
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	fixture.writeHold(`{"version":"3.1.0"}`)
	holdUpdateLockElsewhere(t, cacheRoot)

	code, stdout, _ := runUpdateForHoldTest()

	if code != 1 || fixture.installerCalls != 0 || fixture.resolverCalls != 0 {
		t.Fatalf("expected exit 1 before any update work: code=%d installer=%d resolver=%d", code, fixture.installerCalls, fixture.resolverCalls)
	}
	if strings.Contains(stdout, "Automatic updates resumed.") {
		t.Fatalf("the hold must not be reported as removed: %s", stdout)
	}
	assertFileContent(t, fixture.holdPath(), `{"version":"3.1.0"}`)
}

func TestFailedHoldWriteLeavesNoPartialFiles(t *testing.T) {
	// Verifies a hold write that cannot replace the hold path leaves no temporary file behind.
	configRoot := t.TempDir()
	t.Setenv(nativepath.ConfigDirEnvName, configRoot)
	writeDispatcherTestFile(t, filepath.Join(configRoot, dispatcherVersionHoldFileName, "occupied"), "")

	if err := writeDispatcherVersionHold("999.0.0"); err == nil {
		t.Fatal("expected the write to fail when a directory occupies the hold path")
	}

	entries, err := os.ReadDir(configRoot)
	if err != nil {
		t.Fatalf("read config root: %v", err)
	}
	if len(entries) != 1 || entries[0].Name() != dispatcherVersionHoldFileName {
		t.Fatalf("expected only the occupied hold path, got %v", entries)
	}
}

func TestPeriodicSelfUpdateRechecksTheHoldAfterTakingTheLock(t *testing.T) {
	// Verifies a hold recorded by another process while this one takes the lock stops the periodic update.
	fixture := newPeriodicUpdateFixture(t)
	fixture.deps.acquireUpdateLock = func(cacheRoot string) (func(), dispatcherUpdateLockResult) {
		release, result := tryAcquireDispatcherUpdateLock(cacheRoot)
		writeDispatcherTestFile(t, filepath.Join(fixture.configRoot, dispatcherVersionHoldFileName), `{"version":"3.1.0"}`)
		return release, result
	}

	fixture.run()

	if fixture.periodicCalls != 0 {
		t.Fatalf("expected no update after a hold appeared, got %d", fixture.periodicCalls)
	}
}

func TestRequiredUpdateRechecksTheHoldAfterTakingTheLock(t *testing.T) {
	// Verifies a hold recorded by another process before the required update takes the lock stops the install and names the hold.
	fixture := newPeriodicUpdateFixture(t)
	writePinnedProjectWithMinimum(t, fixture, "999.0.0")
	fixture.writeLastChecked(fixture.now)
	fixture.deps.acquireUpdateLock = func(cacheRoot string) (func(), dispatcherUpdateLockResult) {
		release, result := tryAcquireDispatcherUpdateLock(cacheRoot)
		writeDispatcherTestFile(t, filepath.Join(fixture.configRoot, dispatcherVersionHoldFileName), `{"version":"3.1.0"}`)
		return release, result
	}
	var stderr bytes.Buffer

	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, io.Discard, &stderr, fixture.deps)

	if code != 1 || fixture.requiredCalls != 0 {
		t.Fatalf("expected no install after a hold appeared: code=%d required=%d", code, fixture.requiredCalls)
	}
	if !strings.Contains(stderr.String(), "held at 3.1.0") {
		t.Fatalf("expected the hold to be named: %s", stderr.String())
	}
}
