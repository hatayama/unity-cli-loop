package dispatcher

import (
	"bytes"
	"os"
	"path/filepath"
	"runtime"
	"slices"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

func TestCompletionListCommandsIncludesNativeCommandsAndDefaultTools(t *testing.T) {
	var stdout bytes.Buffer
	handled, code := tryHandleCompletionRequest(
		[]string{"--list-commands"},
		clicore.LoadDefaultTools(),
		&stdout,
		&bytes.Buffer{},
	)

	if !handled {
		t.Fatal("completion request was not handled")
	}
	if code != 0 {
		t.Fatalf("exit code mismatch: %d", code)
	}

	output := stdout.String()
	for _, command := range []string{"completion", "focus-window", "sync", "uninstall"} {
		if !strings.Contains(output, command) {
			t.Fatalf("command %s was not listed: %s", command, output)
		}
	}
}

func TestCompletionListOptionsUsesToolSchema(t *testing.T) {
	var stdout bytes.Buffer
	handled, code := tryHandleCompletionRequest(
		[]string{"--list-options", "compile"},
		clicore.LoadDefaultTools(),
		&stdout,
		&bytes.Buffer{},
	)

	if !handled {
		t.Fatal("completion request was not handled")
	}
	if code != 0 {
		t.Fatalf("exit code mismatch: %d", code)
	}

	output := stdout.String()
	for _, option := range []string{"--force-recompile", "--no-wait-for-domain-reload", "--stop-on-external-scene-changes"} {
		if !strings.Contains(output, option) {
			t.Fatalf("option %s was not listed: %s", option, output)
		}
	}
}

func TestCompletionListOptionsPrefersProjectCacheForDefaultToolNames(t *testing.T) {
	// Verifies synced project tool metadata overrides embedded defaults for regular commands.
	var stdout bytes.Buffer
	cache := clicore.ToolsCache{
		Tools: []clicore.ToolDefinition{
			{
				Name: "compile",
				InputSchema: clicore.InputSchema{
					Type: "object",
					Properties: map[string]clicore.ToolProperty{
						"CachedOnly": {Type: "boolean"},
					},
				},
			},
		},
	}

	handled, code := tryHandleCompletionRequest(
		[]string{"--list-options", "compile"},
		cache,
		&stdout,
		&bytes.Buffer{},
	)

	if !handled {
		t.Fatal("completion request was not handled")
	}
	if code != 0 {
		t.Fatalf("exit code mismatch: %d", code)
	}

	output := stdout.String()
	if !strings.Contains(output, "--cached-only") {
		t.Fatalf("cached compile options were not used: %s", output)
	}
	if strings.Contains(output, "--stop-on-external-scene-changes") {
		t.Fatalf("embedded compile option should not be listed when cache exists: %s", output)
	}
}

func TestCompletionListOptionsUsesExecuteDynamicCodeWaitFlag(t *testing.T) {
	// Verifies shell completion exposes reload waiting as an explicit opt-in flag.
	var stdout bytes.Buffer
	handled, code := tryHandleCompletionRequest(
		[]string{"--list-options", clicore.ExecuteDynamicCodeCommandName},
		clicore.LoadDefaultTools(),
		&stdout,
		&bytes.Buffer{},
	)

	if !handled {
		t.Fatal("completion request was not handled")
	}
	if code != 0 {
		t.Fatalf("exit code mismatch: %d", code)
	}

	output := stdout.String()
	if !strings.Contains(output, "--wait-for-domain-reload") {
		t.Fatalf("execute-dynamic-code wait option was not listed: %s", output)
	}
	if !strings.Contains(output, "--code-file") {
		t.Fatalf("execute-dynamic-code code-file option was not listed: %s", output)
	}
	if strings.Contains(output, "--no-wait-for-domain-reload") {
		t.Fatalf("execute-dynamic-code no-wait option should not be listed: %s", output)
	}
	if strings.Contains(output, "--compile-only") {
		t.Fatalf("execute-dynamic-code internal compile-only option should stay hidden: %s", output)
	}
}

func TestCompletionListOptionsUsesEmbeddedExecuteDynamicCodeDefinition(t *testing.T) {
	// Verifies stale project caches do not hide hot-path execute-dynamic-code options.
	var stdout bytes.Buffer
	cache := clicore.ToolsCache{
		Tools: []clicore.ToolDefinition{
			{
				Name: "execute-dynamic-code",
				InputSchema: clicore.InputSchema{
					Properties: map[string]clicore.ToolProperty{
						"Code": {Type: "string"},
					},
				},
			},
		},
	}

	handled, code := tryHandleCompletionRequest(
		[]string{"--list-options", clicore.ExecuteDynamicCodeCommandName},
		cache,
		&stdout,
		&bytes.Buffer{},
	)

	if !handled {
		t.Fatal("completion request was not handled")
	}
	if code != 0 {
		t.Fatalf("exit code mismatch: %d", code)
	}

	output := stdout.String()
	if !strings.Contains(output, "--wait-for-domain-reload") {
		t.Fatalf("embedded execute-dynamic-code options were not used: %s", output)
	}
}

func TestCompletionListOptionsUsesNativeLaunchOptions(t *testing.T) {
	// Verifies shell completion still suggests native launch flags after CLI unification.
	var stdout bytes.Buffer
	handled, code := tryHandleCompletionRequest(
		[]string{"--list-options", clicore.LaunchCommandName},
		clicore.LoadDefaultTools(),
		&stdout,
		&bytes.Buffer{},
	)

	if !handled {
		t.Fatal("completion request was not handled")
	}
	if code != 0 {
		t.Fatalf("exit code mismatch: %d", code)
	}

	output := stdout.String()
	listedOptions := strings.Split(strings.TrimSpace(output), "\n")
	for _, option := range []string{"--project-path", "--restart", "--quit", "--delete-recovery", "--platform", "--max-depth", "--editor-version"} {
		if !slices.Contains(listedOptions, option) {
			t.Fatalf("launch option %s was not listed: %s", option, output)
		}
	}
	for _, option := range []string{"-i", "--ignore-compiler-errors"} {
		if slices.Contains(listedOptions, option) {
			t.Fatalf("removed launch option %s was listed: %s", option, output)
		}
	}
}

func TestCompletionListOptionsUsesNativeUpdateOptions(t *testing.T) {
	// Verifies shell completion suggests exact update target flags.
	var stdout bytes.Buffer
	handled, code := tryHandleCompletionRequest(
		[]string{"--list-options", clicore.UpdateCommandName},
		clicore.LoadDefaultTools(),
		&stdout,
		&bytes.Buffer{},
	)

	if !handled {
		t.Fatal("completion request was not handled")
	}
	if code != 0 {
		t.Fatalf("exit code mismatch: %d", code)
	}
	if !strings.Contains(stdout.String(), "--to-version") {
		t.Fatalf("update option was not listed: %s", stdout.String())
	}
}

func TestCompletionCommandListOptionsUsesNativeCompletionOptions(t *testing.T) {
	// Verifies nested completion option probes preserve dispatcher-compatible behavior.
	var stdout bytes.Buffer
	handled, code := tryHandleCompletionRequest(
		[]string{clicore.CompletionCommand, "--list-options", clicore.CompletionCommand},
		clicore.LoadDefaultTools(),
		&stdout,
		&bytes.Buffer{},
	)

	if !handled {
		t.Fatal("completion request was not handled")
	}
	if code != 0 {
		t.Fatalf("exit code mismatch: %d", code)
	}

	output := stdout.String()
	for _, option := range []string{"--install", "--shell"} {
		if !strings.Contains(output, option) {
			t.Fatalf("completion option %s was not listed: %s", option, output)
		}
	}
}

func TestCompletionHelpDocumentsMachineReadableHelpers(t *testing.T) {
	// Verifies completion-specific probes are documented outside the main help surface.
	var stdout bytes.Buffer
	handled, code := tryHandleCompletionRequest(
		[]string{clicore.CompletionCommand, "--help"},
		clicore.LoadDefaultTools(),
		&stdout,
		&bytes.Buffer{},
	)

	if !handled {
		t.Fatal("completion request was not handled")
	}
	if code != 0 {
		t.Fatalf("exit code mismatch: %d", code)
	}

	output := stdout.String()
	for _, expected := range []string{"uloop --list-commands", "uloop --list-options <command>"} {
		if !strings.Contains(output, expected) {
			t.Fatalf("completion help missing %q:\n%s", expected, output)
		}
	}
}

func TestCompletionListOptionsIgnoresCachedToolSchemaForNativeCommand(t *testing.T) {
	// Verifies native commands keep priority when a cached Unity tool has the same name.
	var stdout bytes.Buffer
	cache := clicore.ToolsCache{
		Tools: []clicore.ToolDefinition{
			{
				Name: "focus-window",
				InputSchema: clicore.InputSchema{
					Type: "object",
					Properties: map[string]clicore.ToolProperty{
						"ProjectPath": {Type: "string"},
					},
				},
			},
		},
	}

	handled, code := tryHandleCompletionRequest(
		[]string{"--list-options", "focus-window"},
		cache,
		&stdout,
		&bytes.Buffer{},
	)

	if !handled {
		t.Fatal("completion request was not handled")
	}
	if code != 0 {
		t.Fatalf("exit code mismatch: %d", code)
	}
	if stdout.String() != "\n" {
		t.Fatalf("native command should not use cached tool options: %s", stdout.String())
	}
}

// Tests that completion lists default-enabled boolean arguments as --no-* flags.
func TestCompletionListOptionsUsesNegatedDefaultTrueBooleanFlags(t *testing.T) {
	var stdout bytes.Buffer
	handled, code := tryHandleCompletionRequest(
		[]string{"--list-options", "get-hierarchy"},
		clicore.LoadDefaultTools(),
		&stdout,
		&bytes.Buffer{},
	)

	if !handled {
		t.Fatal("completion request was not handled")
	}
	if code != 0 {
		t.Fatalf("exit code mismatch: %d", code)
	}

	output := stdout.String()
	options := strings.Split(strings.TrimSpace(output), "\n")
	for _, option := range []string{"--no-include-components", "--no-include-inactive"} {
		if !slices.Contains(options, option) {
			t.Fatalf("option %s was not listed: %s", option, output)
		}
	}
	for _, option := range []string{"--include-components", "--include-inactive"} {
		if slices.Contains(options, option) {
			t.Fatalf("default-enabled option %s should not be listed: %s", option, output)
		}
	}
}

func TestCompletionPrintsShellScriptWithoutProject(t *testing.T) {
	var stdout bytes.Buffer
	handled, code := tryHandleCompletionRequest(
		[]string{"completion", "--shell", "bash"},
		clicore.LoadDefaultTools(),
		&stdout,
		&bytes.Buffer{},
	)

	if !handled {
		t.Fatal("completion request was not handled")
	}
	if code != 0 {
		t.Fatalf("exit code mismatch: %d", code)
	}

	output := stdout.String()
	if !strings.Contains(output, "complete -F _uloop_completions uloop") {
		t.Fatalf("bash completion script mismatch: %s", output)
	}
}

func TestCompletionDetectionSkipsRegularToolCommands(t *testing.T) {
	// Verifies that normal tool execution avoids completion cache loading.
	if clicore.ShouldHandleCompletionRequest([]string{clicore.ExecuteDynamicCodeCommandName, "--code", "return 1;"}) {
		t.Fatal("execute-dynamic-code should not enter completion handling")
	}
}

func TestCompletionDetectionHandlesCompletionCommands(t *testing.T) {
	// Verifies that completion-specific commands still load completion metadata.
	for _, args := range [][]string{
		{clicore.CompletionCommand, "--shell", "bash"},
		{clicore.ListCommandsFlag},
		{clicore.ListOptionsFlag, "compile"},
	} {
		if !clicore.ShouldHandleCompletionRequest(args) {
			t.Fatalf("completion request was not detected: %#v", args)
		}
	}
}

// Tests that Git Bash auto-install writes bash completion instead of PowerShell completion.
func TestDetectShellOnWindowsGitBashUsesBash(t *testing.T) {
	shellName := detectShellFromEnvironment("windows", "/usr/bin/bash", "MINGW64")

	if shellName != "bash" {
		t.Fatalf("windows Git Bash shell mismatch: %s", shellName)
	}
}

func TestDetectShellOnWindowsPrefersPwshWhenAvailable(t *testing.T) {
	// Verifies Windows completion install targets PowerShell 7 when it is available.
	shellName := detectShellForPlatform("windows", "", "", func(name string) (string, error) {
		if name == "pwsh" {
			return filepath.Join("bin", "pwsh"), nil
		}
		return "", os.ErrNotExist
	})

	if shellName != "pwsh" {
		t.Fatalf("windows shell mismatch: %s", shellName)
	}
}

// Tests that regular Windows terminals still get the native PowerShell completion default.
func TestDetectShellOnWindowsPowerShellDefaultsToPowerShell(t *testing.T) {
	shellName := detectShellForPlatform("windows", "", "", func(name string) (string, error) {
		if name == "powershell" {
			return filepath.Join("bin", "powershell"), nil
		}
		return "", os.ErrNotExist
	})

	if shellName != "powershell" {
		t.Fatalf("windows default shell mismatch: %s", shellName)
	}
}

func TestCompletionInstallReplacesExistingBlock(t *testing.T) {
	temporaryHome := t.TempDir()
	t.Setenv("HOME", temporaryHome)

	configPath := filepath.Join(temporaryHome, ".zshrc")
	existing := "before\n" + completionStartMarker + "\nstale\n" + completionEndMarker + "\nafter\n"
	if err := os.WriteFile(configPath, []byte(existing), 0o644); err != nil {
		t.Fatalf("failed to seed shell config: %v", err)
	}

	var stdout bytes.Buffer
	handled, code := tryHandleCompletionRequest(
		[]string{"completion", "--shell", "zsh", "--install"},
		clicore.LoadDefaultTools(),
		&stdout,
		&bytes.Buffer{},
	)

	if !handled {
		t.Fatal("completion request was not handled")
	}
	if code != 0 {
		t.Fatalf("exit code mismatch: %d", code)
	}

	content, err := os.ReadFile(configPath)
	if err != nil {
		t.Fatalf("failed to read shell config: %v", err)
	}

	result := string(content)
	if strings.Contains(result, "stale") {
		t.Fatalf("stale completion block was not removed: %s", result)
	}
	if !strings.Contains(result, `eval "$(uloop completion --shell zsh)"`) {
		t.Fatalf("new completion eval line missing: %s", result)
	}
	if !strings.Contains(stdout.String(), "Completion installed") {
		t.Fatalf("install output mismatch: %s", stdout.String())
	}
}

func TestCompletionSupportsPwshProfile(t *testing.T) {
	temporaryHome := t.TempDir()
	t.Setenv("HOME", temporaryHome)

	configPath, err := getShellConfigPath("pwsh")
	if err != nil {
		t.Fatalf("getShellConfigPath failed: %v", err)
	}

	expectedHome := temporaryHome
	if runtime.GOOS == "windows" {
		userHome, userHomeErr := os.UserHomeDir()
		if userHomeErr != nil {
			t.Fatalf("os.UserHomeDir failed: %v", userHomeErr)
		}
		expectedHome = userHome
	}
	expectedPath := getPwshProfilePath(expectedHome, runtime.GOOS)
	if configPath != expectedPath {
		t.Fatalf("pwsh profile path mismatch: %s", configPath)
	}

	script := getCompletionScript("pwsh")
	if !strings.Contains(script, "Register-ArgumentCompleter") {
		t.Fatalf("pwsh completion script mismatch: %s", script)
	}
}

func TestGetPwshProfilePathUsesPlatformSpecificLocation(t *testing.T) {
	home := filepath.Join("home", "user")

	windowsPath := getPwshProfilePath(home, "windows")
	expectedWindowsPath := filepath.Join(home, "Documents", "PowerShell", "Microsoft.PowerShell_profile.ps1")
	if windowsPath != expectedWindowsPath {
		t.Fatalf("windows pwsh profile path mismatch: %s", windowsPath)
	}

	posixPath := getPwshProfilePath(home, "darwin")
	expectedPosixPath := filepath.Join(home, ".config", "powershell", "Microsoft.PowerShell_profile.ps1")
	if posixPath != expectedPosixPath {
		t.Fatalf("posix pwsh profile path mismatch: %s", posixPath)
	}
}

func TestGetHomeDirectoryForShellOnWindowsPowerShellIgnoresHomeOverride(t *testing.T) {
	// Tests that Windows PowerShell profiles use the Windows user profile instead of MSYS-style HOME.
	environmentHomeCalls := 0
	userHomeCalls := 0

	for _, shellName := range []string{"powershell", "pwsh"} {
		home, err := getHomeDirectoryForShell(
			shellName,
			"windows",
			func() (string, error) {
				environmentHomeCalls++
				return "/c/Users/ExampleUser", nil
			},
			func() (string, error) {
				userHomeCalls++
				return `C:\Users\ExampleUser`, nil
			},
		)
		if err != nil {
			t.Fatalf("getHomeDirectoryForShell failed: %v", err)
		}

		if home != `C:\Users\ExampleUser` {
			t.Fatalf("windows %s home mismatch: %s", shellName, home)
		}
	}
	if environmentHomeCalls != 0 {
		t.Fatalf("environment HOME should not be used for Windows PowerShell")
	}
	if userHomeCalls != 2 {
		t.Fatalf("user home resolver call count mismatch: %d", userHomeCalls)
	}
}

func TestGetHomeDirectoryForShellOnWindowsBashNormalizesMsysHome(t *testing.T) {
	// Tests that Windows POSIX shells convert Git Bash HOME to a Win32 path before file writes.
	environmentHomeCalls := 0
	userHomeCalls := 0

	home, err := getHomeDirectoryForShell(
		"bash",
		"windows",
		func() (string, error) {
			environmentHomeCalls++
			return "/c/Users/ExampleUser", nil
		},
		func() (string, error) {
			userHomeCalls++
			return `C:\Users\ExampleUser`, nil
		},
	)
	if err != nil {
		t.Fatalf("getHomeDirectoryForShell failed: %v", err)
	}

	if home != `C:\Users\ExampleUser` {
		t.Fatalf("windows bash home mismatch: %s", home)
	}
	if environmentHomeCalls != 1 {
		t.Fatalf("environment HOME resolver call count mismatch: %d", environmentHomeCalls)
	}
	if userHomeCalls != 0 {
		t.Fatalf("user home should not be used for Windows bash")
	}
}

func TestGetHomeDirectoryForShellOnWindowsZshNormalizesWslHome(t *testing.T) {
	// Tests that Windows POSIX shell HOME values from /mnt/c are safe for Win32 file APIs.
	home, err := getHomeDirectoryForShell(
		"zsh",
		"windows",
		func() (string, error) {
			return "/mnt/c/Users/ExampleUser", nil
		},
		func() (string, error) {
			return `C:\Users\IgnoredUser`, nil
		},
	)
	if err != nil {
		t.Fatalf("getHomeDirectoryForShell failed: %v", err)
	}

	if home != `C:\Users\ExampleUser` {
		t.Fatalf("windows zsh home mismatch: %s", home)
	}
}

func TestGetHomeDirectoryForShellOnWindowsBashNormalizesWslDriveRoot(t *testing.T) {
	// Tests that a WSL drive-root HOME is converted before Win32 file APIs receive it.
	home, err := getHomeDirectoryForShell(
		"bash",
		"windows",
		func() (string, error) {
			return "/mnt/c/", nil
		},
		func() (string, error) {
			return `C:\Users\IgnoredUser`, nil
		},
	)
	if err != nil {
		t.Fatalf("getHomeDirectoryForShell failed: %v", err)
	}

	if home != `C:\` {
		t.Fatalf("windows bash drive-root home mismatch: %s", home)
	}
}
