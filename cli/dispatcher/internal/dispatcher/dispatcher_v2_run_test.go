package dispatcher

import (
	"bytes"
	"context"
	"errors"
	"io"
	"path/filepath"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
)

func TestDispatcherV2ModeNoticeReportsDelegatedPackageAndVersion(t *testing.T) {
	// Verifies the notice names the delegated V2 CLI package, its version, and the dispatcher version behind it.
	notice := dispatcherV2ModeNotice("2.2.0")

	want := "uloop: executing in V2 mode (" + dispatcherV2CLIPackageName + "@2.2.0) via uloop dispatcher " + dispatcherVersion + "\n"
	if notice != want {
		t.Fatalf("notice = %q, want %q", notice, want)
	}
}

// writeCachedDispatcherV2CLI places an installed V2 CLI package in the cache so no npm run is needed.
func writeCachedDispatcherV2CLI(t *testing.T, cacheRoot string, version string, bin string) string {
	t.Helper()
	installPath := dispatcherV2InstallPath(cacheRoot, version)
	content := `{"version":"` + version + `"`
	if bin != "" {
		content += `,"bin":` + bin
	}
	writeDispatcherTestFile(t, filepath.Join(installPath, "node_modules", dispatcherV2CLIPackageName, dispatcherPackageJSONFileName), content+"}")
	return installPath
}

func TestTryRunDetectedDispatcherV2ProjectKeepsLaunchNative(t *testing.T) {
	// Verifies launch is never delegated to the V2 CLI, even inside a V2 project.
	deps := defaultDispatcherRunDeps()
	deps.runV2CLI = func(context.Context, string, []string, io.Writer, io.Writer) (int, error) {
		t.Fatal("launch must not be delegated to the V2 CLI")
		return 0, nil
	}

	handled, code := tryRunDetectedDispatcherV2Project(context.Background(), t.TempDir(), []string{clicore.LaunchCommandName}, io.Discard, io.Discard, deps)

	if handled || code != 0 {
		t.Fatalf("result mismatch: handled=%t code=%d", handled, code)
	}
}

func TestRunDispatcherV2CLIExecutesCachedEntrypointWithNode(t *testing.T) {
	// Verifies the cached V2 CLI entrypoint runs through node with the original args, after the V2 mode notice.
	cacheRoot := t.TempDir()
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	installPath := writeCachedDispatcherV2CLI(t, cacheRoot, "2.2.0", `{"uloop":"dist/cli.js"}`)
	writeDispatcherFakeNode(t, `echo "node:$*"; exit 4`)

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code, err := runDispatcherV2CLI(context.Background(), "2.2.0", []string{"compile", "--force-recompile"}, &stdout, &stderr)
	if err != nil {
		t.Fatalf("runDispatcherV2CLI failed: %v", err)
	}
	if code != 4 {
		t.Fatalf("exit code mismatch: %d", code)
	}
	entrypoint := filepath.Join(installPath, "node_modules", dispatcherV2CLIPackageName, "dist", "cli.js")
	if stdout.String() != "node:"+entrypoint+" compile --force-recompile\n" {
		t.Fatalf("stdout mismatch: %q", stdout.String())
	}
	if stderr.String() != dispatcherV2ModeNotice("2.2.0") {
		t.Fatalf("stderr mismatch: %q", stderr.String())
	}
}

func TestRunDispatcherV2CLIReturnsZeroOnSuccess(t *testing.T) {
	// Verifies a successful V2 CLI run reports exit code 0 without an error.
	cacheRoot := t.TempDir()
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	writeCachedDispatcherV2CLI(t, cacheRoot, "2.2.0", `"cli.js"`)
	writeDispatcherFakeNode(t, "exit 0")

	code, err := runDispatcherV2CLI(context.Background(), "2.2.0", nil, io.Discard, io.Discard)

	if err != nil || code != 0 {
		t.Fatalf("result mismatch: code=%d err=%v", code, err)
	}
}

