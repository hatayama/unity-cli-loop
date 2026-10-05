package dispatcher

import (
	"encoding/json"
	"io"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/project"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/projectverify"
)

// tryHandleVerifyProjectRequest answers `uloop verify-project` inside the dispatcher process: the
// check reads files only, so it must work while Unity is closed and in projects without the package.
func tryHandleVerifyProjectRequest(
	args []string,
	startPath string,
	globalProjectPath string,
	stdout io.Writer,
	stderr io.Writer,
) (bool, int) {
	if len(args) == 0 || args[0] != clicore.VerifyProjectCommandName {
		return false, 0
	}
	if clicore.ContainsHelpRequest(args[1:]) {
		printVerifyProjectHelp(stdout)
		return true, 0
	}
	if len(args) > 1 {
		clierrors.WriteClassifiedError(stderr, unknownVerifyProjectOptionError(args[1]),
			clierrors.ErrorContext{Command: clicore.VerifyProjectCommandName})
		return true, 1
	}

	return true, runVerifyProject(startPath, globalProjectPath, stdout, stderr)
}

// runVerifyProject checks the resolved project and prints the report. A report with findings
// still goes to stdout, but the exit code is 1 so scripts and agents can gate on it.
func runVerifyProject(startPath string, globalProjectPath string, stdout io.Writer, stderr io.Writer) int {
	projectRoot, err := resolveVerifyProjectRoot(startPath, globalProjectPath)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{Command: clicore.VerifyProjectCommandName})
		return 1
	}

	report, err := projectverify.Run(projectRoot)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			Command: clicore.VerifyProjectCommandName, ProjectRoot: projectRoot,
		})
		return 1
	}
	encoded, err := json.Marshal(report)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			Command: clicore.VerifyProjectCommandName, ProjectRoot: projectRoot,
		})
		return 1
	}
	clicore.WriteJSON(stdout, encoded)

	if report.FindingCount > 0 {
		return 1
	}
	return 0
}

// resolveVerifyProjectRoot prefers the project the working directory is in over a Unity-shaped
// folder below it: the folder being checked is the one the user stands in, and test fixtures
// shaped like projects often sit inside it.
func resolveVerifyProjectRoot(startPath string, explicitProjectPath string) (string, error) {
	if explicitProjectPath != "" {
		return project.ResolveExplicitProjectRoot(explicitProjectPath)
	}
	return project.FindUnityProjectRootPreferringParents(startPath, defaultProjectSearchDepth)
}

// unknownVerifyProjectOptionError rejects an argument verify-project has no meaning for.
func unknownVerifyProjectOptionError(arg string) error {
	return &clierrors.ArgumentError{
		Message:     "Unknown verify-project option: " + arg,
		Option:      arg,
		Command:     clicore.VerifyProjectCommandName,
		NextActions: []string{"Run `uloop verify-project --help` to inspect supported options."},
	}
}

// printVerifyProjectHelp documents the command and its exit codes.
func printVerifyProjectHelp(stdout io.Writer) {
	clicore.WriteLine(stdout, "Usage:")
	clicore.WriteLine(stdout, "  uloop verify-project")
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Checks this Unity project's files without starting Unity: missing or orphaned .meta files,")
	clicore.WriteLine(stdout, "invalid or duplicate GUIDs, leftover merge conflict markers, and a broken Packages/manifest.json.")
	clicore.WriteLine(stdout, "Exits 0 when nothing is found, and 1 when it finds problems or cannot run.")
	clicore.WriteLine(stdout, "")
	printGlobalOptionsHelp(stdout)
	printSkillGuidanceHelp(clicore.VerifyProjectCommandName, stdout)
}
