package dispatcher

import (
	"encoding/json"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

func TestResolveDispatcherV2CLIEntrypointReadsObjectBin(t *testing.T) {
	// Verifies the V2 CLI entrypoint is resolved from the published object-form bin declaration.
	installPath := t.TempDir()
	writeDispatcherV2PackageBin(t, installPath, `{"uloop":"dist/cli.bundle.cjs"}`)

	entrypoint, err := resolveDispatcherV2CLIEntrypoint(installPath)
	if err != nil {
		t.Fatalf("resolve V2 CLI entrypoint: %v", err)
	}
	want := filepath.Join(installPath, "node_modules", dispatcherV2CLIPackageName, "dist", "cli.bundle.cjs")
	if entrypoint != want {
		t.Fatalf("entrypoint = %q, want %q", entrypoint, want)
	}
}

func TestResolveDispatcherV2CLIEntrypointReadsStringBin(t *testing.T) {
	// Verifies the V2 CLI entrypoint also supports a string-form bin declaration.
	installPath := t.TempDir()
	writeDispatcherV2PackageBin(t, installPath, `"dist/cli.bundle.cjs"`)

	entrypoint, err := resolveDispatcherV2CLIEntrypoint(installPath)
	if err != nil {
		t.Fatalf("resolve V2 CLI entrypoint: %v", err)
	}
	want := filepath.Join(installPath, "node_modules", dispatcherV2CLIPackageName, "dist", "cli.bundle.cjs")
	if entrypoint != want {
		t.Fatalf("entrypoint = %q, want %q", entrypoint, want)
	}
}

func TestResolveDispatcherV2NodeReportsMissingNode(t *testing.T) {
	// Verifies a missing Node executable is returned to the caller as an error.
	_, err := resolveDispatcherV2Node(func(string) (string, error) {
		return "", os.ErrNotExist
	})
	if err == nil {
		t.Fatal("expected missing Node error")
	}
}

func writeDispatcherV2PackageBin(t *testing.T, installPath string, bin string) {
	t.Helper()
	packagePath := filepath.Join(installPath, "node_modules", dispatcherV2CLIPackageName, dispatcherPackageJSONFileName)
	if err := os.MkdirAll(filepath.Dir(packagePath), 0o755); err != nil {
		t.Fatalf("create V2 package directory: %v", err)
	}
	content := "{\n  \"bin\": " + bin + "\n}\n"
	if err := os.WriteFile(packagePath, []byte(content), 0o644); err != nil {
		t.Fatalf("write V2 package.json: %v", err)
	}
}

func TestResolveDispatcherV2CLIEntrypointRejectsInvalidPackages(t *testing.T) {
	// Verifies a missing, unparsable, or unusable V2 package.json is reported instead of executing an arbitrary file.
	cases := []struct {
		name        string
		bin         string
		rawContent  string
		wantMessage string
	}{
		{name: "missing package", wantMessage: "open "},
		{name: "unparsable package", rawContent: "{", wantMessage: "parse "},
		{name: "bin is neither string nor object", bin: "5", wantMessage: "package bin must be a string or object"},
		{name: "bin object without uloop", bin: `{"other":"cli.js"}`, wantMessage: "package bin does not define uloop"},
		{name: "bin object with empty uloop", bin: `{"uloop":""}`, wantMessage: "package bin does not define uloop"},
		{name: "absolute entrypoint", bin: dispatcherTestJSONString(t, filepath.Join(t.TempDir(), "cli.js")), wantMessage: "bin entrypoint must be relative"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			installPath := t.TempDir()
			if testCase.bin != "" {
				writeDispatcherV2PackageBin(t, installPath, testCase.bin)
			}
			if testCase.rawContent != "" {
				writeDispatcherTestFile(t, filepath.Join(installPath, "node_modules", dispatcherV2CLIPackageName, dispatcherPackageJSONFileName), testCase.rawContent)
			}

			entrypoint, err := resolveDispatcherV2CLIEntrypoint(installPath)

			if err == nil || !strings.Contains(err.Error(), testCase.wantMessage) {
				t.Fatalf("expected error containing %q, got entrypoint=%q err=%v", testCase.wantMessage, entrypoint, err)
			}
		})
	}
}

func TestDefaultDispatcherV2NodePathFindsNodeOnPath(t *testing.T) {
	// Verifies the default Node lookup resolves `node` from PATH.
	nodePath := writeDispatcherFakeNode(t, "exit 0")

	resolved, err := defaultDispatcherV2NodePath()
	if err != nil {
		t.Fatalf("defaultDispatcherV2NodePath failed: %v", err)
	}
	if resolved != nodePath {
		t.Fatalf("node path mismatch: got %s want %s", resolved, nodePath)
	}
}

// writeDispatcherFakeNode installs a POSIX shell script named node as the only PATH entry.
func writeDispatcherFakeNode(t *testing.T, body string) string {
	t.Helper()
	if runtime.GOOS == "windows" {
		t.Skip("POSIX shell scripts are not executable on Windows.")
	}
	binDirectory := t.TempDir()
	nodePath := filepath.Join(binDirectory, dispatcherNodeCommandName)
	if err := os.WriteFile(nodePath, []byte("#!/bin/sh\n"+body+"\n"), 0o755); err != nil {
		t.Fatalf("failed to write fake node: %v", err)
	}
	t.Setenv("PATH", binDirectory)
	return nodePath
}

func dispatcherTestJSONString(t *testing.T, value string) string {
	t.Helper()
	encoded, err := json.Marshal(value)
	if err != nil {
		t.Fatalf("failed to encode %q: %v", value, err)
	}
	return string(encoded)
}
