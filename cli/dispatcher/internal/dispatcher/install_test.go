package dispatcher

import (
	"bytes"
	"context"
	"strings"
	"testing"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
)

func TestRunDispatcherInstallHelpDoesNotRequireUnityProject(t *testing.T) {
	// Verifies install help is available before Unity project resolution.
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{"install", "--help"}, &stdout, &stderr)

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
	t.Setenv(nativepath.InstallDirEnvName, "")
	t.Setenv(nativepath.LocalAppDataEnvName, `C:\Users\<USER_NAME>\AppData\Local`)

	installDir, err := resolveNativeInstallDir("windows", "")
	if err != nil {
		t.Fatalf("resolveNativeInstallDir failed: %v", err)
	}

	expected := `C:\Users\<USER_NAME>\AppData\Local\Programs\uloop\bin`
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

func TestWriteInstallCompletionForLinuxMentionsPathAndLegacyCleanup(t *testing.T) {
	// Verifies Linux install output explains both native setup responsibilities.
	var stdout bytes.Buffer

	writeInstallCompletion(&stdout, "linux")

	output := stdout.String()
	for _, expected := range []string{"shell PATH", "Legacy npm uloop-cli"} {
		if !strings.Contains(output, expected) {
			t.Fatalf("install completion missing %q:\n%s", expected, output)
		}
	}
}

func TestPrintInstallHelpAdvertisesLinuxSupport(t *testing.T) {
	// Verifies install help keeps the "On Linux," line that scripts/install.sh greps to detect native install support.
	var stdout bytes.Buffer

	printInstallHelp(&stdout)

	if !strings.Contains(stdout.String(), "\nOn Linux,") {
		t.Fatalf("install help missing the On Linux line:\n%s", stdout.String())
	}
}

func TestInstallSetupFailureErrorIncludesInstallerStderr(t *testing.T) {
	// Verifies installer stderr is preserved inside the JSON error envelope details.
	cliErr := installSetupFailureError(context.Canceled, "warning before failure\n")

	if cliErr.ErrorCode != clierrors.ErrorCodeInternalError {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Details["Cause"] != context.Canceled.Error() {
		t.Fatalf("cause detail mismatch: %#v", cliErr.Details)
	}
	if cliErr.Details["InstallerStderr"] != "warning before failure" {
		t.Fatalf("installer stderr detail mismatch: %#v", cliErr.Details)
	}
}

func TestParseInstallOptionsReadsDirFlagForms(t *testing.T) {
	// Verifies --dir accepts both the separate-value and equals forms.
	for _, args := range [][]string{{"--dir", "/opt/uloop"}, {"--dir=/opt/uloop"}} {
		options, err := parseInstallOptions(args)
		if err != nil {
			t.Fatalf("args %v: parseInstallOptions failed: %v", args, err)
		}
		if options.installDir != "/opt/uloop" {
			t.Fatalf("args %v: install dir mismatch: %s", args, options.installDir)
		}
	}
}

func TestParseInstallOptionsRejectsInvalidArguments(t *testing.T) {
	// Verifies unknown, duplicated, valueless, and positional install arguments are rejected with an argument error.
	cases := []struct {
		name        string
		args        []string
		wantMessage string
	}{
		{name: "unknown option", args: []string{"--prefix", "/opt"}, wantMessage: "Unknown install option: --prefix"},
		{name: "duplicate short flag", args: []string{"-d", "/a", "-d", "/b"}, wantMessage: "Duplicate install option: -d"},
		{name: "short flag after long flag", args: []string{"--dir", "/a", "-d", "/b"}, wantMessage: "Duplicate install option: -d"},
		{name: "long flag after short flag", args: []string{"-d", "/a", "--dir=/b"}, wantMessage: "Duplicate install option: --dir"},
		{name: "short flag without value", args: []string{"-d"}, wantMessage: "-d requires a value"},
		{name: "short flag followed by option", args: []string{"-d", "--dir"}, wantMessage: "-d requires a value"},
		{name: "positional argument", args: []string{"/opt/uloop"}, wantMessage: "/opt/uloop"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			options, err := parseInstallOptions(testCase.args)

			if err == nil || !strings.Contains(err.Error(), testCase.wantMessage) {
				t.Fatalf("expected error containing %q, got options=%+v err=%v", testCase.wantMessage, options, err)
			}
		})
	}
}

func TestTryHandleInstallRequestReportsInvalidOptions(t *testing.T) {
	// Verifies an invalid install option exits with code 1 before any installer step runs.
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	handled, code := tryHandleInstallRequest(context.Background(), []string{"install", "--prefix", "/opt"}, &stdout, &stderr)

	if !handled || code != 1 {
		t.Fatalf("result mismatch: handled=%t code=%d", handled, code)
	}
	if !strings.Contains(stderr.String(), "Unknown install option: --prefix") {
		t.Fatalf("missing option error: %s", stderr.String())
	}
	if stdout.Len() != 0 {
		t.Fatalf("no setup progress may be printed for invalid options: %s", stdout.String())
	}
}

func TestResolveNativeInstallDirRejectsUnsupportedOS(t *testing.T) {
	// Verifies platforms without an install convention report the install-specific unsupported message.
	t.Setenv(nativepath.InstallDirEnvName, "")

	_, err := resolveNativeInstallDir("plan9", "")

	if err == nil || err.Error() != installUnsupportedOSMessage {
		t.Fatalf("expected %q, got %v", installUnsupportedOSMessage, err)
	}
}

func TestWriteInstallCompletionForOtherOSPrintsGenericMessage(t *testing.T) {
	// Verifies platforms without PATH integration get a generic completion line.
	var stdout bytes.Buffer

	writeInstallCompletion(&stdout, "plan9")

	if stdout.String() != "Install setup completed.\n" {
		t.Fatalf("completion mismatch: %q", stdout.String())
	}
}
