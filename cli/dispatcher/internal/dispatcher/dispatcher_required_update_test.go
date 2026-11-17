package dispatcher

import (
	"bytes"
	"context"
	"errors"
	"io"
	"path/filepath"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
)

// stubRequiredUpdateRelease makes the channel's newest release latestVersion and records the
// release tag the installer is asked to install.
func stubRequiredUpdateRelease(t *testing.T, latestVersion string) *[]string {
	t.Helper()
	previousResolver := resolveUpdateTargetVersionFunc
	previousRunner := updateRunCommand
	t.Cleanup(func() {
		resolveUpdateTargetVersionFunc = previousResolver
		updateRunCommand = previousRunner
	})
	resolveUpdateTargetVersionFunc = func(_ context.Context, options update.Options) (update.Options, error) {
		if options.TargetVersion == "" {
			options.TargetVersion = latestVersion
		}
		return options, nil
	}
	installedTags := []string{}
	updateRunCommand = func(_ context.Context, command update.Command, _ io.Writer, _ io.Writer) error {
		installedTags = append(installedTags, command.ReleaseTag)
		return nil
	}
	return &installedTags
}

func TestRequiredUpdateInstallsTheFloorWhenTheNewestReleaseIsOlder(t *testing.T) {
	// Verifies that when the channel's newest release is below the floor, the floor itself is installed and named in the retry request.
	installedTags := stubRequiredUpdateRelease(t, "1.0.0")
	deps := runFromInstallDir(defaultDispatcherRunDeps())
	deps.runRequiredUpdate = func(ctx context.Context, minimumVersion string) (string, error) {
		return runRequiredDispatcherUpdateForOS(ctx, minimumVersion, "linux")
	}
	var stderr bytes.Buffer

	handled, code := runDispatcherFreshnessUpdate(
		context.Background(),
		dispatcherFreshnessPlan{Action: dispatcherFreshnessRunRequiredUpdate, MinimumVersion: "999.0.0"},
		&stderr,
		deps)

	if len(*installedTags) != 1 || !strings.HasSuffix((*installedTags)[0], "999.0.0") {
		t.Fatalf("expected the floor to be installed, got %v", *installedTags)
	}
	if !handled || code != 1 || !strings.Contains(stderr.String(), "Dispatcher updated from "+dispatcherVersion+" to 999.0.0") {
		t.Fatalf("expected a retry request naming the floor: handled=%t code=%d stderr=%s", handled, code, stderr.String())
	}
}

func TestRequiredUpdateInstallsTheNewestReleaseWhenItMeetsTheFloor(t *testing.T) {
	// Verifies that a newest release at or above the floor is the one installed.
	installedTags := stubRequiredUpdateRelease(t, "1000.0.0")

	installed, err := runRequiredDispatcherUpdateForOS(context.Background(), "999.0.0", "linux")

	if err != nil || installed != "1000.0.0" {
		t.Fatalf("expected the newest release: installed=%q err=%v", installed, err)
	}
	if len(*installedTags) != 1 || !strings.HasSuffix((*installedTags)[0], "1000.0.0") {
		t.Fatalf("installer tag mismatch: %v", *installedTags)
	}
}

func TestRequiredUpdateBelowTheFloorAsksForManualUpdate(t *testing.T) {
	// Verifies an install that still leaves the dispatcher below the floor ends in the manual-update error, not a retry request.
	deps := runFromInstallDir(defaultDispatcherRunDeps())
	deps.runRequiredUpdate = func(context.Context, string) (string, error) {
		return "998.0.0", nil
	}
	var stderr bytes.Buffer

	handled, code := runDispatcherFreshnessUpdate(
		context.Background(),
		dispatcherFreshnessPlan{Action: dispatcherFreshnessRunRequiredUpdate, MinimumVersion: "999.0.0"},
		&stderr,
		deps)

	if !handled || code != 1 || strings.Contains(stderr.String(), "Retry the same uloop command.") {
		t.Fatalf("expected a manual-update error without a retry request: handled=%t code=%d stderr=%s", handled, code, stderr.String())
	}
	if !strings.Contains(stderr.String(), "The dispatcher could not be updated to 999.0.0 or later (installed: 998.0.0).") {
		t.Fatalf("missing reason: %s", stderr.String())
	}
}

