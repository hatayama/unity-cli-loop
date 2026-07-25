package dispatcher

import (
	"archive/tar"
	"archive/zip"
	"bytes"
	"compress/gzip"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
)

type dispatcherArchiveTestEntry struct {
	Name    string
	Content string
}

type dispatcherRoundTripFunc func(*http.Request) (*http.Response, error)

func (roundTrip dispatcherRoundTripFunc) RoundTrip(request *http.Request) (*http.Response, error) {
	return roundTrip(request)
}

func TestRunDispatcherUsesProjectPinAndCachedRealCLI(t *testing.T) {
	// Verifies dispatcher reads the project pin and executes the cached real CLI.
	projectRoot := createDispatcherUnityProject(t)
	cacheRoot := t.TempDir()
	writeDispatcherProjectPin(t, projectRoot, clicontract.ProjectRunnerVersion())
	expectedCLIPath := writeCachedDispatcherRealCLI(t, cacheRoot, clicontract.ProjectRunnerVersion())
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")
	t.Chdir(projectRoot)

	deps := defaultDispatcherRunDeps()
	var actualPath string
	var actualArgs []string
	deps.runRealCLI = func(ctx context.Context, realCLIPath string, args []string, stdout io.Writer, stderr io.Writer) int {
		actualPath = realCLIPath
		actualArgs = append([]string{}, args...)
		return 7
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"compile", "--force-recompile"}, &stdout, &stderr, deps)

	if code != 7 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	if actualPath != expectedCLIPath {
		t.Fatalf("real CLI path mismatch: %s", actualPath)
	}
	if stderr.String() != "" {
		t.Fatalf("cached CLI should not write dispatcher download status: %s", stderr.String())
	}
	assertStringSliceEqual(t, actualArgs, []string{"compile", "--force-recompile"})
}

func TestRunDispatcherPreservesExplicitProjectPathForRealCLI(t *testing.T) {
	// Verifies dispatcher accepts trailing --project-path and passes the original arguments onward.
	projectRoot := createDispatcherUnityProject(t)
	cacheRoot := t.TempDir()
	writeDispatcherProjectPin(t, projectRoot, clicontract.ProjectRunnerVersion())
	writeCachedDispatcherRealCLI(t, cacheRoot, clicontract.ProjectRunnerVersion())
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")
	t.Chdir(t.TempDir())

	deps := defaultDispatcherRunDeps()
	var actualArgs []string
	deps.runRealCLI = func(ctx context.Context, realCLIPath string, args []string, stdout io.Writer, stderr io.Writer) int {
		actualArgs = append([]string{}, args...)
		return 0
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"compile", "--project-path", projectRoot}, &stdout, &stderr, deps)

	if code != 0 {
		t.Fatalf("dispatcher failed: code=%d stderr=%s", code, stderr.String())
	}
	assertStringSliceEqual(t, actualArgs, []string{"compile", "--project-path", projectRoot})
}

func TestRunDispatcherForwardsProjectScopedVersionToPinnedRunner(t *testing.T) {
	// Verifies --project-path --version is forwarded so the pinned runner reports its own version.
	projectRoot := createDispatcherUnityProject(t)
	cacheRoot := t.TempDir()
	writeDispatcherProjectPin(t, projectRoot, clicontract.ProjectRunnerVersion())
	writeCachedDispatcherRealCLI(t, cacheRoot, clicontract.ProjectRunnerVersion())
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")
	t.Chdir(t.TempDir())

	deps := defaultDispatcherRunDeps()
	var actualArgs []string
	deps.runRealCLI = func(ctx context.Context, realCLIPath string, args []string, stdout io.Writer, stderr io.Writer) int {
		actualArgs = append([]string{}, args...)
		return 0
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"--project-path", projectRoot, "--version"}, &stdout, &stderr, deps)

	if code != 0 {
		t.Fatalf("dispatcher failed: code=%d stderr=%s", code, stderr.String())
	}
	assertStringSliceEqual(t, actualArgs, []string{"--project-path", projectRoot, "--version"})
	if stdout.String() != "" {
		t.Fatalf("dispatcher must not answer project-scoped version locally: %s", stdout.String())
	}
}

func TestRunDispatcherForwardsProjectScopedVersionJSONToPinnedRunner(t *testing.T) {
	// Verifies --project-path --version --json is forwarded so the pinned runner reports its own version payload.
	projectRoot := createDispatcherUnityProject(t)
	cacheRoot := t.TempDir()
	writeDispatcherProjectPin(t, projectRoot, clicontract.ProjectRunnerVersion())
	writeCachedDispatcherRealCLI(t, cacheRoot, clicontract.ProjectRunnerVersion())
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")
	t.Chdir(t.TempDir())

	deps := defaultDispatcherRunDeps()
	var actualArgs []string
	deps.runRealCLI = func(ctx context.Context, realCLIPath string, args []string, stdout io.Writer, stderr io.Writer) int {
		actualArgs = append([]string{}, args...)
		return 0
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"--project-path", projectRoot, "--version", "--json"}, &stdout, &stderr, deps)

	if code != 0 {
		t.Fatalf("dispatcher failed: code=%d stderr=%s", code, stderr.String())
	}
	assertStringSliceEqual(t, actualArgs, []string{"--project-path", projectRoot, "--version", "--json"})
	if stdout.String() != "" {
		t.Fatalf("dispatcher must not answer project-scoped version locally: %s", stdout.String())
	}
}

func TestRunDispatcherForwardsRunnerOwnedCommandHelpToPinnedRunner(t *testing.T) {
	// Verifies await-pause-point --help is forwarded to the pinned runner instead of being answered locally.
	projectRoot := createDispatcherUnityProject(t)
	cacheRoot := t.TempDir()
	writeDispatcherProjectPin(t, projectRoot, clicontract.ProjectRunnerVersion())
	writeCachedDispatcherRealCLI(t, cacheRoot, clicontract.ProjectRunnerVersion())
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")
	t.Chdir(projectRoot)

	deps := defaultDispatcherRunDeps()
	var actualArgs []string
	deps.runRealCLI = func(ctx context.Context, realCLIPath string, args []string, stdout io.Writer, stderr io.Writer) int {
		actualArgs = append([]string{}, args...)
		return 0
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{clicore.PausePointAwaitCommandName, "--help"}, &stdout, &stderr, deps)

	if code != 0 {
		t.Fatalf("dispatcher failed: code=%d stderr=%s", code, stderr.String())
	}
	assertStringSliceEqual(t, actualArgs, []string{clicore.PausePointAwaitCommandName, "--help"})
	if stdout.String() != "" {
		t.Fatalf("dispatcher must not answer runner-owned command help locally: %s", stdout.String())
	}
}

func TestRunDispatcherCommandHelpDoesNotRequireProjectPin(t *testing.T) {
	// Verifies dispatcher handles command help before project and pin resolution.
	t.Chdir(t.TempDir())

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"compile", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("dispatcher command help failed: code=%d stderr=%s", code, stderr.String())
	}
	if !bytes.Contains(stdout.Bytes(), []byte("uloop compile")) {
		t.Fatalf("compile help output mismatch: %s", stdout.String())
	}
}

