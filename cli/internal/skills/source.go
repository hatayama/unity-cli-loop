package skills

import "path/filepath"

const cliOnlySkillRoot = "Packages/src/Editor/CliOnlyTools~"

func CliOnlySourceRoot(projectRoot string) string {
	return filepath.Join(projectRoot, cliOnlySkillRoot)
}
