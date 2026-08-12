package dispatcher

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"io"
	"net/http"
	"path/filepath"
	"runtime"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/dispatcher/dispatchercontract"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
)

func TestUpdateCommandForDarwinUsesDirectInstaller(t *testing.T) {
	// Verifies dispatcher update downloads and verifies the release installer before running it on the matching channel.
	command, err := update.CommandForOS("darwin", update.Options{
		CurrentVersion: dispatcherVersion,
	})
	if err != nil {
		t.Fatalf("updateCommandForOS failed: %v", err)
	}

	if command.Name != "sh" {
		t.Fatalf("command mismatch: %s", command.Name)
	}
	expectedScriptURL := update.ScriptAssetURL(dispatchercontract.DispatcherCurrent.DispatcherVersion, update.PosixScriptName)
	expectedReleaseTag := update.UpdateSelectorForVersion(dispatchercontract.DispatcherCurrent.DispatcherVersion)
	if command.InstallerURL != expectedScriptURL {
		t.Fatalf("installer URL mismatch: %s", command.InstallerURL)
	}
	if command.InstallerChecksumURL != expectedScriptURL+".sha256" {
		t.Fatalf("installer checksum URL mismatch: %s", command.InstallerChecksumURL)
	}
	if !stringSliceContains(command.Env, "ULOOP_VERSION="+expectedReleaseTag) {
		t.Fatalf("installer version missing: %v", command.Env)
	}
	if command.InstallerName != update.PosixScriptName {
		t.Fatalf("installer name mismatch: %s", command.InstallerName)
	}
}

func TestUpdateCommandForWindowsUsesPowerShellInstaller(t *testing.T) {
	// Verifies dispatcher update downloads and verifies the Windows release installer on the matching channel.
	command, err := update.CommandForOS("windows", update.Options{
		CurrentVersion: dispatcherVersion,
	})
	if err != nil {
		t.Fatalf("updateCommandForOS failed: %v", err)
	}

	if command.Name != "powershell" {
		t.Fatalf("command mismatch: %s", command.Name)
	}
	expectedScriptURL := update.ScriptAssetURL(dispatchercontract.DispatcherCurrent.DispatcherVersion, update.WindowsScriptName)
	expectedReleaseTag := update.UpdateSelectorForVersion(dispatchercontract.DispatcherCurrent.DispatcherVersion)
	if command.InstallerURL != expectedScriptURL {
		t.Fatalf("installer URL mismatch: %s", command.InstallerURL)
	}
	if command.InstallerChecksumURL != expectedScriptURL+".sha256" {
		t.Fatalf("installer checksum URL mismatch: %s", command.InstallerChecksumURL)
	}
	if !stringSliceContains(command.Env, "ULOOP_VERSION="+expectedReleaseTag) {
		t.Fatalf("installer version missing: %v", command.Env)
	}
	if command.InstallerName != update.WindowsScriptName {
		t.Fatalf("installer name mismatch: %s", command.InstallerName)
	}
}

func TestUpdateCommandForDarwinUsesRequestedVersion(t *testing.T) {
	// Verifies dispatcher update can target the minimum release version requested by Unity.
	command, err := update.CommandForOS("darwin", update.Options{
		CurrentVersion: dispatcherVersion,
		TargetVersion:  "3.0.0-beta.6",
	})
	if err != nil {
		t.Fatalf("updateCommandForOSWithOptions failed: %v", err)
	}

	if command.Name != "sh" {
		t.Fatalf("command mismatch: %s", command.Name)
	}
	if !strings.Contains(command.InstallerURL, "dispatcher-v3.0.0-beta.6/install.sh") {
		t.Fatalf("installer URL mismatch: %s", command.InstallerURL)
	}
	if !stringSliceContains(command.Env, "ULOOP_VERSION=dispatcher-v3.0.0-beta.6") {
		t.Fatalf("installer version missing: %v", command.Env)
	}
}

func TestUpdateCommandForDarwinNormalizesRequestedVersionPrefix(t *testing.T) {
	// Verifies accepted v-prefixed semantic versions still resolve to valid dispatcher release tags.
	command, err := update.CommandForOS("darwin", update.Options{
		CurrentVersion: dispatcherVersion,
		TargetVersion:  "v3.0.0-beta.6",
	})
	if err != nil {
		t.Fatalf("updateCommandForOSWithOptions failed: %v", err)
	}

	if command.Name != "sh" {
		t.Fatalf("command mismatch: %s", command.Name)
	}
	if !stringSliceContains(command.Env, "ULOOP_VERSION=dispatcher-v3.0.0-beta.6") {
		t.Fatalf("installer version should not contain a doubled v prefix: %v", command.Env)
	}
	if strings.Contains(command.InstallerURL, "dispatcher-vv3.0.0-beta.6") {
		t.Fatalf("installer URL contains doubled v prefix: %s", command.InstallerURL)
	}
}