func TestRunDispatcherUnknownLeadingOptionDoesNotRequireProjectPin(t *testing.T) {
	// Verifies dispatcher reports leading option mistakes before project and pin resolution.
	t.Chdir(t.TempDir())

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"--project-pathology"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("dispatcher unknown option code mismatch: code=%d stdout=%s", code, stdout.String())
	}
	if !bytes.Contains(stderr.Bytes(), []byte("Unknown global option")) {
		t.Fatalf("dispatcher unknown option output mismatch: %s", stderr.String())
	}
}

func TestRunDispatcherLaunchQuitDoesNotRequireProjectPin(t *testing.T) {
	// Verifies launch can bootstrap a project before Unity has generated the dispatcher pin.
	projectRoot := createDispatcherUnityProject(t)
	t.Chdir(t.TempDir())

	deps := defaultDispatcherRunDeps()
	deps.launch.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return nil, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"launch", projectRoot, "--quit"}, &stdout, &stderr, deps)

	if code != 0 {
		t.Fatalf("dispatcher launch failed: code=%d stderr=%s", code, stderr.String())
	}
	if !bytes.Contains(stdout.Bytes(), []byte(`"Quit": true`)) {
		t.Fatalf("dispatcher launch output mismatch: %s", stdout.String())
	}
}

func TestRunDispatcherLaunchOptionsDoNotRequireProjectPin(t *testing.T) {
	// Verifies dispatcher-owned launch flags are parsed before project pin resolution.
	projectRoot := createDispatcherUnityProject(t)
	t.Chdir(t.TempDir())

	deps := defaultDispatcherRunDeps()
	deps.launch.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return nil, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runDispatcherWithDeps(
		context.Background(),
		[]string{"launch", "--editor-version", "6000.0.0f1", projectRoot, "--quit"},
		&stdout,
		&stderr,
		deps)

	if code != 0 {
		t.Fatalf("dispatcher launch failed: code=%d stderr=%s", code, stderr.String())
	}
	if !bytes.Contains(stdout.Bytes(), []byte(`"Quit": true`)) {
		t.Fatalf("dispatcher launch output mismatch: %s", stdout.String())
	}
}

func TestRunDispatcherVersionUsesDispatcherVersion(t *testing.T) {
	// Verifies `uloop --version` reports the dispatcher release version instead of the project runner version.
	t.Chdir(t.TempDir())

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"--version"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("dispatcher version failed: code=%d stderr=%s", code, stderr.String())
	}
	if strings.TrimSpace(stdout.String()) != dispatcherVersion {
		t.Fatalf("dispatcher version mismatch: %s", stdout.String())
	}
}

func TestRunDispatcherVersionSubcommandMatchesFlagVersion(t *testing.T) {
	// Verifies `uloop version` returns the same text as `uloop --version`.
	t.Chdir(t.TempDir())

	var flagStdout bytes.Buffer
	var subcommandStdout bytes.Buffer
	flagCode := RunDispatcher(context.Background(), []string{"--version"}, &flagStdout, io.Discard)
	subcommandCode := RunDispatcher(context.Background(), []string{clicore.VersionCommandName}, &subcommandStdout, io.Discard)

	if flagCode != 0 || subcommandCode != 0 {
		t.Fatalf("version exit codes mismatch: flag=%d subcommand=%d", flagCode, subcommandCode)
	}
	if flagStdout.String() != subcommandStdout.String() {
		t.Fatalf("version output mismatch:\nflag:       %q\nsubcommand: %q", flagStdout.String(), subcommandStdout.String())
	}
}

func TestRunDispatcherVersionSubcommandJSONMatchesFlagVersionJSON(t *testing.T) {
	// Verifies `uloop version --json` returns the same JSON as `uloop --version --json`.
	t.Chdir(t.TempDir())

	var flagStdout bytes.Buffer
	var subcommandStdout bytes.Buffer
	flagCode := RunDispatcher(context.Background(), []string{"--version", "--json"}, &flagStdout, io.Discard)
	subcommandCode := RunDispatcher(context.Background(), []string{clicore.VersionCommandName, "--json"}, &subcommandStdout, io.Discard)

	if flagCode != 0 || subcommandCode != 0 {
		t.Fatalf("version --json exit codes mismatch: flag=%d subcommand=%d", flagCode, subcommandCode)
	}
	if flagStdout.String() != subcommandStdout.String() {
		t.Fatalf("version --json output mismatch:\nflag:       %q\nsubcommand: %q", flagStdout.String(), subcommandStdout.String())
	}
}

