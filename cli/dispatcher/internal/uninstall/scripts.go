package uninstall

import "embed"

//go:embed scripts/uninstall_darwin.sh scripts/uninstall_windows_delete.ps1 scripts/uninstall_windows_launch.ps1
var uninstallScriptFiles embed.FS

func uninstallScriptTemplate(name string) string {
	content, err := uninstallScriptFiles.ReadFile(name)
	if err != nil {
		panic(err)
	}
	return string(content)
}
