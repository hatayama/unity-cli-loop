package cli

import (
	"io"
	"strings"
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
	writeLine(stdout, "Usage:")
	writeLine(stdout, "  uloop skills list [options]")
	writeLine(stdout, "  uloop skills install [options]")
	writeLine(stdout, "  uloop skills uninstall [options]")
}

func printSkillsTargetGuidance(command string, stdout io.Writer) {
	writeFormat(stdout, "\nPlease specify at least one target for '%s':\n\n", command)
	writeLine(stdout, "Available targets:")
	writeLine(stdout, "  --claude")
	writeLine(stdout, "  --codex")
	writeLine(stdout, "  --cursor")
	writeLine(stdout, "  --gemini")
	writeLine(stdout, "  --agents")
	writeLine(stdout, "  --windsurf")
	writeLine(stdout, "  --antigravity")
}
