package compilecheck

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
)

const (
	beeLogMessageNodeResult     = "noderesult"
	beeCompilerAnnotationPrefix = "Csc "
)

// beeNodeResult is one finished Bee node as the last build's log recorded it.
// Why the exit code is a pointer: Bee writes null for a node it never ran to completion and leaves
// the field out entirely for some others, and reading either of those as the zero value would call
// a node that never reported anything a success.
type beeNodeResult struct {
	Msg        string `json:"msg"`
	Annotation string `json:"annotation"`
	ExitCode   *int   `json:"exitcode"`
	OutputFile string `json:"outputfile"`
}

// ReadFailedUnityCompiles names the assemblies whose compiler step failed in the last Unity build.
// Why the Bee log rather than the artifact timestamps: with deferred dag verification Bee can run
// the stale dag's compiler step successfully, refresh the assembly's timestamp, and only then
// rebuild the dag and fail on the sources that actually changed. The timestamp then says "up to
// date" for an assembly Unity never managed to compile from its current sources.
// Why nothing here is an error: the log is a diagnostic Bee leaves behind, not a contract. A run
// that cannot read it selects assemblies the way it did before this check existed.
func ReadFailedUnityCompiles(projectRoot string, dagDir string) map[string]bool {
	failed := map[string]bool{}

	file, err := os.Open(
		filepath.Join(projectRoot, libraryDirectoryName, beeDirectoryName, tundraLogFileName))
	if err != nil {
		return failed
	}
	defer func() { _ = file.Close() }()

	// Why the log is streamed as concatenated values rather than read line by line: a node that
	// printed a lot carries its whole output on one line, which runs to tens of kilobytes in a real
	// project, and a line reader with a fixed buffer stops at the first of them.
	decoder := json.NewDecoder(file)
	cleanDagDir := filepath.Clean(dagDir)
	for {
		var node beeNodeResult
		// Why the end of the log and a malformed value end the read the same way: once the decoder
		// has lost the structure it cannot say where the next value begins, so everything after it
		// would be read out of a stream that no longer lines up. What was read before it still
		// describes nodes Bee really ran.
		if decodeErr := decoder.Decode(&node); decodeErr != nil {
			return failed
		}
		if name, isFailedCompile := failedCompileAssembly(node, cleanDagDir); isFailedCompile {
			failed[name] = true
		}
	}
}

// failedCompileAssembly reads the assembly a node failed to compile, when that is what it is.
func failedCompileAssembly(node beeNodeResult, cleanDagDir string) (string, bool) {
	if node.Msg != beeLogMessageNodeResult ||
		!strings.HasPrefix(node.Annotation, beeCompilerAnnotationPrefix) {
		return "", false
	}
	if node.ExitCode == nil || *node.ExitCode == 0 {
		return "", false
	}

	// Why the dag directory has to match: switching between the debug and the release dag leaves the
	// other one's failures in the log, and they describe a build this run is not replaying.
	outputPath := filepath.FromSlash(node.OutputFile)
	if filepath.Clean(filepath.Dir(outputPath)) != cleanDagDir {
		return "", false
	}

	return strings.TrimSuffix(filepath.Base(outputPath), assemblyExtension), true
}
