package cli

import (
	"bytes"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

func TestCompletionListCommandsIncludesNativeCommandsAndDefaultTools(t *testing.T) {
	var stdout bytes.Buffer
	handled, code := tryHandleCompletionRequest(
		[]string{"--list-commands"},
		loadDefaultTools(),
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
	for _, command := range []string{"completion", "focus-window", "sync"} {
		if !strings.Contains(output, command) {
			t.Fatalf("command %s was not listed: %s", command, output)
		}
	}
}

func TestCompletionListOptionsUsesToolSchema(t *testing.T) {
	var stdout bytes.Buffer
	handled, code := tryHandleCompletionRequest(
		[]string{"--list-options", "compile"},
		loadDefaultTools(),
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
	for _, option := range []string{"--force-recompile", "--wait-for-domain-reload"} {
		if !strings.Contains(output, option) {
			t.Fatalf("option %s was not listed: %s", option, output)
		}
	}
}

// Tests that completion lists default-enabled boolean arguments as --no-* flags.
func TestCompletionListOptionsUsesNegatedDefaultTrueBooleanFlags(t *testing.T) {
	var stdout bytes.Buffer
	handled, code := tryHandleCompletionRequest(
		[]string{"--list-options", "get-hierarchy"},
		loadDefaultTools(),
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
		if !containsString(options, option) {
			t.Fatalf("option %s was not listed: %s", option, output)
		}
	}
	for _, option := range []string{"--include-components", "--include-inactive"} {
		if containsString(options, option) {
			t.Fatalf("default-enabled option %s should not be listed: %s", option, output)
		}
	}
}

func TestCompletionPrintsShellScriptWithoutProject(t *testing.T) {
	var stdout bytes.Buffer
	handled, code := tryHandleCompletionRequest(
		[]string{"completion", "--shell", "bash"},
		loadDefaultTools(),
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

func containsString(values []string, expected string) bool {
	for _, value := range values {
		if value == expected {
			return true
		}
	}
	return false
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
		loadDefaultTools(),
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

	expectedPath := getPwshProfilePath(temporaryHome, runtime.GOOS)
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