func TestRunDispatcherVersionSubcommandReportsTrailingUnknownOption(t *testing.T) {
	// Verifies `uloop version --json extra` reports the trailing argument, not --json itself.
	t.Chdir(t.TempDir())

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{clicore.VersionCommandName, "--json", "extra"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected failure, got code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if !strings.Contains(stderr.String(), "Unknown version option: extra") {
		t.Fatalf("stderr should report trailing option extra: %s", stderr.String())
	}
	if !strings.Contains(stderr.String(), "uloop version --help") {
		t.Fatalf("stderr should guide users to version --help: %s", stderr.String())
	}
}

func TestResolveDispatcherRealCLIRejectsInvalidProjectRunnerVersion(t *testing.T) {
	// Verifies project pins cannot escape the dispatcher cache through projectRunnerVersion path segments.
	t.Setenv(nativepath.CacheDirEnvName, t.TempDir())

	_, err := resolveDispatcherRealCLI(context.Background(), dispatcherPin{ProjectRunnerVersion: "../../../../payload"}, io.Discard)

	if err == nil {
		t.Fatal("expected invalid projectRunnerVersion error")
	}
}

func TestResolveDispatcherRealCLIUsesProjectRunnerPathOverrideWithoutDownloading(t *testing.T) {
	// Verifies the dev escape-hatch env var returns the local binary and skips pin/cache/download entirely.
	overrideDir := t.TempDir()
	overridePath := filepath.Join(overrideDir, "uloop-project-runner")
	if err := os.WriteFile(overridePath, []byte("#!/bin/sh\n"), 0o755); err != nil {
		t.Fatalf("failed to write override binary: %v", err)
	}
	t.Setenv(nativepath.ProjectRunnerPathEnvName, overridePath)
	t.Setenv(nativepath.CacheDirEnvName, t.TempDir())

	resolvedPath, err := resolveDispatcherRealCLI(
		context.Background(),
		dispatcherPin{ProjectRunnerVersion: "not-a-real-published-version"},
		io.Discard)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if resolvedPath != overridePath {
		t.Fatalf("resolvedPath = %q, want %q", resolvedPath, overridePath)
	}
}

func TestResolveDispatcherRealCLIRejectsMissingProjectRunnerPathOverride(t *testing.T) {
	// Verifies a clear error is returned when the override env var points at a missing file.
	t.Setenv(nativepath.ProjectRunnerPathEnvName, filepath.Join(t.TempDir(), "missing-uloop-project-runner"))
	t.Setenv(nativepath.CacheDirEnvName, t.TempDir())

	_, err := resolveDispatcherRealCLI(context.Background(), dispatcherPin{ProjectRunnerVersion: "1.0.0"}, io.Discard)

	if err == nil {
		t.Fatal("expected an error for a missing override path")
	}
	if !strings.Contains(err.Error(), nativepath.ProjectRunnerPathEnvName) {
		t.Fatalf("error message should mention %s: %v", nativepath.ProjectRunnerPathEnvName, err)
	}
}

func TestResolveDispatcherRealCLIRejectsDirectoryProjectRunnerPathOverride(t *testing.T) {
	// Verifies a clear error is returned when the override env var points at a directory instead of a binary.
	t.Setenv(nativepath.ProjectRunnerPathEnvName, t.TempDir())
	t.Setenv(nativepath.CacheDirEnvName, t.TempDir())

	_, err := resolveDispatcherRealCLI(context.Background(), dispatcherPin{ProjectRunnerVersion: "1.0.0"}, io.Discard)

	if err == nil {
		t.Fatal("expected an error for a directory override path")
	}
	if !strings.Contains(err.Error(), nativepath.ProjectRunnerPathEnvName) {
		t.Fatalf("error message should mention %s: %v", nativepath.ProjectRunnerPathEnvName, err)
	}
}

func TestEnforceDispatcherFreshnessRequiresManualUpdateWhenSelfUpdateDisabled(t *testing.T) {
	// Verifies disabling mutation does not disable the minimum dispatcher version contract.
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")

	var stderr bytes.Buffer
	handled, code := enforceDispatcherFreshness(
		context.Background(),
		dispatcherPin{MinimumDispatcherVersion: "999.0.0"},
		&stderr)

	if !handled || code != 1 {
		t.Fatalf("freshness result mismatch: handled=%t code=%d", handled, code)
	}
	if !bytes.Contains(stderr.Bytes(), []byte(clierrors.ErrorCodeCLIUpdateRequired)) {
		t.Fatalf("freshness output mismatch: %s", stderr.String())
	}
}

func TestEnforceDispatcherFreshnessMarksFailedOptionalUpdateChecked(t *testing.T) {
	// Verifies transient optional update failures are throttled until the next check interval.
	cacheRoot := t.TempDir()
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)

	deps := defaultDispatcherRunDeps()
	runnerCalls := 0
	deps.runUpdate = func(context.Context) error {
		runnerCalls++
		return errors.New("network unavailable")
	}

	var stderr bytes.Buffer
	handled, code := enforceDispatcherFreshnessWithDeps(
		context.Background(),
		dispatcherPin{MinimumDispatcherVersion: dispatcherVersion},
		&stderr,
		deps)

	if handled || code != 0 {
		t.Fatalf("freshness result mismatch: handled=%t code=%d", handled, code)
	}
	if !bytes.Contains(stderr.Bytes(), []byte("dispatcher self-update skipped")) {
		t.Fatalf("freshness output mismatch: %s", stderr.String())
	}
	statePath := filepath.Join(cacheRoot, dispatcherUpdateStateFileName)
	if _, err := os.Stat(statePath); err != nil {
		t.Fatalf("expected update state after failed optional update: %v", err)
	}

	stderr.Reset()
	handled, code = enforceDispatcherFreshnessWithDeps(
		context.Background(),
		dispatcherPin{MinimumDispatcherVersion: dispatcherVersion},
		&stderr,
		deps)

	if handled || code != 0 {
		t.Fatalf("second freshness result mismatch: handled=%t code=%d", handled, code)
	}
	if runnerCalls != 1 {
		t.Fatalf("optional update should be throttled after failure, got %d calls", runnerCalls)
	}
	if stderr.Len() != 0 {
		t.Fatalf("expected no throttled optional update output, got: %s", stderr.String())
	}
}

func TestEnforceDispatcherFreshnessReportsOptionalUpdateVersionChange(t *testing.T) {
	// Verifies optional dispatcher self-updates tell users which dispatcher version will run next.
	t.Setenv(nativepath.CacheDirEnvName, t.TempDir())
	deps, restoreDispatcherUpdateHooks := stubDispatcherUpdateHooks(t, "9.9.9")
	defer restoreDispatcherUpdateHooks()

	var stderr bytes.Buffer
	handled, code := enforceDispatcherFreshnessWithDeps(
		context.Background(),
		dispatcherPin{MinimumDispatcherVersion: dispatcherVersion},
		&stderr,
		deps)

	if handled || code != 0 {
		t.Fatalf("freshness result mismatch: handled=%t code=%d", handled, code)
	}
	expected := "uloop: dispatcher updated from " + dispatcherVersion + " to 9.9.9"
	if !bytes.Contains(stderr.Bytes(), []byte(expected)) {
		t.Fatalf("freshness output mismatch: %s", stderr.String())
	}
}

func TestEnforceDispatcherFreshnessSkipsOptionalUpdateMessageWhenVersionDidNotChange(t *testing.T) {
	// Verifies no-op optional dispatcher self-updates do not add noise before the real command output.
	t.Setenv(nativepath.CacheDirEnvName, t.TempDir())
	deps, restoreDispatcherUpdateHooks := stubDispatcherUpdateHooks(t, dispatcherVersion)
	defer restoreDispatcherUpdateHooks()

	var stderr bytes.Buffer
	handled, code := enforceDispatcherFreshnessWithDeps(
		context.Background(),
		dispatcherPin{MinimumDispatcherVersion: dispatcherVersion},
		&stderr,
		deps)

	if handled || code != 0 {
		t.Fatalf("freshness result mismatch: handled=%t code=%d", handled, code)
	}
	if stderr.Len() != 0 {
		t.Fatalf("expected no optional update output, got: %s", stderr.String())
	}
}

func TestEnforceDispatcherFreshnessReportsRequiredUpdateVersionChange(t *testing.T) {
	// Verifies required dispatcher self-updates include the version change before asking for a retry.
	t.Setenv(nativepath.CacheDirEnvName, t.TempDir())
	deps, restoreDispatcherUpdateHooks := stubDispatcherUpdateHooks(t, "999.0.0")
	defer restoreDispatcherUpdateHooks()

	var stderr bytes.Buffer
	handled, code := enforceDispatcherFreshnessWithDeps(
		context.Background(),
		dispatcherPin{MinimumDispatcherVersion: "999.0.0"},
		&stderr,
		deps)

	if !handled || code != 1 {
		t.Fatalf("freshness result mismatch: handled=%t code=%d", handled, code)
	}
	expected := "Dispatcher updated from " + dispatcherVersion + " to 999.0.0"
	if !bytes.Contains(stderr.Bytes(), []byte(expected)) {
		t.Fatalf("freshness output mismatch: %s", stderr.String())
	}
	if !bytes.Contains(stderr.Bytes(), []byte("Retry the command")) {
		t.Fatalf("retry guidance missing: %s", stderr.String())
	}
}

