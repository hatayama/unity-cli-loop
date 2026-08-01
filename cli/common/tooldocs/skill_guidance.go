package tooldocs

// commandSkillNames maps a command to the agent skill that documents it. A static table rather than
// a name derived from the command: `enable-pause-point` is documented by `uloop-pause-point`, not by
// a `uloop-enable-pause-point` skill that does not exist, and four commands share that one skill.
// A command absent from this table gets no guidance line at all, so custom commands are never
// pointed at a skill nobody installed.
var commandSkillNames = map[string]string{
	"clear-console":        "uloop-clear-console",
	"compile":              "uloop-compile",
	"control-play-mode":    "uloop-control-play-mode",
	"execute-dynamic-code": "uloop-execute-dynamic-code",
	"find-game-objects":    "uloop-find-game-objects",
	"focus-window":         "uloop-focus-window",
	"get-hierarchy":        "uloop-get-hierarchy",
	"get-logs":             "uloop-get-logs",
	"record-input":         "uloop-record-input",
	"replay-input":         "uloop-replay-input",
	"run-tests":            "uloop-run-tests",
	"screenshot":           "uloop-screenshot",
	"set-game-view-size":   "uloop-set-game-view-size",
	"simulate-keyboard":    "uloop-simulate-keyboard",
	"simulate-mouse-input": "uloop-simulate-mouse-input",
	"simulate-mouse-ui":    "uloop-simulate-mouse-ui",
	"hot-reload":           "uloop-hot-reload",

	"launch": "uloop-launch",

	// One skill covers the four pause-point commands and the three watch commands: watch
	// expressions are documented by the pause-point skill's references/watch-expressions.md.
	"enable-pause-point": "uloop-pause-point",
	"clear-pause-point":  "uloop-pause-point",
	"await-pause-point":  "uloop-pause-point",
	"pause-point-status": "uloop-pause-point",
	"enable-watch":       "uloop-pause-point",
	"clear-watch":        "uloop-pause-point",
	"get-watch-values":   "uloop-pause-point",
}

// SkillGuidanceLine returns the closing line of a command's --help output: an instruction to load
// the skill that documents it. `--help` can only list option names and one-line summaries, so the
// workflow rules, response shapes, and failure diagnoses live in the skill; without this line an
// agent that found the command through --help has no way to learn the skill exists.
//
// Phrased as an instruction rather than a cross-reference, because a line that merely mentions a
// document is easy to read past. It names what the skill adds rather than referring to the options
// above, because commands such as focus-window have no options for that phrasing to point at.
func SkillGuidanceLine(command string) (string, bool) {
	skillName, ok := commandSkillNames[command]
	if !ok {
		return "", false
	}
	return "Load the " + skillName +
		" skill for workflow rules and response fields that --help does not cover.", true
}
