package install

import "strings"

func posixInstallArgs(installDir string, targetPath string) []string {
	return []string{
		"-c",
		posixInstallScript(installDir, targetPath),
	}
}

func posixInstallScript(installDir string, targetPath string) string {
	return strings.NewReplacer(
		"'{{INSTALL_DIR}}'", shellQuote(installDir),
		"'{{EXPECTED_ULOOP_PATH}}'", shellQuote(targetPath),
	).Replace(installScriptTemplate("scripts/install_darwin.sh"))
}

func shellQuote(value string) string {
	return "'" + strings.ReplaceAll(value, "'", "'\"'\"'") + "'"
}
