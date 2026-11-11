package unityprocess

import "strings"

// splitWindowsCommandLine splits a Windows command line into arguments the way programs read it: whitespace
// separates them outside double quotes, and backslashes only escape when they come before a double quote.
// Why not parse quotes alone: Go and Unity Hub quote a path ending in a backslash as "C:\dir\\", so reading
// that backslash literally leaves the quote open and swallows every flag after the path.
func splitWindowsCommandLine(commandLine string) []string {
	var args []string
	var current strings.Builder
	insideQuotes := false
	// Why tracked apart from the text: an empty quoted argument still counts, or the argument after it would read
	// as the value of the flag before it.
	hasArgument := false
	backslashes := 0
	for _, character := range commandLine {
		if character == '\\' {
			backslashes++
			hasArgument = true
			continue
		}
		if character == '"' {
			current.WriteString(strings.Repeat(`\`, backslashes/2))
			if backslashes%2 == 1 {
				current.WriteRune(character)
			} else {
				insideQuotes = !insideQuotes
			}
			backslashes = 0
			hasArgument = true
			continue
		}

		current.WriteString(strings.Repeat(`\`, backslashes))
		backslashes = 0
		if insideQuotes || (character != ' ' && character != '\t') {
			current.WriteRune(character)
			hasArgument = true
			continue
		}
		if hasArgument {
			args = append(args, current.String())
			current.Reset()
			hasArgument = false
		}
	}

	current.WriteString(strings.Repeat(`\`, backslashes))
	if hasArgument {
		args = append(args, current.String())
	}
	return args
}

// isUnityHubCommandLineTool reports whether a Windows command line is Unity Hub's helper process, measured as
// `"<UNITY_HUB_DIR>\resources\unity.exe" serve`: a unity.exe right inside a "resources" directory that runs a
// subcommand rather than Editor flags, which all start with "-".
// Why so narrow: leaving a real Editor out of the list makes launch delete its lockfile and Temp and open a
// second Editor, while keeping the helper only reports Unreachable and fails `launch -q`. A Unity command line
// tool placed outside Unity Hub is not recognized and stays listed as an Editor of unknown project, which is the
// less harmful side.
// Why not filepath: the tests run on macOS and Linux too, where filepath does not split on "\".
func isUnityHubCommandLineTool(args []string) bool {
	if len(args) < 2 || strings.HasPrefix(args[1], "-") {
		return false
	}
	executableDirectory, executableName := splitLastWindowsPathElement(args[0])
	if !strings.EqualFold(executableName, "unity.exe") {
		return false
	}
	_, parentDirectoryName := splitLastWindowsPathElement(executableDirectory)
	return strings.EqualFold(parentDirectoryName, "resources")
}

// splitLastWindowsPathElement splits a path at its last "\" or "/" into the directory and the final element.
func splitLastWindowsPathElement(windowsPath string) (string, string) {
	separatorIndex := strings.LastIndexAny(windowsPath, `\/`)
	if separatorIndex < 0 {
		return "", windowsPath
	}
	return windowsPath[:separatorIndex], windowsPath[separatorIndex+1:]
}
