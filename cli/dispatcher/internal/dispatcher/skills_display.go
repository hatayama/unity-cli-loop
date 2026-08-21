package dispatcher

import (
	"io"
	"strings"

	"github.com/hatayama/unity-cli-loop/common/clicore"
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
	if !isV3MigrationSkillSubcommand(command) {
		clicore.WriteLine(stdout, "      --output-dir <path>")
	}
	printSkillTargetFlagLines(stdout, "      ")
	clicore.WriteLine(stdout, "")
	if command == "install" {
		clicore.WriteLine(stdout, "Targets that already contain uloop skills are refreshed automatically,")
		clicore.WriteLine(stdout, "even when their flag is omitted, so previously installed copies never go stale.")
		clicore.WriteLine(stdout, "")
	}
	if !isV3MigrationSkillSubcommand(command) {
		clicore.WriteLine(stdout, "With --output-dir, skills sync flat into <path>/<skill-name> with no target")
		clicore.WriteLine(stdout, "subdirectories; files uloop does not manage there are left untouched.")
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
	printSkillTargetFlagLines(stdout, "  ")
	if !isV3MigrationSkillSubcommand(command) {
		clicore.WriteLine(stdout, "")
		clicore.WriteLine(stdout, "Or pass --output-dir <path> to sync skills into a custom directory.")
	}
}

// printSkillTargetFlagLines prints one --<id> line per target in the shared
// order defined by allSkillTargetIDs, so help output and guidance stay aligned
// with the set of accepted flags.
func printSkillTargetFlagLines(stdout io.Writer, indent string) {
	for _, id := range allSkillTargetIDs {
		clicore.WriteFormat(stdout, "%s--%s\n", indent, id)
	}
}