func TestUpdateCommandForDarwinNormalizesProjectRunnerReleaseTag(t *testing.T) {
	// Verifies project runner release tags resolve to the matching dispatcher release.
	command, err := update.CommandForOS("darwin", update.Options{
		CurrentVersion: dispatcherVersion,
		TargetVersion:  "uloop-project-runner-v3.0.0-beta.6",
	})
	if err != nil {
		t.Fatalf("updateCommandForOSWithOptions failed: %v", err)
	}

	if command.Name != "sh" {
		t.Fatalf("command mismatch: %s", command.Name)
	}
	if !strings.Contains(command.InstallerURL, "dispatcher-v3.0.0-beta.6/install.sh") {
		t.Fatalf("installer URL mismatch: %s", command.InstallerURL)
	}
	if strings.Contains(command.InstallerURL, "dispatcher-vuloop-project-runner-v3.0.0-beta.6") {
		t.Fatalf("installer URL contains project runner prefix: %s", command.InstallerURL)
	}
}

func TestUpdateCommandForWindowsUsesRequestedVersion(t *testing.T) {
	// Verifies Windows dispatcher update can quietly target the minimum release version requested by Unity.
	command, err := update.CommandForOS("windows", update.Options{
		CurrentVersion: dispatcherVersion,
		TargetVersion:  "3.0.0",
	})
	if err != nil {
		t.Fatalf("updateCommandForOSWithOptions failed: %v", err)
	}

	if command.Name != "powershell" {
		t.Fatalf("command mismatch: %s", command.Name)
	}
	if !strings.Contains(command.InstallerURL, "dispatcher-v3.0.0/install.ps1") {
		t.Fatalf("installer URL mismatch: %s", command.InstallerURL)
	}
	if !stringSliceContains(command.Env, "ULOOP_VERSION=dispatcher-v3.0.0") {
		t.Fatalf("installer version missing: %v", command.Env)
	}
}

func TestParseUpdateOptionsNormalizesVersionPrefix(t *testing.T) {
	// Verifies parsed target versions are normalized before installer tag selection.
	options, err := parseUpdateOptions([]string{"--to-version", "v3.0.0-beta.6"})
	if err != nil {
		t.Fatalf("parseUpdateOptions failed: %v", err)
	}

	if options.targetVersion != "3.0.0-beta.6" {
		t.Fatalf("target version mismatch: %#v", options)
	}
}

func TestParseUpdateOptionsNormalizesProjectRunnerReleaseTag(t *testing.T) {
	// Verifies parsed project runner release tags are normalized before dispatcher tag selection.
	options, err := parseUpdateOptions([]string{"--to-version", "uloop-project-runner-v3.0.0-beta.6"})
	if err != nil {
		t.Fatalf("parseUpdateOptions failed: %v", err)
	}

	if options.targetVersion != "3.0.0-beta.6" {
		t.Fatalf("target version mismatch: %#v", options)
	}
}

func TestParseUpdateOptionsAcceptsEqualsSyntax(t *testing.T) {
	// Verifies AI-readable update commands may use a single --to-version=value token.
	options, err := parseUpdateOptions([]string{"--to-version=3.0.0-beta.6"})
	if err != nil {
		t.Fatalf("parseUpdateOptions failed: %v", err)
	}

	if options.targetVersion != "3.0.0-beta.6" {
		t.Fatalf("target version mismatch: %#v", options)
	}
}

func TestParseUpdateOptionsRejectsInvalidVersion(t *testing.T) {
	// Verifies invalid requested update versions fail before installer execution.
	_, err := parseUpdateOptions([]string{"--to-version", "not-a-version"})
	if err == nil {
		t.Fatal("expected invalid version error")
	}
}

func TestDownloadVerifiedUpdateInstallerRejectsChecksumMismatch(t *testing.T) {
	// Verifies update installers are not executable unless the downloaded checksum matches.
	restoreHTTPClient := stubUpdateInstallerHTTPClient([]byte("echo install\n"), []byte("bad  install.sh\n"))
	defer restoreHTTPClient()
	restoreAttestation := stubAttestationVerifyPasses()
	defer restoreAttestation()

	_, err := downloadVerifiedUpdateInstaller(context.Background(), update.Command{
		InstallerName:        update.PosixScriptName,
		InstallerURL:         "https://example.test/install.sh",
		InstallerChecksumURL: "https://example.test/install.sh.sha256",
	}, t.TempDir())

	if err == nil || !strings.Contains(err.Error(), "checksum mismatch") {
		t.Fatalf("expected checksum mismatch, got %v", err)
	}
}

