package unityprocess

import "strings"

// splitWindowsCommandLine splits a Windows command line into arguments the way programs read it: whitespace
// separates them outside double quotes, and backslashes only escape when they come before a double quote.
// Why not parse quotes alone: Go and Unity Hub quote a path ending in a backslash as "C:\dir\\", so reading
// that backslash literally leaves the quote open and swallows every flag after the path.
// An empty quoted argument is dropped, which no caller needs to tell apart.
func splitWindowsCommandLine(commandLine string) []string {
	var args []string
	var current strings.Builder
	insideQuotes := false
	backslashes := 0
	for _, character := range commandLine {
		if character == '\\' {
			backslashes++
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
			continue
		}

		current.WriteString(strings.Repeat(`\`, backslashes))
		backslashes = 0
		if insideQuotes || (character != ' ' && character != '\t') {
			current.WriteRune(character)
			continue
		}
		if current.Len() > 0 {
			args = append(args, current.String())
			current.Reset()
		}
	}

	current.WriteString(strings.Repeat(`\`, backslashes))
	if current.Len() > 0 {
		args = append(args, current.String())
	}
	return args
}
