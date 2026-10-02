package nativepath

import (
	"errors"
	"os"
	"strings"
	"testing"
)

func TestResolveInstallDirUsesExplicitDirectory(t *testing.T) {
	// Verifies command-line install directories take precedence over environment defaults.
	installDir, err := ResolveInstallDir("windows", `D:\Tools\uloop`, Environment{})
	if err != nil {
		t.Fatalf("ResolveInstallDir failed: %v", err)
	}

	if installDir != `D:\Tools\uloop` {
		t.Fatalf("install dir mismatch: %s", installDir)
	}
}

func TestResolveInstallDirUsesEnvironmentDirectory(t *testing.T) {
	// Verifies environment install directories take precedence over operating system defaults.
	installDir, err := ResolveInstallDir("darwin", "", Environment{
		Getenv: func(name string) string {
			if name == InstallDirEnvName {
				return "/custom/bin"
			}
			return ""
		},
		UserHomeDir: func() (string, error) {
			return "", errors.New("home should not be read when install dir env is set")
		},
	})
	if err != nil {
		t.Fatalf("ResolveInstallDir failed: %v", err)
	}

	if installDir != "/custom/bin" {
		t.Fatalf("install dir mismatch: %s", installDir)
	}
}

func TestResolveInstallDirTrimsEnvironmentDirectory(t *testing.T) {
	// Verifies environment install directories are normalized like explicit cache roots.
	installDir, err := ResolveInstallDir("darwin", "", Environment{
		Getenv: func(name string) string {
			if name == InstallDirEnvName {
				return " /custom/bin "
			}
			return ""
		},
	})
	if err != nil {
		t.Fatalf("ResolveInstallDir failed: %v", err)
	}

	if installDir != "/custom/bin" {
		t.Fatalf("install dir mismatch: %s", installDir)
	}
}

func TestDefaultInstallDirForWindowsUsesLocalAppData(t *testing.T) {
	// Verifies Windows install and uninstall commands share the same package-owned default directory.
	installDir, err := DefaultInstallDir("windows", Environment{
		Getenv: func(name string) string {
			if name == LocalAppDataEnvName {
				return `C:\Users\<USER_NAME>\AppData\Local`
			}
			return ""
		},
	})
	if err != nil {
		t.Fatalf("DefaultInstallDir failed: %v", err)
	}

	expected := `C:\Users\<USER_NAME>\AppData\Local\Programs\uloop\bin`
	if installDir != expected {
		t.Fatalf("install dir mismatch: %s", installDir)
	}
}

