package dispatcher

import (
	"os"
	"path/filepath"
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
