package update

import (
	"path/filepath"
	"strings"
)

// IsHomebrewManagedPath reports whether executablePath is under Homebrew's Cellar.
// Why: brew expands binaries under a Cellar segment for every prefix (/opt/homebrew,
// /usr/local, linuxbrew) and symlinks them from <prefix>/bin, so a resolved path
// containing an exact Cellar path segment is the brew-managed install signal.
func IsHomebrewManagedPath(executablePath string) bool {
	normalized := filepath.ToSlash(executablePath)
	for _, segment := range strings.Split(normalized, "/") {
		if segment == "Cellar" {
			return true
		}
	}
	return false
}