func TestDecideDispatcherFreshnessPlansUpdatePaths(t *testing.T) {
	// Verifies dispatcher freshness decisions are pure and separate from update execution.
	tests := []struct {
		name               string
		minimumVersion     string
		currentVersion     string
		selfUpdateDisabled bool
		hasSiblingRealCLI  bool
		updateDue          bool
		expectedAction     dispatcherFreshnessAction
	}{
		{
			name:           "no minimum",
			currentVersion: dispatcherVersion,
			expectedAction: dispatcherFreshnessNoop,
		},
		{
			name:               "required update disabled",
			minimumVersion:     "999.0.0",
			currentVersion:     dispatcherVersion,
			selfUpdateDisabled: true,
			updateDue:          true,
			expectedAction:     dispatcherFreshnessManualUpdateRequired,
		},
		{
			name:           "required update runs immediately",
			minimumVersion: "999.0.0",
			currentVersion: dispatcherVersion,
			expectedAction: dispatcherFreshnessRunRequiredUpdate,
		},
		{
			name:              "optional sibling skip",
			minimumVersion:    dispatcherVersion,
			currentVersion:    dispatcherVersion,
			hasSiblingRealCLI: true,
			updateDue:         true,
			expectedAction:    dispatcherFreshnessNoop,
		},
		{
			name:           "optional due",
			minimumVersion: dispatcherVersion,
			currentVersion: dispatcherVersion,
			updateDue:      true,
			expectedAction: dispatcherFreshnessRunOptionalUpdate,
		},
		{
			name:           "optional not due",
			minimumVersion: dispatcherVersion,
			currentVersion: dispatcherVersion,
			expectedAction: dispatcherFreshnessNoop,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			plan := decideDispatcherFreshness(dispatcherFreshnessInputs{
				MinimumVersion:     tt.minimumVersion,
				CurrentVersion:     tt.currentVersion,
				SelfUpdateDisabled: tt.selfUpdateDisabled,
				HasSiblingRealCLI:  tt.hasSiblingRealCLI,
				UpdateDue:          tt.updateDue,
			})

			if plan.Action != tt.expectedAction {
				t.Fatalf("action mismatch: %s", plan.Action)
			}
			if plan.MinimumVersion != strings.TrimSpace(tt.minimumVersion) {
				t.Fatalf("minimum version mismatch: %s", plan.MinimumVersion)
			}
		})
	}
}

func stubDispatcherUpdateHooks(t *testing.T, updatedVersion string) (dispatcherRunDeps, func()) {
	t.Helper()
	previousReader := dispatcherReadInstalledVersion
	deps := defaultDispatcherRunDeps()
	deps.runUpdate = func(context.Context) error {
		return nil
	}
	dispatcherReadInstalledVersion = func(context.Context) (string, error) {
		return updatedVersion, nil
	}
	return deps, func() {
		dispatcherReadInstalledVersion = previousReader
	}
}

func TestExtractDispatcherRealCLIFromTarRequiresProjectRunnerAsset(t *testing.T) {
	// Verifies project runner release archives extract the project runner binary.
	tempDir := t.TempDir()
	archivePath := filepath.Join(tempDir, "uloop-project-runner-darwin-arm64.tar.gz")
	writeDispatcherTarGzArchive(t, archivePath, []dispatcherArchiveTestEntry{
		{Name: "uloop-project-runner", Content: "real"},
	})
	destinationPath := filepath.Join(tempDir, "uloop-project-runner")

	err := extractDispatcherRealCLI(archivePath, filepath.Base(archivePath), destinationPath, "darwin")
	if err != nil {
		t.Fatalf("extractDispatcherRealCLI failed: %v", err)
	}
	assertFileContent(t, destinationPath, "real")
}

func TestExtractDispatcherRealCLIFromZipRequiresProjectRunnerAsset(t *testing.T) {
	// Verifies Windows project runner release archives extract the project runner binary.
	tempDir := t.TempDir()
	archivePath := filepath.Join(tempDir, "uloop-project-runner-windows-amd64.zip")
	writeDispatcherZipArchive(t, archivePath, []dispatcherArchiveTestEntry{
		{Name: "uloop-project-runner.exe", Content: "real"},
	})
	destinationPath := filepath.Join(tempDir, "uloop-project-runner.exe")

	err := extractDispatcherRealCLI(archivePath, filepath.Base(archivePath), destinationPath, "windows")
	if err != nil {
		t.Fatalf("extractDispatcherRealCLI failed: %v", err)
	}
	assertFileContent(t, destinationPath, "real")
}

func TestDispatcherHTTPClientHasDownloadTimeout(t *testing.T) {
	// Verifies dispatcher release downloads cannot hang indefinitely.
	if dispatcherHTTPClient.Timeout != 2*time.Minute {
		t.Fatalf("dispatcher HTTP timeout mismatch: %s", dispatcherHTTPClient.Timeout)
	}
}

func TestDownloadDispatcherRealCLIWritesDownloadStatus(t *testing.T) {
	// Verifies cache misses tell callers that dispatcher is downloading the pinned project runner.
	tempDir := t.TempDir()
	archivePath := filepath.Join(tempDir, "uloop-project-runner-darwin-arm64.tar.gz")
	writeDispatcherTarGzArchive(t, archivePath, []dispatcherArchiveTestEntry{
		{Name: "uloop-project-runner", Content: "real"},
	})
	archiveContent, err := os.ReadFile(archivePath)
	if err != nil {
		t.Fatalf("failed to read archive: %v", err)
	}
	checksum := sha256.Sum256(archiveContent)
	checksumContent := []byte(hex.EncodeToString(checksum[:]) + "  " + filepath.Base(archivePath) + "\n")

	previousHTTPClient := dispatcherHTTPClient
	defer func() {
		dispatcherHTTPClient = previousHTTPClient
	}()
	dispatcherHTTPClient = &http.Client{
		Transport: dispatcherRoundTripFunc(func(request *http.Request) (*http.Response, error) {
			content := []byte{}
			statusCode := http.StatusNotFound
			if strings.HasSuffix(request.URL.Path, "/uloop-project-runner-darwin-arm64.tar.gz") {
				content = archiveContent
				statusCode = http.StatusOK
			}
			if strings.HasSuffix(request.URL.Path, "/uloop-project-runner-darwin-arm64.tar.gz.sha256") {
				content = checksumContent
				statusCode = http.StatusOK
			}
			return &http.Response{
				StatusCode: statusCode,
				Status:     http.StatusText(statusCode),
				Body:       io.NopCloser(bytes.NewReader(content)),
			}, nil
		}),
	}
	restoreAttestation := stubAttestationVerifyPasses()
	defer restoreAttestation()

	var stderr bytes.Buffer
	realCLIPath, err := downloadDispatcherRealCLIForPin(
		context.Background(),
		t.TempDir(),
		dispatcherPin{ProjectRunnerVersion: "3.0.0-beta.88"},
		"darwin",
		"arm64",
		&stderr)
	if err != nil {
		t.Fatalf("downloadDispatcherRealCLIForPin failed: %v", err)
	}
	expectedStatus := "uloop: downloading pinned project runner 3.0.0-beta.88 for darwin-arm64...\n"
	if stderr.String() != expectedStatus {
		t.Fatalf("download status mismatch: %q", stderr.String())
	}
	assertFileContent(t, realCLIPath, "real")
	assertFileContent(t, dispatcherRealCLIReadyPath(realCLIPath), "ready\n")
}