func TestDefaultInstallDirForWindowsTrimsLocalAppDataSeparator(t *testing.T) {
	// Verifies Windows defaults do not duplicate separators when environment paths end with a slash.
	installDir, err := DefaultInstallDir("windows", Environment{
		Getenv: func(name string) string {
			if name == LocalAppDataEnvName {
				return `C:\Users\<USER_NAME>\AppData\Local\`
			}
			return ""
		},
	})
	if err != nil {
		t.Fatalf("DefaultInstallDir failed: %v", err)
	}

	expected := `C:\Users\<USER_NAME>\AppData\Local\Programs\uloop\bin`
	if installDir != expected {
		t.Fatalf("install dir mismatch: %s", installDir)
	}
}

func TestDefaultInstallDirForDarwinUsesHome(t *testing.T) {
	// Verifies macOS install and uninstall commands share the same user-local default directory.
	installDir, err := DefaultInstallDir("darwin", Environment{
		UserHomeDir: func() (string, error) {
			return "/Users/<USER_NAME>", nil
		},
	})
	if err != nil {
		t.Fatalf("DefaultInstallDir failed: %v", err)
	}

	expected := "/Users/<USER_NAME>/.local/bin"
	if installDir != expected {
		t.Fatalf("install dir mismatch: %s", installDir)
	}
}

func TestDefaultInstallDirRejectsMissingWindowsLocalAppData(t *testing.T) {
	// Verifies Windows defaults fail fast when the platform root directory is unavailable.
	_, err := DefaultInstallDir("windows", Environment{})
	if err == nil {
		t.Fatal("expected missing LOCALAPPDATA error")
	}
	if !strings.Contains(err.Error(), "LOCALAPPDATA") {
		t.Fatalf("unexpected error: %v", err)
	}
}

func TestDefaultInstallDirForLinuxUsesHome(t *testing.T) {
	// Verifies Linux uses the same user-local default directory as macOS and install.sh.
	installDir, err := DefaultInstallDir("linux", Environment{
		UserHomeDir: func() (string, error) {
			return "/home/tester", nil
		},
	})
	if err != nil {
		t.Fatalf("DefaultInstallDir failed: %v", err)
	}

	if installDir != "/home/tester/.local/bin" {
		t.Fatalf("install dir mismatch: %s", installDir)
	}
}

func TestDefaultInstallDirRejectsUnsupportedOS(t *testing.T) {
	// Verifies install directory resolution reports unsupported platforms before building commands.
	_, err := DefaultInstallDir("freebsd", Environment{})
	if !errors.Is(err, ErrUnsupportedOS) {
		t.Fatalf("expected ErrUnsupportedOS, got %v", err)
	}
}

func TestCacheRootUsesExplicitDirectory(t *testing.T) {
	// Verifies explicit cache roots take precedence over operating system defaults.
	cacheRoot, err := CacheRoot("darwin", Environment{
		Getenv: func(name string) string {
			if name == CacheDirEnvName {
				return " /tmp/uloop-cache "
			}
			return ""
		},
	})
	if err != nil {
		t.Fatalf("CacheRoot failed: %v", err)
	}

	if cacheRoot != "/tmp/uloop-cache" {
		t.Fatalf("cache root mismatch: %s", cacheRoot)
	}
}

func TestCacheRootForWindowsTrimsLocalAppDataSeparator(t *testing.T) {
	// Verifies Windows cache roots do not duplicate separators when LOCALAPPDATA ends with a slash.
	cacheRoot, err := CacheRoot("windows", Environment{
		Getenv: func(name string) string {
			if name == LocalAppDataEnvName {
				return `C:\Users\<USER_NAME>\AppData\Local\`
			}
			return ""
		},
	})
	if err != nil {
		t.Fatalf("CacheRoot failed: %v", err)
	}

	expected := `C:\Users\<USER_NAME>\AppData\Local\uloop`
	if cacheRoot != expected {
		t.Fatalf("cache root mismatch: %s", cacheRoot)
	}
}

func TestCacheRootForLinuxUsesXDGCacheHome(t *testing.T) {
	// Verifies Linux cache resolution follows XDG_CACHE_HOME before falling back to home.
	cacheRoot, err := CacheRoot("linux", Environment{
		Getenv: func(name string) string {
			if name == "XDG_CACHE_HOME" {
				return "/cache"
			}
			return ""
		},
	})
	if err != nil {
		t.Fatalf("CacheRoot failed: %v", err)
	}

	if cacheRoot != "/cache/uloop" {
		t.Fatalf("cache root mismatch: %s", cacheRoot)
	}
}

func TestCommandPathTrimsInstallDirectorySeparators(t *testing.T) {
	// Verifies install and uninstall target paths normalize trailing separators consistently.
	targetPath := CommandPath("windows", `C:\Tools\uloop\`, "uloop", "uloop.exe")

	if targetPath != `C:\Tools\uloop\uloop.exe` {
		t.Fatalf("target path mismatch: %s", targetPath)
	}
}

func TestJoinForWindowsPreservesFirstNonEmptyRoot(t *testing.T) {
	// Verifies empty leading elements do not make an absolute Windows path relative.
	joinedPath := Join("windows", "", `\Server\Share\`, "uloop")

	if joinedPath != `\Server\Share\uloop` {
		t.Fatalf("joined path mismatch: %s", joinedPath)
	}
}

func TestCommandPathPreservesPosixRootDirectory(t *testing.T) {
	// Verifies trimming a POSIX install directory does not turn the filesystem root into an empty path.
	targetPath := CommandPath("darwin", "/", "uloop", "uloop.exe")

	if targetPath != "/uloop" {
		t.Fatalf("target path mismatch: %s", targetPath)
	}
}

func homeEnvironment(home string, homeErr error, values map[string]string) Environment {
	return Environment{
		Getenv:      func(name string) string { return values[name] },
		UserHomeDir: func() (string, error) { return home, homeErr },
	}
}

func TestResolveInstallDirFallsBackToOSDefault(t *testing.T) {
	// Verifies a missing explicit and environment directory falls back to the OS default under home.
	installDir, err := ResolveInstallDir("linux", "", homeEnvironment("/home/<USER_NAME>", nil, nil))
	if err != nil {
		t.Fatalf("ResolveInstallDir failed: %v", err)
	}
	if installDir != "/home/<USER_NAME>/.local/bin" {
		t.Fatalf("install dir mismatch: %s", installDir)
	}
}

func TestHomeBasedDirectoriesReportHomeLookupFailure(t *testing.T) {
	// Verifies every home-based directory returns the home lookup error instead of a guessed path.
	homeErr := errors.New("home lookup failed")
	environment := homeEnvironment("", homeErr, nil)
	if _, err := DefaultInstallDir("darwin", environment); !errors.Is(err, homeErr) {
		t.Fatalf("DefaultInstallDir error = %v, want the home lookup error", err)
	}
	for _, goos := range []string{"darwin", "linux"} {
		if _, err := CacheRoot(goos, environment); !errors.Is(err, homeErr) {
			t.Fatalf("CacheRoot(%s) error = %v, want the home lookup error", goos, err)
		}
	}
}

func TestCacheRootUsesOSDefaults(t *testing.T) {
	// Verifies the cache root defaults to the macOS caches folder, LOCALAPPDATA on Windows, and
	// ~/.cache on Linux when XDG_CACHE_HOME is unset.
	cases := []struct {
		goos   string
		values map[string]string
		want   string
	}{
		{goos: "darwin", want: "/Users/<USER_NAME>/Library/Caches/uloop"},
		{goos: "windows", values: map[string]string{LocalAppDataEnvName: `C:\Users\<USER_NAME>\AppData\Local`}, want: `C:\Users\<USER_NAME>\AppData\Local\uloop`},
		{goos: "linux", want: "/Users/<USER_NAME>/.cache/uloop"},
	}
	for _, testCase := range cases {
		cacheRoot, err := CacheRoot(testCase.goos, homeEnvironment("/Users/<USER_NAME>", nil, testCase.values))
		if err != nil {
			t.Fatalf("CacheRoot(%s) failed: %v", testCase.goos, err)
		}
		if cacheRoot != testCase.want {
			t.Fatalf("CacheRoot(%s) = %q, want %q", testCase.goos, cacheRoot, testCase.want)
		}
	}
}

func TestCacheRootRejectsMissingWindowsLocalAppData(t *testing.T) {
	// Verifies Windows cache resolution fails with its own message when LOCALAPPDATA is missing.
	_, err := CacheRoot("windows", homeEnvironment("/unused", nil, nil))
	if err == nil || err.Error() != "LOCALAPPDATA is required to resolve the uloop cache directory" {
		t.Fatalf("err = %v", err)
	}
}

func TestCommandPathUsesWindowsSeparatorAndCommandName(t *testing.T) {
	// Verifies Windows command paths use a backslash and the Windows command name.
	commandPath := CommandPath("windows", `C:\Tools\uloop\`, "uloop", "uloop.exe")
	if commandPath != `C:\Tools\uloop\uloop.exe` {
		t.Fatalf("command path mismatch: %s", commandPath)
	}
}

func TestEnvironmentWithoutFunctionsUsesProcessDefaults(t *testing.T) {
	// Verifies a zero Environment reads no variables and falls back to the process home directory.
	t.Setenv(InstallDirEnvName, "/ignored/by/zero/environment")
	home, err := os.UserHomeDir()
	if err != nil {
		t.Skipf("process home directory unavailable: %v", err)
	}

	installDir, err := ResolveInstallDir("linux", "", Environment{})
	if err != nil {
		t.Fatalf("ResolveInstallDir failed: %v", err)
	}
	if installDir != Join("linux", home, ".local", "bin") {
		t.Fatalf("install dir = %q, want it under the process home %q", installDir, home)
	}
}

func TestDefaultEnvironmentReadsTheProcessEnvironment(t *testing.T) {
	// Verifies DefaultEnvironment reads variables from the current process.
	t.Setenv(CacheDirEnvName, "/from/process/env")

	cacheRoot, err := CacheRoot("linux", DefaultEnvironment())
	if err != nil || cacheRoot != "/from/process/env" {
		t.Fatalf("cacheRoot=%q err=%v", cacheRoot, err)
	}
}
