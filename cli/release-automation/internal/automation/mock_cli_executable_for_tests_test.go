package automation

import (
	"encoding/json"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"sort"
	"testing"
)

// This file provides the Windows counterpart of the POSIX shell mock scripts
// installed as fake `git`/`gh` executables. Why: Windows LookPath only
// resolves files with executable extensions, so an extensionless shell script
// on PATH is silently skipped and the real git/gh would run instead; cmd batch
// shims are no alternative because cmd mangles arguments such as
// `tag^{commit}` and embedded quotes. TestMain therefore builds
// testdata/mockcli once per test run, and installMockCliExecutable copies it
// next to a JSON config that selects which shell mock to emulate.

// mockCliExecutablePath is the shared mockcli build TestMain produces on
// Windows; empty on other platforms.
var mockCliExecutablePath string

// mockCliExecutableConfig mirrors mockCliConfig in testdata/mockcli/main.go.
type mockCliExecutableConfig struct {
	Mode        string                       `json:"mode"`
	RefResolves bool                         `json:"refResolves,omitempty"`
	Paths       map[string]mockCliPathConfig `json:"paths,omitempty"`
	PathOrder   []string                     `json:"pathOrder,omitempty"`
}

// mockCliPathConfig mirrors mockCliPathConfig in testdata/mockcli/main.go.
type mockCliPathConfig struct {
	Exists          bool   `json:"exists"`
	ShowOK          bool   `json:"showOK"`
	ShowContent     string `json:"showContent,omitempty"`
	ShowContentPath string `json:"showContentPath,omitempty"`
	ShowStderr      string `json:"showStderr,omitempty"`
	ProbeSleeps     bool   `json:"probeSleeps,omitempty"`
}

func TestMain(m *testing.M) {
	os.Exit(runTestMain(m))
}

func runTestMain(m *testing.M) int {
	if runtime.GOOS == "windows" {
		buildDir, err := os.MkdirTemp("", "uloop-mockcli-")
		if err != nil {
			fmt.Fprintf(os.Stderr, "failed to create mockcli build directory: %v\n", err)
			return 1
		}
		defer func() {
			_ = os.RemoveAll(buildDir)
		}()

		executablePath := filepath.Join(buildDir, "mockcli.exe")
		command := exec.Command("go", "build", "-o", executablePath, "./testdata/mockcli")
		if output, err := command.CombinedOutput(); err != nil {
			fmt.Fprintf(os.Stderr, "failed to build mockcli: %v\n%s", err, output)
			return 1
		}
		mockCliExecutablePath = executablePath
	}
	return m.Run()
}

// installMockCliExecutable installs the prebuilt mockcli binary as
// <scriptPath>.exe with its config at <scriptPath>.mockconfig.json. Only used
// on Windows; scriptPath is the extensionless path the POSIX shell mock would
// occupy (e.g. .../bin/git), so PATH lookups resolve the same command name.
func installMockCliExecutable(t *testing.T, scriptPath string, config mockCliExecutableConfig) {
	t.Helper()

	if mockCliExecutablePath == "" {
		t.Fatal("mockcli executable was not built; TestMain only builds it on Windows")
	}

	copyMockCliExecutable(t, scriptPath+".exe")

	configContent, err := json.Marshal(config)
	if err != nil {
		t.Fatalf("failed to encode mockcli config: %v", err)
	}
	writeFile(t, scriptPath+".mockconfig.json", string(configContent))
}

func copyMockCliExecutable(t *testing.T, destinationPath string) {
	t.Helper()

	// A hard link avoids copying the binary for every test; both paths live in
	// the same temp volume. Fall back to a byte copy when linking fails.
	if err := os.Link(mockCliExecutablePath, destinationPath); err == nil {
		return
	}
	source, err := os.Open(mockCliExecutablePath)
	if err != nil {
		t.Fatalf("failed to open mockcli executable: %v", err)
	}
	defer func() {
		_ = source.Close()
	}()
	destination, err := os.OpenFile(destinationPath, os.O_CREATE|os.O_WRONLY|os.O_TRUNC, 0o755)
	if err != nil {
		t.Fatalf("failed to create mock executable: %v", err)
	}
	defer func() {
		_ = destination.Close()
	}()
	if _, err := io.Copy(destination, source); err != nil {
		t.Fatalf("failed to copy mock executable: %v", err)
	}
}

// existenceMockCliConfig converts the shared existence fixture into the
// mockcli config, preserving the sorted case-label order the generated shell
// script uses.
func existenceMockCliConfig(fixture mockGitExistenceFixture) mockCliExecutableConfig {
	keys := make([]string, 0, len(fixture.paths))
	for key := range fixture.paths {
		keys = append(keys, key)
	}
	sort.Strings(keys)

	paths := make(map[string]mockCliPathConfig, len(fixture.paths))
	for key, behavior := range fixture.paths {
		paths[key] = mockCliPathConfig{
			Exists:          behavior.exists,
			ShowOK:          behavior.showOK,
			ShowContent:     behavior.showContent,
			ShowContentPath: behavior.showContentPath,
			ShowStderr:      behavior.showStderr,
			ProbeSleeps:     behavior.probeSleeps,
		}
	}

	return mockCliExecutableConfig{
		Mode:        "existence",
		RefResolves: fixture.refResolves,
		Paths:       paths,
		PathOrder:   keys,
	}
}
