package update

import "strings"

// ManagedInstallKind identifies the package manager that owns a dispatcher install.
type ManagedInstallKind string

const (
	ManagedInstallNone     ManagedInstallKind = ""
	ManagedInstallHomebrew ManagedInstallKind = "homebrew"
	ManagedInstallWinget   ManagedInstallKind = "winget"
)

// ManagedInstall describes how a package-manager-owned dispatcher must be updated.
type ManagedInstall struct {
	Kind           ManagedInstallKind
	DisplayName    string
	UpgradeCommand string
}

// IsManaged reports whether a package manager owns the dispatcher install.
func (managedInstall ManagedInstall) IsManaged() bool {
	return managedInstall.Kind != ManagedInstallNone
}

// DetectManagedInstall resolves the package-manager update policy for an executable path.
func DetectManagedInstall(executablePath string) ManagedInstall {
	if IsHomebrewManagedPath(executablePath) {
		return ManagedInstall{
			Kind:           ManagedInstallHomebrew,
			DisplayName:    "Homebrew",
			UpgradeCommand: "brew upgrade uloop",
		}
	}
	if IsWingetManagedPath(executablePath) {
		return ManagedInstall{
			Kind:           ManagedInstallWinget,
			DisplayName:    "winget",
			UpgradeCommand: "winget upgrade --id hatayama.uloop",
		}
	}
	return ManagedInstall{}
}

// IsWingetManagedPath reports whether executablePath is under a WinGet Packages or Links directory.
// Why: WinGet supports custom portable package roots, so ownership must key on
// its Packages or Links path segments instead of fixed AppData or Program Files roots.
func IsWingetManagedPath(executablePath string) bool {
	normalizedPath := strings.ReplaceAll(executablePath, "\\", "/")
	segments := strings.Split(normalizedPath, "/")
	for index := 0; index+1 < len(segments); index++ {
		if !strings.EqualFold(segments[index], "WinGet") {
			continue
		}
		managedDirectory := segments[index+1]
		if strings.EqualFold(managedDirectory, "Packages") || strings.EqualFold(managedDirectory, "Links") {
			return true
		}
	}
	return false
}