func TestDownloadDispatcherRealCLIFailsClosedOnAttestationError(t *testing.T) {
	// Verifies pinned project runners are rejected when the attestation verifier reports a mismatch, so a compromised release cannot install a runner even if its .sha256 lines up.
	tempDir := t.TempDir()
	archivePath := filepath.Join(tempDir, "uloop-project-runner-darwin-arm64.tar.gz")
	writeDispatcherTarGzArchive(t, archivePath, []dispatcherArchiveTestEntry{
		{Name: "uloop-project-runner", Content: "real"},
	})
	archiveContent, err := os.ReadFile(archivePath)
	if err != nil {
		t.Fatalf("failed to read archive: %v", err)
	}
	checksum := sha256.Sum256(archiveContent)
	checksumContent := []byte(hex.EncodeToString(checksum[:]) + "  " + filepath.Base(archivePath) + "\n")

	previousHTTPClient := dispatcherHTTPClient
	defer func() {
		dispatcherHTTPClient = previousHTTPClient
	}()
	dispatcherHTTPClient = &http.Client{
		Transport: dispatcherRoundTripFunc(func(request *http.Request) (*http.Response, error) {
			content := []byte{}
			statusCode := http.StatusNotFound
			if strings.HasSuffix(request.URL.Path, "/uloop-project-runner-darwin-arm64.tar.gz") {
				content = archiveContent
				statusCode = http.StatusOK
			}
			if strings.HasSuffix(request.URL.Path, "/uloop-project-runner-darwin-arm64.tar.gz.sha256") {
				content = checksumContent
				statusCode = http.StatusOK
			}
			return &http.Response{
				StatusCode: statusCode,
				Status:     http.StatusText(statusCode),
				Body:       io.NopCloser(bytes.NewReader(content)),
			}, nil
		}),
	}
	restoreAttestation := stubAttestationVerifyReturns(fmt.Errorf("simulated runner attestation failure"))
	defer restoreAttestation()

	var stderr bytes.Buffer
	_, err = downloadDispatcherRealCLIForPin(
		context.Background(),
		t.TempDir(),
		dispatcherPin{ProjectRunnerVersion: "3.0.0-beta.88"},
		"darwin",
		"arm64",
		&stderr)
	if err == nil || !strings.Contains(err.Error(), "simulated runner attestation failure") {
		t.Fatalf("expected attestation failure to fail closed, got %v", err)
	}
}

func TestDownloadDispatcherRealCLIPassesRunnerIdentityToAttestation(t *testing.T) {
	// Verifies the runner-publish workflow SAN and the project runner release tag are what get sent to the attestation hook (Fable 5 review — SAN must match .github/workflows/native-cli-publish.yml, tag must be uloop-project-runner-v<version>).
	tempDir := t.TempDir()
	archivePath := filepath.Join(tempDir, "uloop-project-runner-darwin-arm64.tar.gz")
	writeDispatcherTarGzArchive(t, archivePath, []dispatcherArchiveTestEntry{
		{Name: "uloop-project-runner", Content: "real"},
	})
	archiveContent, err := os.ReadFile(archivePath)
	if err != nil {
		t.Fatalf("failed to read archive: %v", err)
	}
	checksum := sha256.Sum256(archiveContent)
	checksumContent := []byte(hex.EncodeToString(checksum[:]) + "  " + filepath.Base(archivePath) + "\n")

	previousHTTPClient := dispatcherHTTPClient
	defer func() {
		dispatcherHTTPClient = previousHTTPClient
	}()
	dispatcherHTTPClient = &http.Client{
		Transport: dispatcherRoundTripFunc(func(request *http.Request) (*http.Response, error) {
			content := []byte{}
			statusCode := http.StatusNotFound
			if strings.HasSuffix(request.URL.Path, "/uloop-project-runner-darwin-arm64.tar.gz") {
				content = archiveContent
				statusCode = http.StatusOK
			}
			if strings.HasSuffix(request.URL.Path, "/uloop-project-runner-darwin-arm64.tar.gz.sha256") {
				content = checksumContent
				statusCode = http.StatusOK
			}
			return &http.Response{
				StatusCode: statusCode,
				Status:     http.StatusText(statusCode),
				Body:       io.NopCloser(bytes.NewReader(content)),
			}, nil
		}),
	}

	var seenReleaseTag string
	var seenAssetURL string
	var seenWorkflowPath string
	previous := verifyReleaseAssetAttestation
	verifyReleaseAssetAttestation = func(_ context.Context, releaseTag string, assetURL string, _ string, workflowPath string) error {
		seenReleaseTag = releaseTag
		seenAssetURL = assetURL
		seenWorkflowPath = workflowPath
		return nil
	}
	defer func() {
		verifyReleaseAssetAttestation = previous
	}()

	var stderr bytes.Buffer
	if _, err := downloadDispatcherRealCLIForPin(
		context.Background(),
		t.TempDir(),
		dispatcherPin{ProjectRunnerVersion: "3.0.0-beta.88"},
		"darwin",
		"arm64",
		&stderr); err != nil {
		t.Fatalf("downloadDispatcherRealCLIForPin failed: %v", err)
	}
	if seenReleaseTag != "uloop-project-runner-v3.0.0-beta.88" {
		t.Fatalf("attestation hook received wrong release tag: %s", seenReleaseTag)
	}
	if !strings.HasSuffix(seenAssetURL, "/uloop-project-runner-v3.0.0-beta.88/uloop-project-runner-darwin-arm64.tar.gz") {
		t.Fatalf("attestation hook received wrong asset URL: %s", seenAssetURL)
	}
	if seenWorkflowPath != attestationRunnerPublishWorkflowPath {
		t.Fatalf("attestation hook received wrong workflow path: %s", seenWorkflowPath)
	}
}

