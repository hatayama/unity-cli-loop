package compilecheck

import (
	"bytes"
	"os"
	"path/filepath"
)

// referenceSurfaceUnchanged reports whether compiling this unit produced the very reference assembly
// the last Unity build produced for it, which means the change it carries does not reach its public
// surface: every assembly that references it would compile against exactly the same API.
// Why the comparison is against Unity's own artifact rather than this check's previous output: the
// dependents were last compiled against Unity's, so only that one answers whether they would see
// anything new. Comparing against the previous run would only say the surface has not moved since
// that run, and would hide an error a dependent has been carrying all along.
// Why anything unreadable counts as changed: an assembly that failed to compile writes no reference
// assembly at all, and compiling a dependent that did not need it costs time, while skipping one
// that did costs a missed error.
func referenceSurfaceUnchanged(projectRoot string, plan BuildPlan, unit CompileUnit) bool {
	if unit.Assembly.RefOutputPath == "" {
		return false
	}

	produced, err := os.ReadFile(
		filepath.Join(projectRoot, plan.OutputDir, filepath.Base(unit.Assembly.RefOutputPath)))
	if err != nil {
		return false
	}
	fromUnityBuild, err := os.ReadFile(filepath.Join(projectRoot, unit.Assembly.RefOutputPath))
	if err != nil {
		return false
	}

	return bytes.Equal(produced, fromUnityBuild)
}