func TestDownloadVerifiedUpdateInstallerReturnsVerifiedFile(t *testing.T) {
	// Verifies update installers are written to disk only after checksum verification succeeds.
	installerContent := []byte("echo install\n")
	checksum := sha256.Sum256(installerContent)
	checksumContent := []byte(hex.EncodeToString(checksum[:]) + "  install.sh\n")
	restoreHTTPClient := stubUpdateInstallerHTTPClient(installerContent, checksumContent)
	defer restoreHTTPClient()
	restoreAttestation := stubAttestationVerifyPasses()
	defer restoreAttestation()

	installerPath, err := downloadVerifiedUpdateInstaller(context.Background(), update.Command{
		InstallerName:        update.PosixScriptName,
		InstallerURL:         "https://example.test/install.sh",
		InstallerChecksumURL: "https://example.test/install.sh.sha256",
		ReleaseTag:           "dispatcher-v9.9.9",
	}, t.TempDir())
	if err != nil {
		t.Fatalf("downloadVerifiedUpdateInstaller failed: %v", err)
	}

	assertFileContent(t, installerPath, string(installerContent))
	if filepath.Base(installerPath) != update.PosixScriptName {
		t.Fatalf("installer path mismatch: %s", installerPath)
	}
}

func TestDownloadVerifiedUpdateInstallerFailsClosedOnAttestationError(t *testing.T) {
	// Verifies installers are rejected when attestation verification fails, even when the sha256 file matches.
	installerContent := []byte("echo install\n")
	checksum := sha256.Sum256(installerContent)
	checksumContent := []byte(hex.EncodeToString(checksum[:]) + "  install.sh\n")
	restoreHTTPClient := stubUpdateInstallerHTTPClient(installerContent, checksumContent)
	defer restoreHTTPClient()
	restoreAttestation := stubAttestationVerifyReturns(fmt.Errorf("simulated attestation failure"))
	defer restoreAttestation()

	_, err := downloadVerifiedUpdateInstaller(context.Background(), update.Command{
		InstallerName:        update.PosixScriptName,
		InstallerURL:         "https://example.test/install.sh",
		InstallerChecksumURL: "https://example.test/install.sh.sha256",
		ReleaseTag:           "dispatcher-v9.9.9",
	}, t.TempDir())

	if err == nil || !strings.Contains(err.Error(), "simulated attestation failure") {
		t.Fatalf("expected attestation failure to fail closed, got %v", err)
	}
}

func TestDownloadVerifiedUpdateInstallerPassesInstallerIdentityToAttestation(t *testing.T) {
	// Verifies the dispatcher-publish workflow SAN and dispatcher release tag are what get sent to the attestation hook.
	installerContent := []byte("echo install\n")
	checksum := sha256.Sum256(installerContent)
	checksumContent := []byte(hex.EncodeToString(checksum[:]) + "  install.sh\n")
	restoreHTTPClient := stubUpdateInstallerHTTPClient(installerContent, checksumContent)
	defer restoreHTTPClient()

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

	if _, err := downloadVerifiedUpdateInstaller(context.Background(), update.Command{
		InstallerName:        update.PosixScriptName,
		InstallerURL:         "https://example.test/install.sh",
		InstallerChecksumURL: "https://example.test/install.sh.sha256",
		ReleaseTag:           "dispatcher-v3.0.1-beta.12",
	}, t.TempDir()); err != nil {
		t.Fatalf("downloadVerifiedUpdateInstaller failed: %v", err)
	}
	if seenReleaseTag != "dispatcher-v3.0.1-beta.12" {
		t.Fatalf("attestation hook received wrong release tag: %s", seenReleaseTag)
	}
	if seenAssetURL != "https://example.test/install.sh" {
		t.Fatalf("attestation hook received wrong asset URL: %s", seenAssetURL)
	}
	if seenWorkflowPath != attestationDispatcherPublishWorkflowPath {
		t.Fatalf("attestation hook received wrong workflow path: %s", seenWorkflowPath)
	}
}