func TestInstallDownloadedDispatcherRealCLIKeepsExistingExecutable(t *testing.T) {
	// Verifies concurrent downloads do not delete a ready executable another dispatcher already cached.
	tempDir := t.TempDir()
	realCLIPath := filepath.Join(tempDir, dispatcherRealCLIFileName(runtime.GOOS))
	tempRealCLIPath := filepath.Join(tempDir, "downloaded-"+dispatcherRealCLIFileName(runtime.GOOS))
	if err := os.WriteFile(realCLIPath, []byte("existing"), 0o755); err != nil {
		t.Fatalf("failed to write existing real CLI: %v", err)
	}
	if err := os.WriteFile(dispatcherRealCLIReadyPath(realCLIPath), []byte("ready\n"), 0o644); err != nil {
		t.Fatalf("failed to write ready marker: %v", err)
	}
	if err := os.WriteFile(tempRealCLIPath, []byte("downloaded"), 0o755); err != nil {
		t.Fatalf("failed to write temp real CLI: %v", err)
	}

	path, err := installDownloadedDispatcherRealCLI(tempRealCLIPath, realCLIPath)
	if err != nil {
		t.Fatalf("installDownloadedDispatcherRealCLI failed: %v", err)
	}
	if path != realCLIPath {
		t.Fatalf("real CLI path mismatch: %s", path)
	}
	assertFileContent(t, realCLIPath, "existing")
}

func TestInstallDownloadedDispatcherRealCLIKeepsReadyExecutableAfterRenameFailure(t *testing.T) {
	// Verifies a cache entry that becomes ready during install is reused instead of replaced.
	if runtime.GOOS == "windows" {
		t.Skip("Windows does not support the POSIX executable-bit setup this race regression uses.")
	}
	tempDir := t.TempDir()
	realCLIPath := filepath.Join(tempDir, dispatcherRealCLIFileName(runtime.GOOS))
	tempRealCLIPath := filepath.Join(tempDir, "downloaded-"+dispatcherRealCLIFileName(runtime.GOOS))
	if err := os.WriteFile(realCLIPath, []byte("existing"), 0o644); err != nil {
		t.Fatalf("failed to write existing real CLI: %v", err)
	}
	if err := os.WriteFile(dispatcherRealCLIReadyPath(realCLIPath), []byte("ready\n"), 0o644); err != nil {
		t.Fatalf("failed to write ready marker: %v", err)
	}
	if err := os.WriteFile(tempRealCLIPath, []byte("downloaded"), 0o755); err != nil {
		t.Fatalf("failed to write temp real CLI: %v", err)
	}

	previousRename := dispatcherRename
	defer func() {
		dispatcherRename = previousRename
	}()
	renameCalls := 0
	dispatcherRename = func(oldPath string, newPath string) error {
		renameCalls++
		if oldPath != tempRealCLIPath || newPath != realCLIPath {
			t.Fatalf("rename paths mismatch: %s -> %s", oldPath, newPath)
		}
		if renameCalls == 1 {
			if err := os.Chmod(realCLIPath, 0o755); err != nil {
				t.Fatalf("failed to mark existing real CLI executable: %v", err)
			}
			return errors.New("destination exists")
		}
		return previousRename(oldPath, newPath)
	}

	path, err := installDownloadedDispatcherRealCLI(tempRealCLIPath, realCLIPath)
	if err != nil {
		t.Fatalf("installDownloadedDispatcherRealCLI failed: %v", err)
	}
	if path != realCLIPath {
		t.Fatalf("real CLI path mismatch: %s", path)
	}
	if renameCalls != 1 {
		t.Fatalf("rename call count mismatch: %d", renameCalls)
	}
	assertFileContent(t, realCLIPath, "existing")
	assertFileContent(t, dispatcherRealCLIReadyPath(realCLIPath), "ready\n")
}

func TestInstallDownloadedDispatcherRealCLIReplacesExecutableWithoutReady(t *testing.T) {
	// Verifies executable files without READY are treated as incomplete cache entries.
	tempDir := t.TempDir()
	realCLIPath := filepath.Join(tempDir, dispatcherRealCLIFileName(runtime.GOOS))
	tempRealCLIPath := filepath.Join(tempDir, "downloaded-"+dispatcherRealCLIFileName(runtime.GOOS))
	if err := os.WriteFile(realCLIPath, []byte("existing"), 0o755); err != nil {
		t.Fatalf("failed to write existing real CLI: %v", err)
	}
	if err := os.WriteFile(tempRealCLIPath, []byte("downloaded"), 0o755); err != nil {
		t.Fatalf("failed to write temp real CLI: %v", err)
	}

	path, err := installDownloadedDispatcherRealCLI(tempRealCLIPath, realCLIPath)
	if err != nil {
		t.Fatalf("installDownloadedDispatcherRealCLI failed: %v", err)
	}
	if path != realCLIPath {
		t.Fatalf("real CLI path mismatch: %s", path)
	}
	assertFileContent(t, realCLIPath, "downloaded")
	assertFileContent(t, dispatcherRealCLIReadyPath(realCLIPath), "ready\n")
}

func TestLoadDispatcherPinFallsBackToPackagePin(t *testing.T) {
	// Verifies dispatcher can read the package-level pin when the project copy is missing.
	projectRoot := createDispatcherUnityProject(t)
	packageRoot := filepath.Join(projectRoot, "Packages", "src")
	if err := os.MkdirAll(packageRoot, 0o755); err != nil {
		t.Fatalf("failed to create package root: %v", err)
	}
	writeDispatcherPinFile(t, filepath.Join(packageRoot, dispatcherPackagePinFileName), "3.0.0-beta.55")

	pin, err := loadDispatcherPin(projectRoot)
	if err != nil {
		t.Fatalf("loadDispatcherPin failed: %v", err)
	}
	if pin.ProjectRunnerVersion != "3.0.0-beta.55" {
		t.Fatalf("projectRunnerVersion mismatch: %s", pin.ProjectRunnerVersion)
	}
}

func TestLoadDispatcherPinSkipsInvalidPackageCandidate(t *testing.T) {
	// Verifies stale package pins do not block a valid PackageCache pin during first startup.
	projectRoot := createDispatcherUnityProject(t)
	sourcePackageRoot := filepath.Join(projectRoot, "Packages", "src")
	cachePackageRoot := filepath.Join(projectRoot, "Library", "PackageCache", dispatcherUnityPackageName+"@3.0.0-beta.57")
	if err := os.MkdirAll(sourcePackageRoot, 0o755); err != nil {
		t.Fatalf("failed to create source package root: %v", err)
	}
	if err := os.WriteFile(filepath.Join(sourcePackageRoot, dispatcherPackagePinFileName), []byte("{"), 0o644); err != nil {
		t.Fatalf("failed to write invalid package pin: %v", err)
	}
	writeDispatcherPinFile(t, filepath.Join(cachePackageRoot, dispatcherPackagePinFileName), "3.0.0-beta.57")

	pin, err := loadDispatcherPin(projectRoot)
	if err != nil {
		t.Fatalf("loadDispatcherPin failed: %v", err)
	}
	if pin.ProjectRunnerVersion != "3.0.0-beta.57" {
		t.Fatalf("projectRunnerVersion mismatch: %s", pin.ProjectRunnerVersion)
	}
}