func TestRunDispatcherV2CLIReportsNodeStartFailure(t *testing.T) {
	// Verifies a node executable that cannot start is returned as an error rather than an exit code.
	cacheRoot := t.TempDir()
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	writeCachedDispatcherV2CLI(t, cacheRoot, "2.2.0", `"cli.js"`)
	nodePath := writeDispatcherFakeNode(t, "exit 0")
	writeDispatcherExecutable(t, nodePath, "#!/nonexistent-interpreter\n")

	_, err := runDispatcherV2CLI(context.Background(), "2.2.0", nil, io.Discard, io.Discard)

	if err == nil {
		t.Fatal("expected a node start failure")
	}
}

func TestRunDispatcherV2CLIReportsSetupFailures(t *testing.T) {
	// Verifies install, entrypoint, and Node lookup failures stop before anything is executed.
	cases := []struct {
		name        string
		setup       func(t *testing.T, cacheRoot string)
		wantMessage string
	}{
		{
			name: "cache directory cannot be created",
			setup: func(t *testing.T, cacheRoot string) {
				writeDispatcherTestFile(t, filepath.Join(cacheRoot, dispatcherV2CacheDirectoryName), "not a directory")
				writeDispatcherFakeNode(t, "exit 0")
			},
			wantMessage: "not a directory",
		},
		{
			name: "package declares no entrypoint",
			setup: func(t *testing.T, cacheRoot string) {
				writeCachedDispatcherV2CLI(t, cacheRoot, "2.2.0", "")
				writeDispatcherFakeNode(t, "exit 0")
			},
			wantMessage: "package bin must be a string or object",
		},
		{
			name: "node is missing",
			setup: func(t *testing.T, cacheRoot string) {
				writeCachedDispatcherV2CLI(t, cacheRoot, "2.2.0", `"cli.js"`)
				t.Setenv("PATH", t.TempDir())
			},
			wantMessage: "executable file not found",
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			cacheRoot := t.TempDir()
			t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
			testCase.setup(t, cacheRoot)

			var stderr bytes.Buffer
			_, err := runDispatcherV2CLI(context.Background(), "2.2.0", nil, io.Discard, &stderr)

			if err == nil || !strings.Contains(err.Error(), testCase.wantMessage) {
				t.Fatalf("expected error containing %q, got %v", testCase.wantMessage, err)
			}
			if stderr.Len() != 0 {
				t.Fatalf("no V2 mode notice may be written before setup succeeds: %q", stderr.String())
			}
		})
	}
}

func TestRunDispatcherV2CLIReportsMissingCacheRoot(t *testing.T) {
	// Verifies an unresolvable cache root fails before installing the V2 CLI.
	unsetDispatcherCacheRoot(t)

	if _, err := runDispatcherV2CLI(context.Background(), "2.2.0", nil, io.Discard, io.Discard); err == nil {
		t.Fatal("expected a cache root resolution error")
	}
}

type failingDispatcherWriter struct{}

func (failingDispatcherWriter) Write([]byte) (int, error) {
	return 0, errors.New("stderr closed")
}

func TestRunDispatcherV2CLIReportsNoticeWriteFailure(t *testing.T) {
	// Verifies a V2 run stops when the delegation notice cannot be written, instead of running the CLI silently.
	cacheRoot := t.TempDir()
	t.Setenv(nativepath.CacheDirEnvName, cacheRoot)
	writeCachedDispatcherV2CLI(t, cacheRoot, "2.2.0", `"cli.js"`)
	writeDispatcherFakeNode(t, `echo ran`)

	var stdout bytes.Buffer
	_, err := runDispatcherV2CLI(context.Background(), "2.2.0", nil, &stdout, failingDispatcherWriter{})

	if err == nil || !strings.Contains(err.Error(), "stderr closed") {
		t.Fatalf("expected the notice write error, got %v", err)
	}
	if stdout.Len() != 0 {
		t.Fatalf("the V2 CLI must not run after the notice failed: %q", stdout.String())
	}
}
