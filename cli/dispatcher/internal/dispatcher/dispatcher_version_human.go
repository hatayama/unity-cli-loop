package dispatcher

import (
	"io"
	"os"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"golang.org/x/term"
)

// tryHandleDispatcherHumanVersionRequest answers a plain `-v` / `--version` locally when stdout is a
// terminal, printing the dispatcher version plus one sentence naming the CLI generation that serves
// this directory.
// Why only on a terminal: the shipped V2 Unity package runs `uloop --version` with redirected stdout
// and parses the whole stdout as strict SemVer, so the non-terminal contract must stay byte-identical.
func tryHandleDispatcherHumanVersionRequest(
	remainingArgs []string,
	projectPath string,
	stdout io.Writer,
	deps dispatcherRunDeps,
) (bool, int) {
	if !clicore.IsVersionRequest(remainingArgs) {
		return false, 0
	}
	if !dispatcherStdoutIsTerminal(stdout, deps) {
		return false, 0
	}
	writeDispatcherVersionOutput(stdout, false)
	clicore.WriteLine(stdout, dispatcherVersionContextSentence(projectPath, remainingArgs))
	return true, 0
}

// dispatcherStdoutIsTerminal reports whether human-readable output is appropriate for stdout.
// Why the nil check: dependency literals built without the probe must behave as "not a terminal".
func dispatcherStdoutIsTerminal(stdout io.Writer, deps dispatcherRunDeps) bool {
	if deps.stdoutIsTerminal == nil {
		return false
	}
	return deps.stdoutIsTerminal(stdout)
}

// isTerminalWriter reports whether the writer is an OS file attached to a terminal.
func isTerminalWriter(writer io.Writer) bool {
	file, isFile := writer.(*os.File)
	if !isFile {
		return false
	}
	return term.IsTerminal(int(file.Fd()))
}

// dispatcherVersionContextSentence explains which CLI generation serves the resolved directory.
func dispatcherVersionContextSentence(projectPath string, remainingArgs []string) string {
	projectRoot, err := resolveDispatcherHumanVersionProjectRoot(projectPath, remainingArgs)
	if err != nil {
		return "No Unity project detected here; run inside a Unity project to see which CLI generation serves it."
	}
	v2Project, detectErr := detectV2DispatcherProject(projectRoot)
	if detectErr == nil && v2Project.IsV2 {
		return dispatcherV2VersionContextSentence(v2Project)
	}
	pin, pinErr := loadDispatcherPin(projectRoot)
	if pinErr != nil {
		return "This Unity project has no readable project runner pin yet; " +
			"open it in Unity once to create " + dispatcherProjectPinRelativePath + "."
	}
	return "This Unity project pins uloop project runner " + pin.ProjectRunnerVersion + "."
}

func dispatcherV2VersionContextSentence(v2Project dispatcherV2Project) string {
	if v2Project.PackageVersion == "" {
		return "This Unity project uses the uloop V2 package, but its package version could not be resolved; " +
			"see the error from any other uloop command here."
	}
	return "This Unity project uses the uloop V2 package " + v2Project.PackageVersion +
		", so its commands run through the V2 CLI (" +
		dispatcherV2CLIPackageName + "@" + v2Project.PackageVersion + ")."
}

func resolveDispatcherHumanVersionProjectRoot(projectPath string, remainingArgs []string) (string, error) {
	startPath, err := os.Getwd()
	if err != nil {
		return "", err
	}
	return resolveDispatcherProjectRoot(startPath, projectPath, remainingArgs)
}
