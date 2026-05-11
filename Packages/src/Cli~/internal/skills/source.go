package skills

import "path/filepath"

const cliOnlySkillRoot = "Packages/src/Cli~/internal/skills/skill-definitions/cli-only"

func CliOnlySourceRoot(projectRoot string) string {
	return filepath.Join(projectRoot, cliOnlySkillRoot)
}