func TestUpdateExecutionArgsRunsDownloadedInstallerFile(t *testing.T) {
	// Verifies update execution runs the verified installer path directly.
	posixArgs := updateExecutionArgs(update.Command{Name: "sh"}, "/tmp/install.sh")
	if len(posixArgs) != 1 || posixArgs[0] != "/tmp/install.sh" {
		t.Fatalf("posix update args mismatch: %v", posixArgs)
	}

	windowsArgs := updateExecutionArgs(update.Command{Name: "powershell"}, `C:\Temp\install.ps1`)
	expected := []string{"-NoProfile", "-ExecutionPolicy", "Bypass", "-File", `C:\Temp\install.ps1`}
	if !stringSlicesEqual(windowsArgs, expected) {
		t.Fatalf("windows update args mismatch: %v", windowsArgs)
	}
}

func TestTryHandleUpdateRequestReportsVersionChange(t *testing.T) {
	// Verifies manual dispatcher updates tell users which dispatcher version was installed.
	skipWhenNativeUpdateIsUnsupported(t)
	restoreUpdateHooks := stubManualUpdateHooks(t, "9.9.9")
	defer restoreUpdateHooks()

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	handled, code := tryHandleUpdateRequest(context.Background(), []string{clicore.UpdateCommandName}, &stdout, &stderr)

	if !handled || code != 0 {
		t.Fatalf("update result mismatch: handled=%t code=%d stderr=%s", handled, code, stderr.String())
	}
	expected := "uloop dispatcher updated from " + dispatcherVersion + " to 9.9.9."
	if !bytes.Contains(stdout.Bytes(), []byte(expected)) {
		t.Fatalf("update output mismatch: %s", stdout.String())
	}
	if stderr.Len() != 0 {
		t.Fatalf("expected no stderr output, got: %s", stderr.String())
	}
}

func TestTryHandleUpdateRequestReportsAlreadyCurrentVersion(t *testing.T) {
	// Verifies manual dispatcher updates explain when the selected release matches the installed dispatcher.
	skipWhenNativeUpdateIsUnsupported(t)
	restoreUpdateHooks := stubManualUpdateHooks(t, dispatcherVersion)
	defer restoreUpdateHooks()

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	handled, code := tryHandleUpdateRequest(context.Background(), []string{clicore.UpdateCommandName}, &stdout, &stderr)

	if !handled || code != 0 {
		t.Fatalf("update result mismatch: handled=%t code=%d stderr=%s", handled, code, stderr.String())
	}
	expected := "uloop dispatcher is already up to date at " + dispatcherVersion + "."
	if !bytes.Contains(stdout.Bytes(), []byte(expected)) {
		t.Fatalf("update output mismatch: %s", stdout.String())
	}
	if stderr.Len() != 0 {
		t.Fatalf("expected no stderr output, got: %s", stderr.String())
	}
}

