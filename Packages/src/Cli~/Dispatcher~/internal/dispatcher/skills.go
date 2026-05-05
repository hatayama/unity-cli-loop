package dispatcher

import "io"

const skillsCommandName = "skills"

func isSkillsCommand(args []string) bool {
	return len(args) > 0 && args[0] == skillsCommandName
}

func isSkillsHelpRequest(args []string) bool {
	return isSkillsCommand(args) && (len(args) == 1 || isHelpRequest(args[1:]))
}

func printSkillsHelp(stdout io.Writer) {
	writeLine(stdout, "Usage:")
	writeLine(stdout, "  uloop skills list [options]")
	writeLine(stdout, "  uloop skills install [options]")
	writeLine(stdout, "  uloop skills uninstall [options]")
	writeLine(stdout, "")
	writeLine(stdout, "Options:")
	writeLine(stdout, "  --claude        Target Claude Code skills")
	writeLine(stdout, "  --codex         Target Codex skills")
	writeLine(stdout, "  --cursor        Target Cursor rules")
	writeLine(stdout, "  --gemini        Target Gemini CLI extensions")
	writeLine(stdout, "  --agents        Target AGENTS.md instructions")
	writeLine(stdout, "  --windsurf      Target Windsurf rules")
	writeLine(stdout, "  --antigravity   Target Antigravity rules")
	writeLine(stdout, "  -g, --global    Install to the user-level location")
	writeLine(stdout, "  --flat          Install directly under the target skills directory")
}
