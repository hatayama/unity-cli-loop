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