func TestUpdateRefusesHomebrewManagedExecutable(t *testing.T) {
	// Verifies Homebrew-managed installs refuse self-update and point users at brew upgrade.
	previousResolver := resolveUpdateExecutablePathFunc
	previousRunner := updateRunCommand
	defer func() {
		resolveUpdateExecutablePathFunc = previousResolver
		updateRunCommand = previousRunner
	}()
	homebrewPath := "/opt/homebrew/Cellar/uloop/3.0.0/bin/uloop"
	resolveUpdateExecutablePathFunc = func() (string, error) {
		return homebrewPath, nil
	}
	updateRunCommand = func(context.Context, update.Command, io.Writer, io.Writer) error {
		t.Fatal("updateRunCommand must not run for Homebrew-managed installs")
		return nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	handled, code := tryHandleUpdateRequest(context.Background(), []string{clicore.UpdateCommandName}, &stdout, &stderr)

	if !handled || code != 1 {
		t.Fatalf("update result mismatch: handled=%t code=%d stderr=%s", handled, code, stderr.String())
	}
	if !strings.Contains(stderr.String(), "brew upgrade uloop") {
		t.Fatalf("expected brew upgrade guidance in stderr, got: %s", stderr.String())
	}
}

func TestUpdateFailsWhenExecutablePathResolutionFails(t *testing.T) {
	// Verifies update aborts when the dispatcher executable path cannot be resolved.
	previousResolver := resolveUpdateExecutablePathFunc
	previousRunner := updateRunCommand
	defer func() {
		resolveUpdateExecutablePathFunc = previousResolver
		updateRunCommand = previousRunner
	}()
	resolveUpdateExecutablePathFunc = func() (string, error) {
		return "", errors.New("executable path unavailable")
	}
	updateRunCommand = func(context.Context, update.Command, io.Writer, io.Writer) error {
		t.Fatal("updateRunCommand must not run when executable path resolution fails")
		return nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	handled, code := tryHandleUpdateRequest(context.Background(), []string{clicore.UpdateCommandName}, &stdout, &stderr)

	if !handled || code != 1 {
		t.Fatalf("update result mismatch: handled=%t code=%d stderr=%s", handled, code, stderr.String())
	}
	if !strings.Contains(stderr.String(), "executable path unavailable") {
		t.Fatalf("expected resolution error in stderr, got: %s", stderr.String())
	}
}

func TestUpdateCommandForLinuxIsUnsupported(t *testing.T) {
	// Verifies Linux update fails before trying to run a platform-specific update.
	_, _, err := updateCommandForOS("linux")
	if err == nil {
		t.Fatal("expected unsupported OS error")
	}
	if !strings.Contains(err.Error(), "macOS and Windows") {
		t.Fatalf("unexpected linux error: %v", err)
	}
}

func TestUpdateCommandRejectsUnsupportedOS(t *testing.T) {
	// Verifies unknown OS values are rejected.
	_, _, err := updateCommandForOS("plan9")
	if err == nil {
		t.Fatal("expected unsupported OS error")
	}
}

func skipWhenNativeUpdateIsUnsupported(t *testing.T) {
	t.Helper()
	if runtime.GOOS == "darwin" || runtime.GOOS == "windows" {
		return
	}
	t.Skip("native update is supported only on macOS and Windows")
}

func stubManualUpdateHooks(t *testing.T, updatedVersion string) func() {
	t.Helper()
	previousRunner := updateRunCommand
	previousReader := dispatcherReadInstalledVersion
	previousResolver := resolveUpdateTargetVersionFunc
	previousManifest := fetchAttestationSubjectManifestFunc
	previousExecutablePath := resolveUpdateExecutablePathFunc
	updateRunCommand = func(context.Context, update.Command, io.Writer, io.Writer) error {
		return nil
	}
	dispatcherReadInstalledVersion = func(context.Context) (string, error) {
		return updatedVersion, nil
	}
	resolveUpdateTargetVersionFunc = func(ctx context.Context, options update.Options) (update.Options, error) {
		if options.TargetVersion == "" {
			options.TargetVersion = "9.9.9"
		}
		return options, nil
	}
	fetchAttestationSubjectManifestFunc = func(ctx context.Context, tag string) (string, error) {
		return "deadbeef  install.sh\n", nil
	}
	// Why: keep existing update tests off os.Executable so a Cellar-hosted test binary
	// cannot trip the Homebrew guard and change their assertions.
	resolveUpdateExecutablePathFunc = func() (string, error) {
		return "/Users/someone/.local/bin/uloop", nil
	}
	return func() {
		updateRunCommand = previousRunner
		dispatcherReadInstalledVersion = previousReader
		resolveUpdateTargetVersionFunc = previousResolver
		fetchAttestationSubjectManifestFunc = previousManifest
		resolveUpdateExecutablePathFunc = previousExecutablePath
	}
}

func stubAttestationVerifyPasses() func() {
	return stubAttestationVerifyReturns(nil)
}

func stubAttestationVerifyReturns(returnErr error) func() {
	previous := verifyReleaseAssetAttestation
	verifyReleaseAssetAttestation = func(context.Context, string, string, string, string) error {
		return returnErr
	}
	return func() {
		verifyReleaseAssetAttestation = previous
	}
}

func stubUpdateInstallerHTTPClient(installerContent []byte, checksumContent []byte) func() {
	previousHTTPClient := dispatcherHTTPClient
	dispatcherHTTPClient = &http.Client{
		Transport: dispatcherRoundTripFunc(func(request *http.Request) (*http.Response, error) {
			content := []byte{}
			statusCode := http.StatusNotFound
			if strings.HasSuffix(request.URL.Path, ".sha256") {
				content = checksumContent
				statusCode = http.StatusOK
			}
			if strings.HasSuffix(request.URL.Path, ".sh") || strings.HasSuffix(request.URL.Path, ".ps1") {
				content = installerContent
				statusCode = http.StatusOK
			}
			return &http.Response{
				StatusCode: statusCode,
				Status:     http.StatusText(statusCode),
				Body:       io.NopCloser(bytes.NewReader(content)),
			}, nil
		}),
	}
	return func() {
		dispatcherHTTPClient = previousHTTPClient
	}
}

func stringSliceContains(values []string, target string) bool {
	for _, value := range values {
		if value == target {
			return true
		}
	}
	return false
}

func stringSlicesEqual(left []string, right []string) bool {
	if len(left) != len(right) {
		return false
	}
	for index := range left {
		if left[index] != right[index] {
			return false
		}
	}
	return true
}
