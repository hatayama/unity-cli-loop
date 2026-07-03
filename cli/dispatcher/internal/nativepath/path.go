package nativepath

import (
	"errors"
	"os"
	"path"
	"strings"
)

const (
	InstallDirEnvName       = "ULOOP_INSTALL_DIR"
	CacheDirEnvName         = "ULOOP_CACHE_DIR"
	LocalAppDataEnvName     = "LOCALAPPDATA"
	WindowsProgramsDirName  = "Programs"
	ProductDirectoryName    = "uloop"
	InstallBinDirectoryName = "bin"
)

// ErrUnsupportedOS marks platforms without native install directory conventions.
var ErrUnsupportedOS = errors.New("unsupported operating system")

// Environment supplies process-specific path inputs without package-level test seams.
type Environment struct {
	Getenv      func(string) string
	UserHomeDir func() (string, error)
}

// DefaultEnvironment reads path inputs from the current process.
func DefaultEnvironment() Environment {
	return Environment{
		Getenv:      os.Getenv,
		UserHomeDir: os.UserHomeDir,
	}
}

// ResolveInstallDir returns the explicit, environment, or OS default launcher directory.
func ResolveInstallDir(goos string, explicitInstallDir string, environment Environment) (string, error) {
	if explicitInstallDir != "" {
		return explicitInstallDir, nil
	}
	if installDir := strings.TrimSpace(getEnvironmentValue(environment, InstallDirEnvName)); installDir != "" {
		return installDir, nil
	}
	return DefaultInstallDir(goos, environment)
}

// DefaultInstallDir returns the OS default package-owned launcher directory.
func DefaultInstallDir(goos string, environment Environment) (string, error) {
	switch goos {
	case "darwin":
		home, err := userHomeDir(environment)
		if err != nil {
			return "", err
		}
		return Join(goos, home, ".local", InstallBinDirectoryName), nil
	case "windows":
		localAppData := getEnvironmentValue(environment, LocalAppDataEnvName)
		if localAppData == "" {
			return "", errors.New("LOCALAPPDATA is required to resolve the uloop install directory")
		}
		return Join(goos, localAppData, WindowsProgramsDirName, ProductDirectoryName, InstallBinDirectoryName), nil
	default:
		return "", ErrUnsupportedOS
	}
}

// CacheRoot returns the package-owned cache root used for downloaded runners and update state.
func CacheRoot(goos string, environment Environment) (string, error) {
	if explicitCacheRoot := strings.TrimSpace(getEnvironmentValue(environment, CacheDirEnvName)); explicitCacheRoot != "" {
		return explicitCacheRoot, nil
	}

	switch goos {
	case "darwin":
		home, err := userHomeDir(environment)
		if err != nil {
			return "", err
		}
		return Join(goos, home, "Library", "Caches", ProductDirectoryName), nil
	case "windows":
		localAppData := getEnvironmentValue(environment, LocalAppDataEnvName)
		if localAppData == "" {
			return "", errors.New("LOCALAPPDATA is required to resolve the uloop cache directory")
		}
		return Join(goos, localAppData, ProductDirectoryName), nil
	default:
		if xdgCacheHome := strings.TrimSpace(getEnvironmentValue(environment, "XDG_CACHE_HOME")); xdgCacheHome != "" {
			return Join(goos, xdgCacheHome, ProductDirectoryName), nil
		}
		home, err := userHomeDir(environment)
		if err != nil {
			return "", err
		}
		return Join(goos, home, ".cache", ProductDirectoryName), nil
	}
}

// CommandPath joins a launcher command name to an install directory using target OS separators.
func CommandPath(goos string, installDir string, posixCommandName string, windowsCommandName string) string {
	trimmedInstallDir := TrimInstallDir(goos, installDir)
	if goos == "windows" {
		return trimmedInstallDir + `\` + windowsCommandName
	}
	return path.Join(trimmedInstallDir, posixCommandName)
}

// TrimInstallDir removes trailing separators while preserving the POSIX filesystem root.
func TrimInstallDir(goos string, installDir string) string {
	if goos == "windows" {
		return strings.TrimRight(installDir, `\/`)
	}

	trimmedInstallDir := strings.TrimRight(installDir, `/`)
	if trimmedInstallDir == "" {
		return `/`
	}
	return trimmedInstallDir
}

// Join combines path elements with the separator conventions of the target OS.
func Join(goos string, elements ...string) string {
	if goos == "windows" {
		return joinWindows(elements...)
	}
	return path.Join(elements...)
}

func joinWindows(elements ...string) string {
	normalizedElements := make([]string, 0, len(elements))
	for _, element := range elements {
		if element == "" {
			continue
		}
		if len(normalizedElements) == 0 {
			normalizedElements = append(normalizedElements, trimWindowsPathSuffix(element))
			continue
		}
		normalizedElement := strings.Trim(element, `\/`)
		if normalizedElement != "" {
			normalizedElements = append(normalizedElements, normalizedElement)
		}
	}
	return strings.Join(normalizedElements, `\`)
}

func trimWindowsPathSuffix(element string) string {
	trimmedElement := strings.TrimRight(element, `\/`)
	if trimmedElement != "" {
		return trimmedElement
	}
	return strings.TrimRight(element, `/`)
}

func getEnvironmentValue(environment Environment, name string) string {
	if environment.Getenv == nil {
		return ""
	}
	return environment.Getenv(name)
}

func userHomeDir(environment Environment) (string, error) {
	if environment.UserHomeDir == nil {
		return os.UserHomeDir()
	}
	return environment.UserHomeDir()
}
