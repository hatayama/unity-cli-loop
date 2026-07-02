package cli

import (
	"io"
	"strings"

	"github.com/hatayama/unity-cli-loop/cli/internal/clicore"
)

func defaultSkillTargets() []skillTarget {
	targets := make([]skillTarget, 0, len(defaultSkillTargetIDs))
	for _, targetID := range defaultSkillTargetIDs {
		targets = append(targets, targetConfigs[targetID])
	}
	return targets
}

func shouldSkipSkillFile(name string) bool {
	return name == ".DS_Store" || strings.HasSuffix(name, ".meta")
}

func isSafeSkillName(name string) bool {
	return name != "" && name != "." && name != ".." &&
		!strings.Contains(name, "/") && !strings.Contains(name, `\`)
}

func skillLocationName(global bool) string {
	if global {
		return "global"
	}
	return "project"
}

func statusIcon(status string) string {
	switch status {
	case "installed":
		return "+"
	case "outdated":
		return "^"
	default:
		return "-"
	}
}

func statusText(status string) string {
	switch status {
	case "installed":
		return "installed"
	case "outdated":
		return "outdated"
	default:
		return "not installed"
	}
}

func printSkillsHelp(stdout io.Writer) {
	clicore.WriteLine(stdout, "Usage:")
	clicore.WriteLine(stdout, "  uloop skills list [options]")
	clicore.WriteLine(stdout, "  uloop skills install [options]")
	clicore.WriteLine(stdout, "  uloop skills uninstall [options]")
	clicore.WriteLine(stdout, "  uloop skills install-v3-migration [options]")
	clicore.WriteLine(stdout, "  uloop skills uninstall-v3-migration [options]")
	clicore.WriteLine(stdout, "")
	printGlobalOptionsHelp(stdout)
}

func printSkillsSubcommandHelp(command string, stdout io.Writer) {
	clicore.WriteLine(stdout, "Usage:")
	clicore.WriteFormat(stdout, "  uloop skills %s [options]\n", command)
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Options:")
	clicore.WriteLine(stdout, "  -g, --global")
	clicore.WriteLine(stdout, "      --flat")
	clicore.WriteLine(stdout, "      --claude")
	clicore.WriteLine(stdout, "      --codex")
	clicore.WriteLine(stdout, "      --cursor")
	clicore.WriteLine(stdout, "      --gemini")
	clicore.WriteLine(stdout, "      --agents")
	clicore.WriteLine(stdout, "      --windsurf")
	clicore.WriteLine(stdout, "      --antigravity")
	clicore.WriteLine(stdout, "")
	if command == "install" {
		clicore.WriteLine(stdout, "Targets that already contain uloop skills are refreshed automatically,")
		clicore.WriteLine(stdout, "even when their flag is omitted, so previously installed copies never go stale.")
		clicore.WriteLine(stdout, "")
	}
	if command == "install-v3-migration" {
		clicore.WriteLine(stdout, "Installs only the temporary V3 CLI invocation migration skill.")
		clicore.WriteLine(stdout, "")
	}
	if command == "uninstall-v3-migration" {
		clicore.WriteLine(stdout, "Removes only the temporary V3 CLI invocation migration skill.")
		clicore.WriteLine(stdout, "")
	}
	printGlobalOptionsHelp(stdout)
}

func printSkillsTargetGuidance(command string, stdout io.Writer) {
	clicore.WriteFormat(stdout, "\nPlease specify at least one target for '%s':\n\n", command)
	clicore.WriteLine(stdout, "Available targets:")
	clicore.WriteLine(stdout, "  --claude")
	clicore.WriteLine(stdout, "  --codex")
	clicore.WriteLine(stdout, "  --cursor")
	clicore.WriteLine(stdout, "  --gemini")
	clicore.WriteLine(stdout, "  --agents")
	clicore.WriteLine(stdout, "  --windsurf")
	clicore.WriteLine(stdout, "  --antigravity")
}