func TestRequiredUpdateRunsWhenThePeriodicUpdateStayedBelowTheFloor(t *testing.T) {
	// Verifies that a periodic install below the floor is followed by one required update that installs the floor.
	fixture := newPeriodicUpdateFixture(t)
	writePinnedProjectWithMinimum(t, fixture, "999.0.0")
	fixture.periodicResult = "998.0.0"
	var stderr bytes.Buffer

	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, io.Discard, &stderr, fixture.deps)

	if code != 1 || fixture.periodicCalls != 1 || fixture.requiredCalls != 1 || fixture.runnerCalls != 0 {
		t.Fatalf("expected one periodic and one required update: code=%d periodic=%d required=%d runner=%d", code, fixture.periodicCalls, fixture.requiredCalls, fixture.runnerCalls)
	}
	if len(fixture.requiredMinimums) != 1 || fixture.requiredMinimums[0] != "999.0.0" {
		t.Fatalf("required update floor mismatch: %v", fixture.requiredMinimums)
	}
}

func TestRequiredUpdateInstallsNothingWhenAlreadyCurrent(t *testing.T) {
	// Verifies the default required update returns no version and runs no installer when the newest release is the running one.
	installedTags := stubRequiredUpdateRelease(t, dispatcherVersion)

	installed, err := runRequiredDispatcherUpdate(context.Background(), dispatcherVersion)

	if err != nil || installed != "" || len(*installedTags) != 0 {
		t.Fatalf("expected no install: installed=%q err=%v tags=%v", installed, err, *installedTags)
	}
}

func TestRequiredUpdateReportsLookupAndInstallerFailures(t *testing.T) {
	// Verifies a failed floor lookup, an invalid newest release, and a failed installer each return an error and no installed version.
	cases := []struct {
		name          string
		latestVersion string
		floorErr      error
		installerErr  error
	}{
		{name: "floor lookup failure", latestVersion: "1.0.0", floorErr: errors.New("floor release not found")},
		{name: "invalid newest release", latestVersion: "not-a-version"},
		{name: "installer failure", latestVersion: "1000.0.0", installerErr: errors.New("installer exited 1")},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			stubRequiredUpdateRelease(t, testCase.latestVersion)
			resolveUpdateTargetVersionFunc = func(_ context.Context, options update.Options) (update.Options, error) {
				if options.TargetVersion == "" {
					options.TargetVersion = testCase.latestVersion
					return options, nil
				}
				return options, testCase.floorErr
			}
			updateRunCommand = func(context.Context, update.Command, io.Writer, io.Writer) error {
				return testCase.installerErr
			}

			installed, err := runRequiredDispatcherUpdateForOS(context.Background(), "999.0.0", "linux")

			if err == nil || installed != "" {
				t.Fatalf("expected an error and no install: installed=%q err=%v", installed, err)
			}
		})
	}
}

func TestRequiredUpdateAsksForAManualUpdateWhenRunningOutsideTheInstallDir(t *testing.T) {
	// Verifies a below-floor dispatcher running outside the install dir installs nothing and asks for a manual update instead of a retry that could never succeed.
	fixture := newPeriodicUpdateFixture(t)
	writePinnedProjectWithMinimum(t, fixture, "999.0.0")
	fixture.writeLastChecked(fixture.now)
	otherDir := t.TempDir()
	fixture.deps.resolveSelfUpdateLocation = func() (string, string, error) {
		return filepath.Join(otherDir, "uloop"), fixture.installDir, nil
	}
	var stderr bytes.Buffer

	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, io.Discard, &stderr, fixture.deps)

	if code != 1 || fixture.requiredCalls != 0 {
		t.Fatalf("expected no install: code=%d required=%d", code, fixture.requiredCalls)
	}
	if !strings.Contains(stderr.String(), "This uloop runs from") || strings.Contains(stderr.String(), "Retry the same uloop command.") {
		t.Fatalf("expected the manual-update error without a retry request: %s", stderr.String())
	}
}

func TestRequiredUpdateStillRunsWhenTheLocationIsUnknown(t *testing.T) {
	// Verifies a failure to resolve where the dispatcher runs does not block the required update.
	fixture := newPeriodicUpdateFixture(t)
	writePinnedProjectWithMinimum(t, fixture, "999.0.0")
	fixture.writeLastChecked(fixture.now)
	fixture.deps.resolveSelfUpdateLocation = func() (string, string, error) {
		return "", "", errors.New("no executable path")
	}

	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, io.Discard, io.Discard, fixture.deps)

	if code != 1 || fixture.requiredCalls != 1 {
		t.Fatalf("expected the required update to run: code=%d required=%d", code, fixture.requiredCalls)
	}
}
