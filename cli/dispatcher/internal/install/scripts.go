package install

import "embed"

//go:embed scripts/install_darwin.sh scripts/install_windows.ps1
var installScriptFiles embed.FS

func installScriptTemplate(name string) string {
	content, err := installScriptFiles.ReadFile(name)
	if err != nil {
		panic(err)
	}
	return string(content)
}
