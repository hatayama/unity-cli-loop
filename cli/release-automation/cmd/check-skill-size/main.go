// check-skill-size fails when any SKILL.md is larger than the Codex prompt
// cap (8,000 bytes for the whole file), which would silently truncate the
// skill for Codex agents.
package main

import (
	"flag"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	root := flag.String("root", ".", "repository root to scan")
	maxBytes := flag.Int("max-bytes", automation.MaxSkillFileBytes, "maximum allowed SKILL.md size in bytes")
	flag.Parse()

	os.Exit(automation.RunSkillSizeCheck(os.Stdout, os.Stderr, automation.SkillSizeCheckOptions{
		Root:     *root,
		MaxBytes: *maxBytes,
	}))
}