func TestLoadDispatcherPinFindsPackageCachePinWithBracketedProjectPath(t *testing.T) {
	// Verifies PackageCache pin discovery treats glob metacharacters in the project path literally.
	projectRoot := filepath.Join(t.TempDir(), "project[legacy]")
	for _, directory := range []string{"Assets", "ProjectSettings"} {
		if err := os.MkdirAll(filepath.Join(projectRoot, directory), 0o755); err != nil {
			t.Fatalf("failed to create Unity project directory: %v", err)
		}
	}
	cachePackageRoot := filepath.Join(projectRoot, "Library", "PackageCache", dispatcherUnityPackageName+"@3.0.0-beta.57")
	writeDispatcherPinFile(t, filepath.Join(cachePackageRoot, dispatcherPackagePinFileName), "3.0.0-beta.57")

	pin, err := loadDispatcherPin(projectRoot)
	if err != nil {
		t.Fatalf("loadDispatcherPin failed: %v", err)
	}
	if pin.ProjectRunnerVersion != "3.0.0-beta.57" {
		t.Fatalf("projectRunnerVersion mismatch: %s", pin.ProjectRunnerVersion)
	}
}

func TestLoadDispatcherPinNormalizesVersionPrefixes(t *testing.T) {
	// Verifies v-prefixed pin versions are normalized before semantic-version validation.
	projectRoot := createDispatcherUnityProject(t)
	pinPath := filepath.Join(projectRoot, dispatcherProjectPinRelativePath)
	writeDispatcherPinFileWithMinimum(t, pinPath, "v3.0.0-beta.58", "V3.0.0-beta.39")

	pin, err := loadDispatcherPin(projectRoot)
	if err != nil {
		t.Fatalf("loadDispatcherPin failed: %v", err)
	}
	if pin.ProjectRunnerVersion != "3.0.0-beta.58" {
		t.Fatalf("projectRunnerVersion mismatch: %s", pin.ProjectRunnerVersion)
	}
	if pin.MinimumDispatcherVersion != "3.0.0-beta.39" {
		t.Fatalf("minimumDispatcherVersion mismatch: %s", pin.MinimumDispatcherVersion)
	}
}

func TestLoadDispatcherPinIgnoresBootstrapFields(t *testing.T) {
	// Verifies old dispatchers ignore additive bootstrap fields and retain their existing pin behavior.
	projectRoot := createDispatcherUnityProject(t)
	pinPath := filepath.Join(projectRoot, dispatcherProjectPinRelativePath)
	if err := os.MkdirAll(filepath.Dir(pinPath), 0o755); err != nil {
		t.Fatalf("failed to create pin directory: %v", err)
	}
	content := `{"projectRunnerVersion":"3.0.0-beta.58","minimumDispatcherVersion":"3.0.0-beta.39","dispatcherReleaseTag":"dispatcher-v3.0.1","dispatcherArchiveManifest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh"}`
	if err := os.WriteFile(pinPath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write pin: %v", err)
	}

	pin, err := loadDispatcherPin(projectRoot)
	if err != nil {
		t.Fatalf("loadDispatcherPin failed: %v", err)
	}
	if pin.ProjectRunnerVersion != "3.0.0-beta.58" {
		t.Fatalf("projectRunnerVersion mismatch: %s", pin.ProjectRunnerVersion)
	}
	if pin.MinimumDispatcherVersion != "3.0.0-beta.39" {
		t.Fatalf("minimumDispatcherVersion mismatch: %s", pin.MinimumDispatcherVersion)
	}
}

func TestLoadDispatcherPinRejectsInvalidProjectRunnerVersion(t *testing.T) {
	// Verifies project pin projectRunnerVersion must be a release version, not a filesystem path.
	projectRoot := createDispatcherUnityProject(t)
	pinPath := filepath.Join(projectRoot, dispatcherProjectPinRelativePath)
	if err := os.MkdirAll(filepath.Dir(pinPath), 0o755); err != nil {
		t.Fatalf("failed to create pin directory: %v", err)
	}
	content := `{"projectRunnerVersion":"../../payload","minimumDispatcherVersion":"3.0.0-beta.39"}`
	if err := os.WriteFile(pinPath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write pin: %v", err)
	}

	_, err := loadDispatcherPin(projectRoot)

	if err == nil {
		t.Fatal("expected invalid projectRunnerVersion error")
	}
}

func TestLoadDispatcherPinRejectsProjectRunnerVersionWithInvalidBuildMetadata(t *testing.T) {
	// Verifies project pin build metadata cannot smuggle path segments through semver validation.
	projectRoot := createDispatcherUnityProject(t)
	pinPath := filepath.Join(projectRoot, dispatcherProjectPinRelativePath)
	writeDispatcherPinFileWithMinimum(t, pinPath, "3.0.0+../../payload", "3.0.0-beta.39")

	_, err := loadDispatcherPin(projectRoot)

	if err == nil {
		t.Fatal("expected invalid projectRunnerVersion error")
	}
}

func TestLoadDispatcherPinRejectsInvalidMinimumDispatcherVersion(t *testing.T) {
	// Verifies malformed dispatcher minimums fail closed instead of bypassing freshness checks.
	projectRoot := createDispatcherUnityProject(t)
	pinPath := filepath.Join(projectRoot, dispatcherProjectPinRelativePath)
	writeDispatcherPinFileWithMinimum(t, pinPath, "3.0.0-beta.58", "../../payload")

	_, err := loadDispatcherPin(projectRoot)

	if err == nil {
		t.Fatal("expected invalid minimumDispatcherVersion error")
	}
}

func TestLoadDispatcherPinRejectsMinimumDispatcherVersionWithLeadingZero(t *testing.T) {
	// Verifies dispatcher pin validation rejects versions the shared comparator cannot order.
	projectRoot := createDispatcherUnityProject(t)
	pinPath := filepath.Join(projectRoot, dispatcherProjectPinRelativePath)
	writeDispatcherPinFileWithMinimum(t, pinPath, "3.0.0-beta.58", "3.00.1")

	_, err := loadDispatcherPin(projectRoot)

	if err == nil {
		t.Fatal("expected invalid minimumDispatcherVersion error")
	}
}

func TestLoadDispatcherPinFailsWhenPinFileMissing(t *testing.T) {
	// Verifies loadDispatcherPin now requires a pin JSON and does not fall back to CliConstants.cs.
	projectRoot := createDispatcherUnityProject(t)
	constantsPath := filepath.Join(projectRoot, "Packages", "src", "Editor", "Domain", "CliConstants.cs")
	if err := os.MkdirAll(filepath.Dir(constantsPath), 0o755); err != nil {
		t.Fatalf("failed to create constants directory: %v", err)
	}
	content := `public const int REQUIRED_CLI_PROTOCOL_VERSION = 3;`
	if err := os.WriteFile(constantsPath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write constants: %v", err)
	}

	_, err := loadDispatcherPin(projectRoot)

	if err == nil {
		t.Fatal("expected pin resolution to fail when no pin JSON is present")
	}
	if !strings.Contains(err.Error(), "project runner pin not found") {
		t.Fatalf("expected 'project runner pin not found' message, got: %v", err)
	}
}

