package cli

import (
	"bytes"
	"context"
	"strings"
	"testing"
)

func TestRunProjectLocalInstallHelpDoesNotRequireUnityProject(t *testing.T) {
	// Verifies install help is available before Unity project resolution.
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"install", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("install help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{"Usage:", "uloop install", "--dir <install-dir>", "shell PATH", "legacy npm"} {
		if !strings.Contains(output, expected) {
			t.Fatalf("install help missing %q:\n%s", expected, output)
		}
	}
}

func TestParseInstallOptionsAcceptsDirAlias(t *testing.T) {
	// Verifies installer scripts can use the Antigravity-style short directory flag.
	options, err := parseInstallOptions([]string{"-d", `C:\Tools\uloop`})
	if err != nil {
		t.Fatalf("parseInstallOptions failed: %v", err)
	}

	if options.installDir != `C:\Tools\uloop` {
		t.Fatalf("install dir mismatch: %s", options.installDir)
	}
}

func TestResolveNativeInstallDirForWindowsUsesLocalAppData(t *testing.T) {
	// Verifies the native install command resolves the same default Windows install directory as the installer.
	previousGetenv := getenv
	defer func() {
		getenv = previousGetenv
	}()
	getenv = func(name string) string {
		switch name {
		case nativeInstallDirEnvName:
			return ""
		case nativeLocalAppDataEnvName:
			return `C:\Users\ExampleUser\AppData\Local`
		default:
			return ""
		}
	}

	installDir, err := resolveNativeInstallDir("windows", "")
	if err != nil {
		t.Fatalf("resolveNativeInstallDir failed: %v", err)
	}

	expected := `C:\Users\ExampleUser\AppData\Local\Programs\uloop\bin`
	if installDir != expected {
		t.Fatalf("install dir mismatch: %s", installDir)
	}
}

func TestResolveNativeInstallDirForMacUsesHome(t *testing.T) {
	// Verifies the native install command resolves the same default macOS install directory as the installer.
	previousGetenv := getenv
	previousNativeUserHomeDir := nativeUserHomeDir
	defer func() {
		getenv = previousGetenv
		nativeUserHomeDir = previousNativeUserHomeDir
	}()
	getenv = func(name string) string {
		return ""
	}
	nativeUserHomeDir = func() (string, error) {
		return "/Users/ExampleUser", nil
	}

	installDir, err := resolveNativeInstallDir("darwin", "")
	if err != nil {
		t.Fatalf("resolveNativeInstallDir failed: %v", err)
	}

	expected := "/Users/ExampleUser/.local/bin"
	if installDir != expected {
		t.Fatalf("install dir mismatch: %s", installDir)
	}
}

func TestWriteInstallCompletionForWindowsMentionsPathAndLegacyCleanup(t *testing.T) {
	// Verifies Windows install output explains both native setup responsibilities.
	var stdout bytes.Buffer

	writeInstallCompletion(&stdout, "windows")

	output := stdout.String()
	for _, expected := range []string{"User PATH", "Legacy npm uloop-cli"} {
		if !strings.Contains(output, expected) {
			t.Fatalf("install completion missing %q:\n%s", expected, output)
		}
	}
}

func TestWriteInstallCompletionForMacMentionsPathAndLegacyCleanup(t *testing.T) {
	// Verifies macOS install output explains both native setup responsibilities.
	var stdout bytes.Buffer

	writeInstallCompletion(&stdout, "darwin")

	output := stdout.String()
	for _, expected := range []string{"shell PATH", "Legacy npm uloop-cli"} {
		if !strings.Contains(output, expected) {
			t.Fatalf("install completion missing %q:\n%s", expected, output)
		}
	}
}

func TestInstallSetupFailureErrorIncludesInstallerStderr(t *testing.T) {
	// Verifies installer stderr is preserved inside the JSON error envelope details.
	cliErr := installSetupFailureError(context.Canceled, "warning before failure\n")

	if cliErr.ErrorCode != errorCodeInternalError {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Details["cause"] != context.Canceled.Error() {
		t.Fatalf("cause detail mismatch: %#v", cliErr.Details)
	}
	if cliErr.Details["installerStderr"] != "warning before failure" {
		t.Fatalf("installer stderr detail mismatch: %#v", cliErr.Details)
	}
}
