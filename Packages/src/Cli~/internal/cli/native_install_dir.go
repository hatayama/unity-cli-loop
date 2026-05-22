package cli

import (
	"os"
	"path/filepath"
	"strings"
)

const (
	nativeInstallDirEnvName    = "ULOOP_INSTALL_DIR"
	nativeLocalAppDataEnvName  = "LOCALAPPDATA"
	nativeWindowsProgramsDir   = "Programs"
	nativeInstallDirectoryName = "uloop"
	nativeInstallBinDirName    = "bin"
)

var (
	getenv            = os.Getenv
	nativeUserHomeDir = os.UserHomeDir
)

func joinNativeInstallPath(goos string, elements ...string) string {
	if goos == "windows" {
		return strings.Join(elements, `\`)
	}
	return filepath.Join(elements...)
}
