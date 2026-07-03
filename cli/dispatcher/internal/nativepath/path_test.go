package nativepath

import (
	"errors"
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

func TestDefaultInstallDirRejectsUnsupportedOS(t *testing.T) {
	// Verifies install directory resolution reports unsupported platforms before building commands.
	_, err := DefaultInstallDir("linux", Environment{})
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

func TestCommandPathPreservesPosixRootDirectory(t *testing.T) {
	// Verifies trimming a POSIX install directory does not turn the filesystem root into an empty path.
	targetPath := CommandPath("darwin", "/", "uloop", "uloop.exe")

	if targetPath != "/uloop" {
		t.Fatalf("target path mismatch: %s", targetPath)
	}
}