func TestRunDispatcherMissingPinEmitsPinResolutionGuidance(t *testing.T) {
	// Verifies the dispatcher surfaces the pin-resolution error envelope with NextActions guidance when the pin is missing.
	projectRoot := createDispatcherUnityProject(t)
	t.Setenv(nativepath.CacheDirEnvName, t.TempDir())
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")
	t.Chdir(projectRoot)

	deps := defaultDispatcherRunDeps()

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, &stdout, &stderr, deps)

	if code == 0 {
		t.Fatalf("expected non-zero exit when pin is missing, stderr=%s", stderr.String())
	}
	stderrText := stderr.String()
	if !strings.Contains(stderrText, "Could not resolve the required uloop project runner") {
		t.Fatalf("expected pin resolution error message, got: %s", stderrText)
	}
	if !strings.Contains(stderrText, "project-runner-pin.json") {
		t.Fatalf("expected NextActions to reference project-runner-pin.json, got: %s", stderrText)
	}
}

func TestDispatcherReleaseAssetNameRejectsUnsupportedPlatform(t *testing.T) {
	// Verifies dispatcher does not invent download assets for unsupported platforms.
	_, err := dispatcherReleaseAssetName("linux", "amd64")

	if err == nil {
		t.Fatal("expected unsupported platform error")
	}
}

func TestResolveDispatcherProjectRootLaunchDoesNotRequireExistingEndpointDirectory(t *testing.T) {
	// Verifies first launch resolves the Unity project before its private IPC directory exists.
	projectRoot := createDispatcherUnityProject(t)

	resolved, err := resolveDispatcherProjectRoot(projectRoot, "", []string{"launch"})
	if err != nil {
		t.Fatalf("first launch project resolution failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("unexpected launch project root: got %s want %s", resolved, projectRoot)
	}
}

func createDispatcherUnityProject(t *testing.T) string {
	t.Helper()
	projectRoot := t.TempDir()
	for _, dirName := range []string{"Assets", "ProjectSettings"} {
		if err := os.MkdirAll(filepath.Join(projectRoot, dirName), 0o755); err != nil {
			t.Fatalf("failed to create Unity project directory: %v", err)
		}
	}
	return projectRoot
}

func writeDispatcherProjectPin(t *testing.T, projectRoot string, projectRunnerVersion string) {
	t.Helper()
	pinPath := filepath.Join(projectRoot, dispatcherProjectPinRelativePath)
	writeDispatcherPinFile(t, pinPath, projectRunnerVersion)
}

func writeDispatcherPinFile(t *testing.T, pinPath string, projectRunnerVersion string) {
	t.Helper()
	writeDispatcherPinFileWithMinimum(t, pinPath, projectRunnerVersion, dispatcherVersion)
}

func writeDispatcherPinFileWithMinimum(t *testing.T, pinPath string, projectRunnerVersion string, minimumDispatcherVersion string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(pinPath), 0o755); err != nil {
		t.Fatalf("failed to create pin directory: %v", err)
	}
	content := `{"projectRunnerVersion":"` +
		projectRunnerVersion +
		`","minimumDispatcherVersion":"` +
		minimumDispatcherVersion +
		`"}`
	if err := os.WriteFile(pinPath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write pin: %v", err)
	}
}

func writeCachedDispatcherRealCLI(t *testing.T, cacheRoot string, projectRunnerVersion string) string {
	t.Helper()
	realCLIPath := dispatcherCachedRealCLIPath(cacheRoot, projectRunnerVersion, runtime.GOOS, runtime.GOARCH)
	if err := os.MkdirAll(filepath.Dir(realCLIPath), 0o755); err != nil {
		t.Fatalf("failed to create cached CLI directory: %v", err)
	}
	if err := os.WriteFile(realCLIPath, []byte("cached real cli"), 0o755); err != nil {
		t.Fatalf("failed to write cached CLI: %v", err)
	}
	if err := os.WriteFile(dispatcherRealCLIReadyPath(realCLIPath), []byte("ready\n"), 0o644); err != nil {
		t.Fatalf("failed to write cached CLI ready marker: %v", err)
	}
	return realCLIPath
}

func writeDispatcherTarGzArchive(t *testing.T, archivePath string, entries []dispatcherArchiveTestEntry) {
	t.Helper()
	file, err := os.Create(archivePath)
	if err != nil {
		t.Fatalf("failed to create tar archive: %v", err)
	}
	gzipWriter := gzip.NewWriter(file)
	tarWriter := tar.NewWriter(gzipWriter)
	for _, entry := range entries {
		content := []byte(entry.Content)
		header := &tar.Header{
			Name: entry.Name,
			Mode: 0o755,
			Size: int64(len(content)),
		}
		if err := tarWriter.WriteHeader(header); err != nil {
			t.Fatalf("failed to write tar header: %v", err)
		}
		if _, err := tarWriter.Write(content); err != nil {
			t.Fatalf("failed to write tar content: %v", err)
		}
	}
	closeErr := tarWriter.Close()
	gzipCloseErr := gzipWriter.Close()
	fileCloseErr := file.Close()
	if closeErr != nil {
		t.Fatalf("failed to close tar archive: %v", closeErr)
	}
	if gzipCloseErr != nil {
		t.Fatalf("failed to close gzip archive: %v", gzipCloseErr)
	}
	if fileCloseErr != nil {
		t.Fatalf("failed to close tar file: %v", fileCloseErr)
	}
}

func writeDispatcherZipArchive(t *testing.T, archivePath string, entries []dispatcherArchiveTestEntry) {
	t.Helper()
	file, err := os.Create(archivePath)
	if err != nil {
		t.Fatalf("failed to create zip archive: %v", err)
	}
	zipWriter := zip.NewWriter(file)
	for _, entry := range entries {
		writer, err := zipWriter.Create(entry.Name)
		if err != nil {
			t.Fatalf("failed to write zip header: %v", err)
		}
		if _, err := writer.Write([]byte(entry.Content)); err != nil {
			t.Fatalf("failed to write zip content: %v", err)
		}
	}
	closeErr := zipWriter.Close()
	fileCloseErr := file.Close()
	if closeErr != nil {
		t.Fatalf("failed to close zip archive: %v", closeErr)
	}
	if fileCloseErr != nil {
		t.Fatalf("failed to close zip file: %v", fileCloseErr)
	}
}

func assertFileContent(t *testing.T, filePath string, expected string) {
	t.Helper()
	content, err := os.ReadFile(filePath)
	if err != nil {
		t.Fatalf("failed to read %s: %v", filePath, err)
	}
	if string(content) != expected {
		t.Fatalf("file content mismatch: %q", string(content))
	}
}

func assertStringSliceEqual(t *testing.T, actual []string, expected []string) {
	t.Helper()
	if len(actual) != len(expected) {
		t.Fatalf("length mismatch: actual=%#v expected=%#v", actual, expected)
	}
	for index, expectedValue := range expected {
		if actual[index] != expectedValue {
			t.Fatalf("value mismatch at %d: actual=%#v expected=%#v", index, actual, expected)
		}
	}
}
